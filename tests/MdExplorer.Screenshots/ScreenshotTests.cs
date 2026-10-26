using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using MdExplorer.UI.Services;
using MdExplorer.UI.ViewModels;
using MdExplorer.UI.Views;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(MdExplorer.Screenshots.TestAppBuilder))]

namespace MdExplorer.Screenshots;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<MdExplorer.UI.App>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false,
                ShouldRenderOnUIThread = true,
            });
}

/// <summary>
/// 断点截图生成（兼作 UI 冒烟测试）：以真实视图在无头 Skia 下渲染五档断点与关键页面，
/// PNG 写入 BaseDirectory/screenshots 供 CI 归档（见 ci.yml 的 ui-screenshots 工件）。
/// </summary>
public sealed class ScreenshotTests
{
    private static readonly string SampleRoot = SampleData.Create();
    private static string PicturesDir => Path.Combine(SampleRoot, "Pictures");

    private static string OutputDir
    {
        get
        {
            var dir = Environment.GetEnvironmentVariable("MDEXPLORER_SCREENSHOTS")
                      ?? Path.Combine(AppContext.BaseDirectory, "screenshots");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private async Task<ShellViewModel> CreateShellAsync(string themeMode = "Light")
    {
        var dataDir = Path.Combine(Path.GetTempPath(), "mdexplorer-shots", Guid.NewGuid().ToString("N"));
        var services = AppHost.Build(dataDirectory: dataDir, startFolder: PicturesDir);
        services.SettingsStore.Settings.ThemeMode = themeMode;
        services.SettingsStore.Settings.SeedColor = "#006A6A";
        ThemeService.Apply(services.SettingsStore.Settings);
        var shell = AppHost.CreateShell(services);
        shell.ThemeSettingsChanged += (_, _) => ThemeService.Apply(services.SettingsStore.Settings);
        await shell.InitializeAsync();
        await shell.OpenFolderAsync(PicturesDir);
        await shell.Browser.WhenIdleAsync();
        // 选中一张照片让预览出图
        var photo = shell.Browser.Items.FirstOrDefault(i => i.Name.StartsWith("IMG_", StringComparison.Ordinal));
        if (photo is not null)
        {
            shell.Browser.SelectedItems.Clear();
            shell.Browser.SelectedItems.Add(photo);
            for (int i = 0; i < 60 && shell.Preview.Image is null; i++)
                await Task.Delay(50);
        }
        return shell;
    }

    private async Task CaptureAsync(string fileName, double width, double height, Func<ShellViewModel, Task>? arrange = null, string themeMode = "Light")
    {
        var shell = await CreateShellAsync(themeMode);
        shell.SetViewportSize(width, height);
        if (arrange is not null)
            await arrange(shell);

        var window = new Window
        {
            Width = width,
            Height = height,
            Content = new MainView { DataContext = shell },
        };
        window.Show();
        try
        {
            for (int i = 0; i < 40 && shell.Browser.Items.All(x => x.Thumbnail is null); i++)
                await Task.Delay(50);
            await Task.Delay(120);
            window.CaptureRenderedFrame(); // 先热一帧让容器实现/布局稳定
            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame!.Save(Path.Combine(OutputDir, fileName));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Compact_BrowserList() => await CaptureAsync("01-compact-390x844.png", 390, 844);

    [AvaloniaFact]
    public async Task Compact_Home() => await CaptureAsync("02-compact-home.png", 390, 844, shell =>
    {
        shell.GoHomeCommand.Execute(null);
        return Task.CompletedTask;
    });

    [AvaloniaFact]
    public async Task Medium_BrowserGrid() => await CaptureAsync("03-medium-768x1024.png", 768, 1024, async shell =>
    {
        shell.Browser.ViewMode = BrowserViewMode.Grid;
        await shell.Browser.WhenIdleAsync();
    });

    [AvaloniaFact]
    public async Task Expanded_BrowserPreview() => await CaptureAsync("04-expanded-1100x760.png", 1100, 760);

    [AvaloniaFact]
    public async Task Large_BrowserGridPreview() => await CaptureAsync("05-large-1440x900.png", 1440, 900, async shell =>
    {
        shell.Browser.ViewMode = BrowserViewMode.Grid;
        await shell.Browser.WhenIdleAsync();
    });

    [AvaloniaFact]
    public async Task Large_DetailsView() => await CaptureAsync("15-large-details.png", 1440, 900, async shell =>
    {
        shell.Browser.ViewMode = BrowserViewMode.Details;
        await shell.Browser.WhenIdleAsync();
    });

    /// <summary>拖放进行中的投放提示浮层（原生 DnD 无法在无头环境完整驱动，这里直接呈现浮层做视觉冒烟）。</summary>
    [AvaloniaFact]
    public async Task Large_DropHint()
    {
        var shell = await CreateShellAsync();
        shell.SetViewportSize(1440, 900);
        var view = new MainView { DataContext = shell };
        var window = new Window { Width = 1440, Height = 900, Content = view };
        window.Show();
        try
        {
            await Task.Delay(150);
            var hint = view.FindControl<Border>("DropHint");
            var text = view.FindControl<TextBlock>("DropHintText");
            Assert.NotNull(hint);
            Assert.NotNull(text);
            text!.Text = "释放以复制到「Pictures」";
            hint!.IsVisible = true;
            await Task.Delay(120);
            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame!.Save(Path.Combine(OutputDir, "16-large-drop-hint.png"));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ExtraLarge_Browser() => await CaptureAsync("06-xlarge-1920x1080.png", 1920, 1080);

    [AvaloniaFact]
    public async Task Large_Dark() => await CaptureAsync("07-large-dark.png", 1440, 900, themeMode: "Dark");

    [AvaloniaFact]
    public async Task Settings_Page() => await CaptureAsync("08-settings-1100x760.png", 1100, 760, shell =>
    {
        shell.GoSettingsCommand.Execute(null);
        return Task.CompletedTask;
    });
    /// <summary>弹出层验证：上下文菜单（依赖 ContextMenu/MenuItem 主题齐备）。</summary>
    [AvaloniaFact]
    public async Task Large_ContextMenu_Open()
    {
        var shell = await CreateShellAsync();
        shell.SetViewportSize(1440, 900);
        var view = new MainView { DataContext = shell };
        var window = new Window { Width = 1440, Height = 900, Content = view };
        window.Show();
        try
        {
            await Task.Delay(150);
            var list = view.FindControl<ListBox>("FileList");
            Assert.NotNull(list);
            Assert.NotNull(list!.ContextMenu);
            list.ContextMenu!.Open(list);
            await Task.Delay(150);
            Assert.True(list.ContextMenu.IsOpen);
            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame!.Save(Path.Combine(OutputDir, "09-large-contextmenu.png"));
            list.ContextMenu.Close();
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>弹出层验证：窄屏 FAB 的新建菜单（Flyout 呈现）。</summary>
    [AvaloniaFact]
    public async Task Compact_FabFlyout_Open()
    {
        var shell = await CreateShellAsync();
        shell.SetViewportSize(390, 844);
        var view = new MainView { DataContext = shell };
        var window = new Window { Width = 390, Height = 844, Content = view };
        window.Show();
        try
        {
            await Task.Delay(150);
            var fab = view.FindControl<Button>("FabCompact");
            Assert.NotNull(fab);
            Assert.NotNull(fab!.Flyout);
            fab.Flyout!.ShowAt(fab);
            await Task.Delay(150);
            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame!.Save(Path.Combine(OutputDir, "10-compact-fab-menu.png"));
            fab.Flyout.Hide();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Expanded_TrashPage() => await CaptureAsync("11-expanded-trash.png", 1100, 760, shell =>
    {
        shell.GoTrashCommand.Execute(null);
        return Task.CompletedTask;
    });
    /// <summary>定点探针：图片解码管线（缩略图 + 预览大图）在无头环境真实可用。</summary>
    [AvaloniaFact]
    public async Task Thumbnails_AndPreview_Decode()
    {
        var png = Path.Combine(PicturesDir, "screenshot_ide.png");
        Assert.True(File.Exists(png), "sample png missing");
        using (var direct = new Bitmap(png))
            Assert.True(direct.PixelSize.Width > 0, "direct Bitmap decode failed");

        var shell = await CreateShellAsync();
        await shell.Browser.WhenIdleAsync();
        var withThumbs = shell.Browser.Items.Where(i => i.Thumbnail is not null).Select(i => i.Name).ToArray();
        Assert.True(withThumbs.Length > 0,
            "no thumbnails decoded; kinds=" + string.Join(",", shell.Browser.Items.Select(i => i.Name + ":" + i.Kind)));
        Assert.NotNull(shell.Preview.Image);
    }
    private static async Task SelectByNameAsync(ShellViewModel shell, string name, Func<bool> ready)
    {
        var item = shell.Browser.Items.First(i => i.Name == name);
        shell.Browser.SelectedItems.Clear();
        shell.Browser.SelectedItems.Add(item);
        for (int i = 0; i < 80 && !ready(); i++)
            await Task.Delay(50);
    }

    [AvaloniaFact]
    public async Task Large_MarkdownPreview() => await CaptureAsync("12-large-markdown.png", 1440, 900,
        shell => SelectByNameAsync(shell, "README.md", () => shell.Preview.MarkdownText is not null));

    [AvaloniaFact]
    public async Task Large_CodePreview() => await CaptureAsync("13-large-code.png", 1440, 900,
        shell => SelectByNameAsync(shell, "Program.cs", () => shell.Preview.ShowSource));

    [AvaloniaFact]
    public async Task Expanded_BatchRenameDialog() => await CaptureAsync("14-expanded-batch-rename.png", 1100, 760, async shell =>
    {
        var names = shell.Browser.Items.Where(i => !i.Model.IsFolder).Take(4).Select(i => i.Name).ToArray();
        Assert.True(names.Length >= 2);
        _ = shell.BatchRenameAsync(names, []); // 不 await：保持对话框打开供截图
        await Task.Delay(150);
    });
}
