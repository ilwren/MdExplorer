using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using System.Security.Cryptography;
using MdExplorer.Domain;
using MdExplorer.Platform.Abstractions;
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

    private async Task<ShellViewModel> CreateShellAsync(string themeMode = "Light", bool androidAccess = false)
    {
        var dataDir = Path.Combine(Path.GetTempPath(), "mdexplorer-shots", Guid.NewGuid().ToString("N"));
        var built = AppHost.Build(dataDirectory: dataDir, startFolder: PicturesDir);
        var services = androidAccess
            ? built with
            {
                Trash = new AppTrashService(dataDir, built.StorageFileSystem),
                Capabilities = new PlatformCapabilities(
                    CanMoveToTrash: true,
                    CanBrowseTrash: true,
                    IsAndroid: true,
                    IsDesktop: false,
                    UsesAppTrash: true),
            }
            : built;
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

    private async Task CaptureAsync(
        string fileName,
        double width,
        double height,
        Func<ShellViewModel, Task>? arrange = null,
        string themeMode = "Light",
        bool androidAccess = false)
    {
        var shell = await CreateShellAsync(themeMode, androidAccess);
        shell.SetViewportSize(width, height);
        if (arrange is not null)
            await arrange(shell);

        Window window = !androidAccess && width >= 840
            ? new MainWindow { Width = width, Height = height, DataContext = shell }
            : new Window
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
            // 截图夹具的收尾关闭不走用户确认；真实关闭守卫由专门用例覆盖。
            shell.DocumentWorkbench.Clear();
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
    public async Task Large_TwoTabs() => await CaptureAsync("17-large-tabs.png", 1440, 900, async shell =>
    {
        await shell.Browser.OpenInNewTabAsync(Path.Combine(PicturesDir, "Camera"));
        await shell.Browser.ActivateTabCommand.ExecuteAsync(shell.Browser.Tabs[0]);
        shell.Browser.ViewMode = BrowserViewMode.Grid;
        await shell.Browser.WhenIdleAsync();
        Assert.Equal(2, shell.Browser.Tabs.Count);
        Assert.Equal("Pictures", shell.Browser.ActiveTab!.Title);
    });

    [AvaloniaFact]
    public async Task Large_HomePolished() => await CaptureAsync("21-large-home-polished.png", 1440, 900, async shell =>
    {
        if (shell.Browser.SelectedItems.FirstOrDefault() is { } item)
            await shell.Browser.RecordRecentFileAsync(item.FullPath);
        shell.GoHomeCommand.Execute(null);
        await Task.Delay(180);
    });

    [AvaloniaFact]
    public async Task Large_PdfPreview() => await CaptureAsync("18-large-pdf.png", 1440, 900, async shell =>
    {
        await SelectByNameAsync(shell, "设计规范 v3.pdf", () => shell.Preview.HasPdf && shell.Preview.Image is not null);
        Assert.Equal(2, shell.Preview.PdfPageCount);
        await shell.Preview.NextPdfPageCommand.ExecuteAsync(null);
        Assert.Equal(1, shell.Preview.PdfPageIndex);
        Assert.NotNull(shell.Preview.Image);
    });

    [AvaloniaFact]
    public async Task Large_DocumentWorkbenchText() => await CaptureAsync("38-large-document-editor.png", 1440, 900, async shell =>
    {
        await OpenDocumentByNameAsync(shell, "Program.cs");
        var workbench = shell.DocumentWorkbench;
        Assert.Equal(ShellPage.DocumentWorkbench, shell.Page);
        Assert.True(workbench.IsTextDocument);
        workbench.BeginEditCommand.Execute(null);
        workbench.EditorText += "\n// Eidoria 文档工作台编辑中\n";
        Assert.True(workbench.IsDirty);
    });

    [AvaloniaFact]
    public async Task Large_DocumentWorkbenchPdf()
    {
        var shell = await CreateShellAsync();
        shell.SetViewportSize(1440, 900);
        await OpenDocumentByNameAsync(shell, "设计规范 v3.pdf");
        var workbench = shell.DocumentWorkbench;
        Assert.Equal(ShellPage.DocumentWorkbench, shell.Page);
        Assert.Equal(2, workbench.PdfPageCount);
        Assert.Equal(2, workbench.PdfPages.Count);
        Assert.NotNull(workbench.PdfImage);
        workbench.NextPdfPageCommand.Execute(null);
        workbench.ZoomInCommand.Execute(null);
        for (int i = 0; i < 80 && workbench.IsLoading; i++)
            await Task.Delay(50);
        Assert.Equal(1, workbench.PdfPageIndex);
        Assert.Equal(125, workbench.ZoomPercent);
        Assert.NotNull(workbench.PdfImage);

        var window = new MainWindow { Width = 1440, Height = 900, DataContext = shell };
        var view = Assert.IsType<MainView>(window.Content);
        window.Show();
        try
        {
            ListBox? pageList = null;
            Control? firstPage = null;
            Control? secondPage = null;
            for (int i = 0; i < 100; i++)
            {
                window.CaptureRenderedFrame();
                // DocumentWorkbenchView 有独立 NameScope，MainView.FindControl 跨不过去；按视觉树与控件名定位。
                pageList = view.GetVisualDescendants()
                    .OfType<ListBox>()
                    .FirstOrDefault(control => control.Name == "PdfPageList");
                firstPage = pageList?.ContainerFromIndex(0);
                secondPage = pageList?.ContainerFromIndex(1);
                if (firstPage is not null && secondPage is not null &&
                    workbench.PdfPages.All(page => page.Thumbnail is not null))
                    break;
                await Task.Delay(50);
            }

            Assert.NotNull(pageList);
            Assert.Equal(2, pageList!.ItemCount);
            Assert.True(pageList.Bounds.Width > 100 && pageList.Bounds.Height > 300,
                $"PDF 缩略图轨应有真实布局尺寸，实际 {pageList.Bounds}。");
            Assert.NotNull(firstPage);
            Assert.NotNull(secondPage);
            Assert.Same(workbench.PdfPages[0], firstPage!.DataContext);
            Assert.Same(workbench.PdfPages[1], secondPage!.DataContext);
            Assert.True(firstPage.Bounds.Width > 80 && firstPage.Bounds.Height > 100,
                $"第一页缩略图容器应已实现，实际 {firstPage.Bounds}。");
            Assert.All(workbench.PdfPages, page => Assert.NotNull(page.Thumbnail));
            Assert.Contains(firstPage.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "1");
            Assert.Contains(secondPage.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "2");
            Assert.Contains(firstPage.GetVisualDescendants().OfType<Image>(), image => image.Source is not null);
            Assert.Contains(secondPage.GetVisualDescendants().OfType<Image>(), image => image.Source is not null);

            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame!.Save(Path.Combine(OutputDir, "39-large-pdf-workbench.png"));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Compact_DocumentWorkbenchText() => await CaptureAsync("40-compact-document-editor.png", 390, 844, async shell =>
    {
        await OpenDocumentByNameAsync(shell, "README.md");
        var workbench = shell.DocumentWorkbench;
        Assert.Equal(ShellPage.DocumentWorkbench, shell.Page);
        Assert.True(workbench.IsCompactLayout);
        Assert.True(workbench.IsMarkdown);
        workbench.BeginEditCommand.Execute(null);
        Assert.True(workbench.IsEditing);
    });

    [AvaloniaFact]
    public async Task Compact_DocumentWorkbenchPdf() => await CaptureAsync("41-compact-pdf-workbench.png", 390, 844, async shell =>
    {
        await OpenDocumentByNameAsync(shell, "设计规范 v3.pdf");
        var workbench = shell.DocumentWorkbench;
        Assert.Equal(ShellPage.DocumentWorkbench, shell.Page);
        Assert.True(workbench.IsCompactLayout);
        Assert.Equal(2, workbench.PdfPageCount);
        Assert.NotNull(workbench.PdfImage);
        workbench.NextPdfPageCommand.Execute(null);
        for (int i = 0; i < 80 && workbench.IsLoading; i++)
            await Task.Delay(50);
        Assert.Equal(1, workbench.PdfPageIndex);
        Assert.NotNull(workbench.PdfImage);
    });

    [AvaloniaFact]
    public async Task Large_DocumentWorkbenchPdfContinuous() => await CaptureAsync(
        "45-large-pdf-continuous.png", 1440, 900, async shell =>
    {
        await OpenDocumentByNameAsync(shell, "设计规范 v3.pdf");
        var workbench = shell.DocumentWorkbench;
        workbench.UseContinuousPdfModeCommand.Execute(null);
        workbench.ZoomOutCommand.Execute(null);
        workbench.ZoomOutCommand.Execute(null);
        for (int i = 0; i < 100 && workbench.PdfPages.Any(page => page.PageImage is null); i++)
            await Task.Delay(50);

        Assert.True(workbench.IsContinuousPdfMode);
        Assert.Equal(50, workbench.ZoomPercent);
        Assert.Equal(0, workbench.PdfPageIndex);
        Assert.All(workbench.PdfPages, page => Assert.NotNull(page.PageImage));
    });

    [AvaloniaFact]
    public async Task Compact_DocumentWorkbenchPdfContinuous() => await CaptureAsync(
        "46-compact-pdf-continuous.png", 390, 844, async shell =>
    {
        await OpenDocumentByNameAsync(shell, "设计规范 v3.pdf");
        var workbench = shell.DocumentWorkbench;
        workbench.UseContinuousPdfModeCommand.Execute(null);
        for (int i = 0; i < 100 && workbench.PdfPages.Any(page => page.PageImage is null); i++)
            await Task.Delay(50);

        Assert.True(workbench.IsCompactLayout);
        Assert.True(workbench.IsContinuousPdfMode);
        Assert.All(workbench.PdfPages, page => Assert.NotNull(page.PageImage));
    });

    [AvaloniaFact]
    public async Task Large_DocumentWorkbenchPdfContinuousScrollSync()
    {
        var shell = await CreateShellAsync();
        shell.SetViewportSize(1440, 900);
        await OpenDocumentByNameAsync(shell, "设计规范 v3.pdf");
        var workbench = shell.DocumentWorkbench;
        workbench.UseContinuousPdfModeCommand.Execute(null);
        workbench.ZoomOutCommand.Execute(null);
        workbench.ZoomOutCommand.Execute(null);
        for (int i = 0; i < 100 && workbench.PdfPages.Any(page => page.PageImage is null); i++)
            await Task.Delay(50);
        Assert.All(workbench.PdfPages, page => Assert.NotNull(page.PageImage));

        var window = new MainWindow { Width = 1440, Height = 900, DataContext = shell };
        var view = Assert.IsType<MainView>(window.Content);
        window.Show();
        try
        {
            ListBox? list = null;
            Control? firstPage = null;
            ScrollViewer? scroller = null;
            for (int i = 0; i < 100; i++)
            {
                window.CaptureRenderedFrame();
                list = view.GetVisualDescendants()
                    .OfType<ListBox>()
                    .FirstOrDefault(control => control.Name == "PdfContinuousList");
                firstPage = list?.ContainerFromIndex(0);
                scroller = list?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
                if (list is not null && firstPage is not null && scroller is not null &&
                    list.Bounds.Height > 300 && firstPage.Bounds.Height > 100)
                    break;
                await Task.Delay(50);
            }

            Assert.NotNull(list);
            Assert.NotNull(firstPage);
            Assert.NotNull(scroller);
            Assert.Same(workbench.PdfPages[0], firstPage!.DataContext);
            Assert.Contains(firstPage.GetVisualDescendants().OfType<Image>(), image => image.Source is not null);

            scroller!.Offset = new Vector(0, firstPage.Bounds.Height);
            for (int i = 0; i < 100 && workbench.PdfPageIndex != 1; i++)
            {
                window.CaptureRenderedFrame();
                await Task.Delay(50);
            }
            Assert.Equal(1, workbench.PdfPageIndex);
            var secondPage = list!.ContainerFromIndex(1);
            Assert.NotNull(secondPage);
            Assert.Same(workbench.PdfPages[1], secondPage!.DataContext);
            Assert.Contains(secondPage.GetVisualDescendants().OfType<Image>(), image => image.Source is not null);

            workbench.PreviousPdfPageCommand.Execute(null);
            for (int i = 0; i < 100; i++)
            {
                window.CaptureRenderedFrame();
                firstPage = list.ContainerFromIndex(0);
                var top = firstPage?.TranslatePoint(new Point(0, 0), list)?.Y;
                if (workbench.PdfPageIndex == 0 && top is > -20 and < 80)
                    break;
                await Task.Delay(50);
            }
            Assert.Equal(0, workbench.PdfPageIndex);
            Assert.NotNull(firstPage);
            var firstTop = firstPage!.TranslatePoint(new Point(0, 0), list)?.Y;
            Assert.True(firstTop is > -20 and < 80, $"上一页命令应把第一页滚回视口顶部，实际 Y={firstTop}。");
        }
        finally
        {
            workbench.Clear();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Large_UnsavedDocumentCloseGuard()
    {
        var shell = await CreateShellAsync();
        shell.SetViewportSize(1440, 900);
        await OpenDocumentByNameAsync(shell, "Program.cs");
        var workbench = shell.DocumentWorkbench;
        workbench.BeginEditCommand.Execute(null);
        workbench.EditorText += "\n// 未保存更改\n";
        Assert.True(workbench.NeedsCloseConfirmation);

        var window = new MainWindow { Width = 1440, Height = 900, DataContext = shell };
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.Show();
        try
        {
            window.Close();
            for (int i = 0; i < 80 && !shell.IsDialogOpen; i++)
            {
                window.CaptureRenderedFrame();
                await Task.Delay(50);
            }

            Assert.True(shell.IsDialogOpen);
            Assert.True(window.IsVisible);
            Assert.False(closed);
            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame!.Save(Path.Combine(OutputDir, "47-large-unsaved-close-dialog.png"));

            shell.DialogCancelCommand.Execute(null);
            for (int i = 0; i < 40 && shell.IsDialogOpen; i++)
                await Task.Delay(25);
            Assert.False(shell.IsDialogOpen);
            Assert.True(window.IsVisible);
            Assert.False(closed);
            await Task.Delay(100);

            window.Close();
            for (int i = 0; i < 80 && !shell.IsDialogOpen; i++)
                await Task.Delay(50);
            Assert.True(shell.IsDialogOpen);
            shell.DialogConfirmCommand.Execute(null);
            for (int i = 0; i < 80 && !closed; i++)
                await Task.Delay(50);
            Assert.True(closed);
            Assert.False(window.IsVisible);
        }
        finally
        {
            if (window.IsVisible)
            {
                workbench.Clear();
                window.Close();
            }
        }
    }

    [AvaloniaFact]
    public async Task Large_AutomationEditor()
    {
        var shell = await CreateShellAsync();
        shell.SetViewportSize(1440, 900);
        shell.GoAutomationCommand.Execute(null);
        Assert.Equal(ShellPage.Automation, shell.Page);
        Assert.True(shell.Automation.IsWorkflowValid, shell.Automation.ValidationText);
        Assert.Equal(4, shell.Automation.Nodes.Count);
        Assert.Equal(3, shell.Automation.Connections.Count);

        var window = new MainWindow { Width = 1440, Height = 900, DataContext = shell };
        var view = Assert.IsType<MainView>(window.Content);
        window.Show();
        try
        {
            Control? editor = null;
            Control[] nodeControls = [];
            for (int i = 0; i < 100; i++)
            {
                window.CaptureRenderedFrame();
                editor = view.GetVisualDescendants()
                    .OfType<Control>()
                    .FirstOrDefault(control => control.Name == "AutomationEditor");
                nodeControls = view.GetVisualDescendants()
                    .OfType<Control>()
                    .Where(control => control.GetType().FullName == "NodifyM.Avalonia.Controls.Node")
                    .ToArray();
                if (editor is not null && editor.Bounds.Width > 500 && nodeControls.Length == 4)
                    break;
                await Task.Delay(50);
            }

            Assert.NotNull(editor);
            Assert.True(editor!.Bounds.Width > 500 && editor.Bounds.Height > 400,
                $"自动化画布应有真实布局尺寸，实际 {editor.Bounds}。");
            Assert.Equal(4, nodeControls.Length);
            Assert.All(nodeControls, node => Assert.True(
                node.Bounds.Width > 100 && node.Bounds.Height > 40,
                $"节点容器应已实现，实际 {node.Bounds}。"));

            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame!.Save(Path.Combine(OutputDir, "42-large-automation.png"));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Compact_AutomationEditor() => await CaptureAsync("43-compact-automation.png", 390, 844, shell =>
    {
        shell.GoAutomationCommand.Execute(null);
        Assert.Equal(ShellPage.Automation, shell.Page);
        Assert.True(shell.Automation.IsWorkflowValid, shell.Automation.ValidationText);
        return Task.CompletedTask;
    });

    [AvaloniaFact]
    public async Task Large_AutomationEditorDark() => await CaptureAsync(
        "44-large-automation-dark.png", 1440, 900,
        shell =>
        {
            shell.GoAutomationCommand.Execute(null);
            Assert.Equal(ShellPage.Automation, shell.Page);
            return Task.CompletedTask;
        },
        themeMode: "Dark");

    [AvaloniaFact]
    public async Task Large_AddressBar() => await CaptureAsync("19-large-address.png", 1440, 900, shell =>
    {
        shell.Browser.BeginEditAddressCommand.Execute(null);
        Assert.True(shell.Browser.IsAddressEditing);
        return Task.CompletedTask;
    });

    [AvaloniaFact]
    public async Task Large_TaskCenter()
    {
        var shell = await CreateShellAsync();
        shell.SetViewportSize(1440, 900);
        var window = new Window
        {
            Width = 1440,
            Height = 900,
            Content = new MainView { DataContext = shell },
        };
        window.Show();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await Task.Delay(120);
            var running = shell.RunOperationAsync("正在复制 128 个项目…", async (progress, ct) =>
            {
                progress.Report(new OperationProgress(0.46, "IMG_20261026_081500.jpg", 59, 128));
                await release.Task.WaitAsync(ct);
                return new OperationOutcome(128, [], []);
            });
            var queued = shell.RunOperationAsync("正在压缩为 ZIP…", (_, _) =>
                Task.FromResult(new OperationOutcome(1, [], [])));
            shell.IsTaskCenterOpen = true;
            await Task.Delay(180);
            window.CaptureRenderedFrame();
            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame!.Save(Path.Combine(OutputDir, "20-large-task-center.png"));

            release.TrySetResult();
            Assert.True((await running).Success);
            Assert.True((await queued).Success);
        }
        finally
        {
            release.TrySetResult();
            window.Close();
        }
    }

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

    /// <summary>回归：文件行 ⋮ 按钮必须以自身挂载的 ContextMenu 打开菜单，不能对未挂载控件强行 Open。</summary>
    [AvaloniaFact]
    public async Task Large_ItemMoreButton_OpensContextMenu()
    {
        var shell = await CreateShellAsync();
        shell.SetViewportSize(1440, 900);
        var view = new MainView { DataContext = shell };
        var window = new Window { Width = 1440, Height = 900, Content = view };
        window.Show();
        try
        {
            await Task.Delay(150);
            window.CaptureRenderedFrame();
            var list = view.FindControl<ListBox>("FileList");
            Assert.NotNull(list);
            var moreButton = list!.GetVisualDescendants()
                .OfType<Button>()
                .First(button => AutomationProperties.GetName(button) == "更多操作" && button.IsEffectivelyVisible);
            var item = Assert.IsType<FileItemViewModel>(moreButton.DataContext);
            var clickPoint = moreButton.TranslatePoint(
                new Point(moreButton.Bounds.Width / 2, moreButton.Bounds.Height / 2), window);
            Assert.NotNull(clickPoint);

            window.MouseMove(clickPoint!.Value);
            window.MouseDown(clickPoint.Value, MouseButton.Left);
            window.MouseUp(clickPoint.Value, MouseButton.Left);
            await Task.Delay(150);

            Assert.NotNull(moreButton.ContextMenu);
            Assert.True(moreButton.ContextMenu!.IsOpen);
            Assert.Same(item, Assert.Single(shell.Browser.SelectedItems));
            moreButton.ContextMenu.Close();
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>指针回归：在真实桌面窗口顶部横向拖动文件标签超过相邻标签中点后，模型顺序应真实交换。</summary>
    [AvaloniaFact]
    public async Task Large_TabDrag_ReordersTabs()
    {
        var shell = await CreateShellAsync();
        shell.SetViewportSize(1440, 900);
        await shell.Browser.OpenInNewTabAsync(Path.Combine(PicturesDir, "Camera"));
        var originalOrder = shell.Browser.Tabs.Select(tab => tab.Path).ToArray();
        Assert.Equal(2, originalOrder.Length);

        var window = new MainWindow { Width = 1440, Height = 900, DataContext = shell };
        var view = Assert.IsType<MainView>(window.Content);
        window.Show();
        try
        {
            await Task.Delay(150);
            window.CaptureRenderedFrame();
            var strip = view.FindControl<ItemsControl>("DesktopTabStrip");
            Assert.NotNull(strip);
            var first = strip!.ContainerFromIndex(0);
            var second = strip.ContainerFromIndex(1);
            Assert.NotNull(first);
            Assert.NotNull(second);
            var firstCenter = first!.TranslatePoint(
                new Point(first.Bounds.Width / 2, first.Bounds.Height / 2), window);
            var secondCenter = second!.TranslatePoint(
                new Point(second.Bounds.Width / 2, second.Bounds.Height / 2), window);
            Assert.NotNull(firstCenter);
            Assert.NotNull(secondCenter);
            Assert.True(firstCenter!.Value.Y < 48, $"桌面标签应位于窗口顶部 48px Chrome 内，实际中心为 {firstCenter.Value}。");

            var start = firstCenter.Value;
            var end = new Point(secondCenter!.Value.X + 24, secondCenter.Value.Y);
            window.MouseMove(start);
            window.MouseDown(start, MouseButton.Left);
            for (var step = 1; step <= 6; step++)
            {
                var point = new Point(
                    start.X + (end.X - start.X) * step / 6,
                    start.Y + (end.Y - start.Y) * step / 6);
                // Headless 的 MouseMove 不会自动继承 MouseDown 的按钮状态，需显式带左键修饰。
                window.MouseMove(point, RawInputModifiers.LeftMouseButton);
                await Task.Delay(20);
            }
            var dragTransform = first.RenderTransform as Avalonia.Media.TranslateTransform;
            Assert.True(dragTransform is { X: > 50 },
                $"拖拽中首个标签应有横向位移；Bounds={first.Bounds}，Transform={first.RenderTransform}，Start={start}，End={end}。");
            window.MouseUp(end, MouseButton.Left);
            await Task.Delay(150);

            Assert.Equal(originalOrder.Reverse(), shell.Browser.Tabs.Select(tab => tab.Path));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>窗口 Chrome 回归：Expanded+ 用顶部标签，桌面窄窗保留自有窗口按钮，最大化时关闭缩放热区并切换还原语义。</summary>
    [AvaloniaFact]
    public async Task MainWindowChrome_AdaptsToWindowSizeAndState()
    {
        var shell = await CreateShellAsync();
        shell.SetViewportSize(1440, 900);
        var window = new MainWindow { Width = 1440, Height = 900, DataContext = shell };
        var view = Assert.IsType<MainView>(window.Content);
        window.Show();
        try
        {
            await Task.Delay(150);
            window.CaptureRenderedFrame();

            var chrome = view.FindControl<Border>("DesktopWindowChrome");
            var desktopTabs = view.FindControl<Control>("DesktopTabScroller");
            var workspaceTabs = view.FindControl<Border>("WorkspaceTabStripHost");
            var compactControls = view.FindControl<StackPanel>("CompactWindowControls");
            var resizeGrips = view.FindControl<Grid>("WindowResizeGrips");
            var selectionBar = view.FindControl<Border>("SelectionModeBar");
            var maximizeButton = view.FindControl<Control>("WindowMaximizeButton");

            Assert.NotNull(chrome);
            Assert.NotNull(desktopTabs);
            Assert.NotNull(workspaceTabs);
            Assert.NotNull(compactControls);
            Assert.NotNull(resizeGrips);
            Assert.NotNull(selectionBar);
            Assert.NotNull(maximizeButton);

            Assert.True(chrome!.IsVisible);
            Assert.True(desktopTabs!.IsVisible);
            Assert.False(workspaceTabs!.IsVisible);
            Assert.False(compactControls!.IsVisible);
            Assert.True(resizeGrips!.IsVisible);
            Assert.Equal("最大化窗口", AutomationProperties.GetName(maximizeButton!));

            shell.GoSettingsCommand.Execute(null);
            await Task.Delay(50);
            window.CaptureRenderedFrame();
            Assert.True(chrome.IsVisible);
            Assert.False(desktopTabs.IsVisible);

            await shell.OpenFolderAsync(PicturesDir);
            await shell.Browser.WhenIdleAsync();
            window.Width = 768;
            window.Height = 900;
            shell.SetViewportSize(768, 900);
            await Task.Delay(100);
            window.CaptureRenderedFrame();

            Assert.False(chrome.IsVisible);
            Assert.True(workspaceTabs.IsVisible);
            Assert.True(compactControls.IsVisible);
            Assert.Equal(120, selectionBar!.Margin.Right);

            window.Width = 1440;
            window.Height = 900;
            shell.SetViewportSize(1440, 900);
            window.WindowState = WindowState.Maximized;
            await Task.Delay(100);
            window.CaptureRenderedFrame();

            Assert.True(chrome.IsVisible);
            Assert.False(resizeGrips.IsVisible);
            Assert.Equal("还原窗口", AutomationProperties.GetName(maximizeButton));
        }
        finally
        {
            window.WindowState = WindowState.Normal;
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
    private static async Task OpenDocumentByNameAsync(ShellViewModel shell, string name)
    {
        var item = shell.Browser.Items.First(i => i.Name == name);
        await shell.OpenDocumentWorkbenchAsync(item.Model);
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

    [AvaloniaFact]
    public async Task Compact_FloatingPreview() => await CaptureAsync("22-compact-floating-preview.png", 390, 844, shell =>
    {
        shell.IsPreviewPageOpen = true;
        Assert.True(shell.IsPreviewPageOpen);
        Assert.Equal(0, shell.Preview.SelectedTabIndex);
        return Task.CompletedTask;
    });

    [AvaloniaFact]
    public async Task Large_SearchIslandPanel() => await CaptureAsync("23-large-search-island.png", 1440, 900, async shell =>
    {
        shell.Browser.FilterText = "IMG_";
        shell.Browser.OpenSearchPanelCommand.Execute(null);
        await shell.Browser.WhenIdleAsync();
        Assert.True(shell.Browser.IsSearchPanelOpen);
        Assert.Equal("IMG_", shell.Browser.SearchIslandText);
    });

    [AvaloniaFact]
    public async Task Large_SearchCenter() => await CaptureAsync("25-large-search-center.png", 1440, 900, async shell =>
    {
        await shell.GoSearchCenterCommand.ExecuteAsync(null);
        Assert.True(await shell.AddSearchIndexRootAsync(SampleRoot));
        await shell.RebuildSearchIndexCommand.ExecuteAsync(null);
        shell.SearchCenterQuery = "IMG_";
        shell.SearchCenterKindFilter = "Image";
        await shell.RunSearchCenterSearchCommand.ExecuteAsync(null);
        shell.DismissSnackbarCommand.Execute(null);
        Assert.True(shell.SearchIndexIsCurrent);
        Assert.NotEmpty(shell.SearchCenterResults);
    });

    [AvaloniaFact]
    public async Task Compact_SearchCenter() => await CaptureAsync("26-compact-search-center.png", 390, 844, async shell =>
    {
        await shell.GoSearchCenterCommand.ExecuteAsync(null);
        Assert.True(await shell.AddSearchIndexRootAsync(SampleRoot));
        await shell.RebuildSearchIndexCommand.ExecuteAsync(null);
        shell.SearchCenterQuery = "IMG_";
        shell.SearchCenterKindFilter = "Image";
        await shell.RunSearchCenterSearchCommand.ExecuteAsync(null);
        shell.DismissSnackbarCommand.Execute(null);
        Assert.True(shell.SearchIndexIsCurrent);
        Assert.NotEmpty(shell.SearchCenterResults);
    });

    [AvaloniaFact]
    public async Task Large_StorageInsights() => await CaptureAsync("27-large-storage-insights.png", 1440, 900, async shell =>
    {
        await shell.GoStorageInsightsCommand.ExecuteAsync(null);
        Assert.True(shell.TrySetStorageRoot(SampleRoot));
        await shell.AnalyzeStorageCommand.ExecuteAsync(null);
        await shell.FindStorageDuplicatesCommand.ExecuteAsync(null);
        shell.DismissSnackbarCommand.Execute(null);
        Assert.True(shell.HasStorageAnalysis);
        Assert.True(shell.HasStorageDuplicateScan);
        Assert.NotEmpty(shell.StorageTopFolders);
        Assert.NotEmpty(shell.StorageLargestFiles);
    });

    [AvaloniaFact]
    public async Task Compact_StorageInsights() => await CaptureAsync("28-compact-storage-insights.png", 390, 844, async shell =>
    {
        await shell.GoStorageInsightsCommand.ExecuteAsync(null);
        Assert.True(shell.TrySetStorageRoot(SampleRoot));
        await shell.AnalyzeStorageCommand.ExecuteAsync(null);
        shell.DismissSnackbarCommand.Execute(null);
        Assert.True(shell.HasStorageAnalysis);
        Assert.NotEmpty(shell.StorageKindUsages);
        Assert.NotEmpty(shell.StorageLargestFiles);
    });

    [AvaloniaFact]
    public async Task Large_ArchiveWorkbench() => await CaptureAsync(
        "29-large-archive-workbench.png", 1440, 900, ArrangeArchiveWorkbenchAsync);

    [AvaloniaFact]
    public async Task Compact_ArchiveWorkbench() => await CaptureAsync(
        "30-compact-archive-workbench.png", 390, 844, ArrangeArchiveWorkbenchAsync);

    private static async Task ArrangeArchiveWorkbenchAsync(ShellViewModel shell)
    {
        var archivePath = Path.Combine(PicturesDir, "backup-2026.zip");
        await shell.OpenArchiveWorkbenchAsync(archivePath);
        var docs = Assert.Single(shell.ArchiveVisibleEntries, row => row.Name == "docs");
        shell.NavigateArchiveFolder(docs.VirtualPath);

        var readme = Assert.Single(shell.ArchiveVisibleEntries, row => row.Name == "readme.txt");
        await shell.SelectArchiveEntryAsync(readme);
        var config = Assert.Single(shell.ArchiveVisibleEntries, row => row.Name == "config.json");
        config.IsChecked = true;
        shell.DismissSnackbarCommand.Execute(null);

        Assert.True(shell.HasArchive);
        Assert.Equal("docs/", shell.ArchiveCurrentFolder);
        Assert.Contains("ZIP 工作台样例", shell.ArchivePreviewText, StringComparison.Ordinal);
        Assert.Equal(1, shell.ArchiveSelectedCount);
    }

    [AvaloniaFact]
    public async Task Compact_AndroidAccessManagement() => await CaptureAsync(
        "31-compact-android-access.png", 390, 844, ArrangeAndroidAccessAsync, androidAccess: true);

    [AvaloniaFact]
    public async Task Compact_AndroidAppTrash() => await CaptureAsync(
        "32-compact-android-app-trash.png", 390, 844, ArrangeAndroidAppTrashAsync, androidAccess: true);

    [AvaloniaFact]
    public async Task Large_TransferConflictDialog() => await CaptureAsync(
        "33-large-conflict-dialog.png", 1440, 900, ArrangeTransferConflictAsync);

    [AvaloniaFact]
    public async Task Compact_TransferConflictDialog() => await CaptureAsync(
        "34-compact-conflict-dialog.png", 390, 844, ArrangeTransferConflictAsync);

    [AvaloniaFact]
    public async Task Large_TaskDetails() => await CaptureAsync(
        "35-large-task-details.png", 1440, 900, ArrangeTaskDetailsAsync);

    [AvaloniaFact]
    public async Task Compact_TaskDetailsSheet() => await CaptureAsync(
        "36-compact-task-sheet.png", 390, 844, ArrangeTaskDetailsAsync);

    [AvaloniaFact]
    public async Task ExtraLarge_DualPane() => await CaptureAsync(
        "37-xlarge-dual-pane.png", 1920, 1080, ArrangeDualPaneAsync);

    private static async Task ArrangeDualPaneAsync(ShellViewModel shell)
    {
        var cameraDir = Path.Combine(PicturesDir, "Camera");
        shell.SettingsStore.Settings.DualPanePath = cameraDir;
        await shell.ToggleDualPaneCommand.ExecuteAsync(null);
        await shell.SecondaryBrowser.WhenIdleAsync();

        Assert.True(shell.ShowDualPane);
        Assert.Equal(cameraDir, shell.SecondaryBrowser.CurrentPath);
        Assert.Same(shell.SecondaryBrowser, shell.ActiveBrowser);

        var photo = shell.SecondaryBrowser.Items.First(
            item => item.Name.StartsWith("IMG_", StringComparison.Ordinal));
        shell.SecondaryBrowser.SelectedItems.Clear();
        shell.SecondaryBrowser.SelectedItems.Add(photo);
        for (var i = 0; i < 60 && shell.Preview.Image is null; i++)
            await Task.Delay(50);
        Assert.Equal(photo.FullPath, shell.Preview.CurrentItem?.FullPath);
    }

    private static Task ArrangeTransferConflictAsync(ShellViewModel shell)
    {
        _ = shell.AskConflictAsync(CreateConflictPrompt());
        Assert.True(shell.IsConflictDialog);
        Assert.NotNull(shell.CurrentConflict);
        Assert.True(shell.IsConflictKeepBoth);
        Assert.False(shell.ConflictApplyToRemaining);
        Assert.Equal(3, shell.PendingConflictCount);
        return Task.CompletedTask;
    }

    private static TransferConflictPrompt CreateConflictPrompt()
    {
        var sourcePath = Path.Combine(PicturesDir, "IMG_20261001.jpg");
        var targetPath = Path.Combine(PicturesDir, "Camera", "IMG_0001.png");
        FileItem MakeItem(string path) => new(
            FileLocator.FromPath(path),
            "IMG_20261001.jpg",
            FileKind.Image,
            new FileInfo(path).Length,
            new FileInfo(path).LastWriteTime,
            IsHidden: false,
            IsSymlink: false,
            ".jpg");
        string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

        var source = new ConflictItemSnapshot(
            "来源",
            MakeItem(sourcePath),
            PicturesDir,
            Hash(sourcePath),
            new Bitmap(sourcePath));
        var target = new ConflictItemSnapshot(
            "目标已有",
            MakeItem(targetPath),
            Path.Combine(PicturesDir, "Camera"),
            Hash(targetPath),
            new Bitmap(targetPath));
        return new TransferConflictPrompt(source, target, Index: 0, TotalCount: 3, IsMove: false);
    }

    private static async Task ArrangeTaskDetailsAsync(ShellViewModel shell)
    {
        await shell.RunOperationAsync("正在解压 backup-2026.zip…", (_, _) =>
            Task.FromResult(new OperationOutcome(
                12,
                [],
                [Path.Combine(PicturesDir, "README.md")],
                SkippedCount: 1)));
        await shell.RunOperationAsync(
            "正在复制 4 个项目…",
            (_, _) => Task.FromResult(new OperationOutcome(
                2,
                [new OperationError(
                    "旅行视频.mp4",
                    "目标文件夹暂时不可写",
                    Path.Combine(PicturesDir, "旅行视频.mp4"))],
                [
                    Path.Combine(PicturesDir, "IMG_20261001.jpg"),
                    Path.Combine(PicturesDir, "screenshot_ide.png"),
                ],
                SkippedCount: 1)),
            _ => Task.CompletedTask);
        shell.IsTaskCenterOpen = true;
        shell.DismissSnackbarCommand.Execute(null);

        var failed = Assert.Single(shell.OperationTasks, task => task.IsFailed);
        Assert.True(failed.IsDetailsExpanded);
        Assert.True(failed.CanRetry);
        Assert.Equal(1, shell.FailedOperationTaskCount);
    }

    private static Task ArrangeAndroidAccessAsync(ShellViewModel shell)
    {
        shell.AuthorizedRoots.Clear();
        shell.AuthorizedRoots.Add(new AuthorizedRootModel(
            "Download",
            "content://com.android.externalstorage.documents/tree/primary%3ADownload",
            "bookmark-download",
            IsAvailable: true,
            IsPersistent: true));
        shell.AuthorizedRoots.Add(new AuthorizedRootModel(
            "相机备份",
            "content://com.android.externalstorage.documents/tree/primary%3ADCIM",
            Bookmark: null,
            IsAvailable: true,
            IsPersistent: false));
        shell.AuthorizedRoots.Add(new AuthorizedRootModel(
            "旧 SD 卡",
            string.Empty,
            "bookmark-stale",
            IsAvailable: false,
            IsPersistent: true));
        SeedAndroidTrashState(shell);
        shell.Page = ShellPage.Settings;

        Assert.True(shell.Capabilities.IsAndroid);
        Assert.Equal(2, shell.AvailableAuthorizedRootCount);
        Assert.Equal(1, shell.UnavailableAuthorizedRootCount);
        Assert.True(shell.HasTrashQuota);
        return Task.CompletedTask;
    }

    private static Task ArrangeAndroidAppTrashAsync(ShellViewModel shell)
    {
        SeedAndroidTrashState(shell);
        shell.Page = ShellPage.Trash;

        Assert.True(shell.Capabilities.UsesAppTrash);
        Assert.Equal("应用内回收站", shell.TrashTitle);
        Assert.Equal(4, shell.TrashItems.Count);
        Assert.True(shell.HasTrashQuota);
        return Task.CompletedTask;
    }

    private static void SeedAndroidTrashState(ShellViewModel shell)
    {
        shell.TrashItems.Clear();
        shell.TrashItems.Add(new TrashItemModel(
            "sample-video",
            "旅行视频.mp4",
            "content://com.android.externalstorage.documents/tree/primary%3ADownload/document/primary%3ADownload%2F旅行视频.mp4",
            "10月28日 09:42",
            IsFolder: false,
            SizeBytes: 512L * 1024 * 1024));
        shell.TrashItems.Add(new TrashItemModel(
            "sample-folder",
            "相机备份",
            "content://com.android.externalstorage.documents/tree/primary%3ADCIM/document/primary%3ADCIM%2F相机备份",
            "10月27日 21:18",
            IsFolder: true,
            SizeBytes: 210L * 1024 * 1024));
        shell.TrashItems.Add(new TrashItemModel(
            "sample-archive",
            "设计稿 v2.zip",
            "content://com.android.externalstorage.documents/tree/primary%3ADownload/document/primary%3ADownload%2F设计稿%20v2.zip",
            "10月26日 16:05",
            IsFolder: false,
            SizeBytes: 18L * 1024 * 1024));
        shell.TrashItems.Add(new TrashItemModel(
            "sample-note",
            "会议记录.md",
            "content://com.android.externalstorage.documents/tree/primary%3ADownload/document/primary%3ADownload%2F会议记录.md",
            "10月25日 11:30",
            IsFolder: false,
            SizeBytes: 24L * 1024));
        shell.TrashUsedBytes = shell.TrashItems.Sum(item => item.SizeBytes ?? 0);
        shell.TrashQuotaBytes = AppTrashService.DefaultQuotaBytes;
        shell.TrashRetentionDays = AppTrashService.DefaultRetentionDays;
        shell.TrashUsedPercent = shell.TrashUsedBytes * 100.0 / AppTrashService.DefaultQuotaBytes;
    }

    /// <summary>桌面溢出菜单：三列分组 + 限高滚动的呈现冒烟。</summary>
    [AvaloniaFact]
    public async Task Large_OverflowMenu_Open()
    {
        var shell = await CreateShellAsync();
        shell.SetViewportSize(1440, 900);
        var view = new MainView { DataContext = shell };
        var window = new Window { Width = 1440, Height = 900, Content = view };
        window.Show();
        try
        {
            await Task.Delay(150);
            var button = view.FindControl<Button>("OverflowButton");
            Assert.NotNull(button);
            Assert.NotNull(button!.Flyout);
            button.Flyout!.ShowAt(button);
            await Task.Delay(150);
            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame!.Save(Path.Combine(OutputDir, "24-large-overflow-menu.png"));
            button.Flyout.Hide();
        }
        finally
        {
            window.Close();
        }
    }
}
