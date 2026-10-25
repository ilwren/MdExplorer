using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
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
            await Task.Delay(120);
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
        shell.Browser.IsGridView = true;
        await shell.Browser.WhenIdleAsync();
    });

    [AvaloniaFact]
    public async Task Expanded_BrowserPreview() => await CaptureAsync("04-expanded-1100x760.png", 1100, 760);

    [AvaloniaFact]
    public async Task Large_BrowserGridPreview() => await CaptureAsync("05-large-1440x900.png", 1440, 900, async shell =>
    {
        shell.Browser.IsGridView = true;
        await shell.Browser.WhenIdleAsync();
    });

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
}
