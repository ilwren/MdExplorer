using MdExplorer.UI.Services;
using MdExplorer.UI.ViewModels;
using Xunit;

namespace MdExplorer.Tests;

/// <summary>XL 双窗格的第二会话、跨窗格传输与窄屏会话收纳。</summary>
public sealed class DualPaneBrowserTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("eidoria-dual-pane-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private ShellViewModel CreateShell()
    {
        var dataDir = Directory.CreateDirectory(Path.Combine(_root, Guid.NewGuid().ToString("N"), "data")).FullName;
        var startDir = Directory.CreateDirectory(Path.Combine(_root, Guid.NewGuid().ToString("N"), "start")).FullName;
        var shell = AppHost.CreateShell(AppHost.Build(dataDir, startDir));
        shell.Browser.SetWatchEnabled(false);
        shell.SecondaryBrowser.SetWatchEnabled(false);
        shell.Page = ShellPage.Browser;
        return shell;
    }

    private string CreateFile(string relativeDirectory, string fileName, string content)
    {
        var dir = Directory.CreateDirectory(Path.Combine(_root, relativeDirectory)).FullName;
        var path = Path.Combine(dir, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    private static FileItemViewModel Select(BrowserViewModel browser, string name)
    {
        var item = Assert.Single(browser.Items, candidate => candidate.Name == name);
        browser.SelectedItems.Clear();
        browser.SelectedItems.Add(item);
        return item;
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var ct = TestContext.Current.CancellationToken;
        for (var i = 0; i < 100 && !condition(); i++)
            await Task.Delay(20, ct);
        Assert.True(condition());
    }

    [Fact]
    public async Task DualPaneRequiresExtraLargeAndKeepsIndependentSessions()
    {
        var shell = CreateShell();
        var leftFile = CreateFile("left", "left.txt", "left");
        var rightFile = CreateFile("right", "right.txt", "right");
        var leftDir = Path.GetDirectoryName(leftFile)!;
        var rightDir = Path.GetDirectoryName(rightFile)!;
        await shell.Browser.NavigateToAsync(leftDir);

        shell.SetViewportSize(1440, 900);
        await shell.ToggleDualPaneCommand.ExecuteAsync(null);
        Assert.False(shell.IsDualPaneEnabled);
        Assert.False(shell.ShowDualPane);

        shell.SettingsStore.Settings.DualPanePath = rightDir;
        shell.SetViewportSize(1920, 1080);
        await shell.ToggleDualPaneCommand.ExecuteAsync(null);

        Assert.True(shell.IsDualPaneEnabled);
        Assert.True(shell.ShowDualPane);
        Assert.Equal(rightDir, shell.SecondaryBrowser.CurrentPath);
        Assert.Same(shell.SecondaryBrowser, shell.ActiveBrowser);

        _ = Select(shell.Browser, "left.txt");
        _ = Select(shell.SecondaryBrowser, "right.txt");
        Assert.Equal("left.txt", Assert.Single(shell.Browser.SelectedItems).Name);
        Assert.Equal("right.txt", Assert.Single(shell.SecondaryBrowser.SelectedItems).Name);
        await WaitForAsync(() => shell.Preview.CurrentItem?.FullPath == rightFile);

        shell.ActivatePrimaryPane();
        Assert.Same(shell.Browser, shell.ActiveBrowser);
        await WaitForAsync(() => shell.Preview.CurrentItem?.FullPath == leftFile);
        Assert.Equal(rightDir, shell.SettingsStore.Settings.DualPanePath);
    }

    [Fact]
    public async Task CrossPaneCopyAndMoveRefreshBothPanes()
    {
        var ct = TestContext.Current.CancellationToken;
        var shell = CreateShell();
        var copySource = CreateFile("copy-left", "copy.txt", "copy-me");
        var moveSource = CreateFile("move-right", "move.txt", "move-me");
        var leftDir = Path.GetDirectoryName(copySource)!;
        var rightDir = Path.GetDirectoryName(moveSource)!;
        shell.SettingsStore.Settings.DualPanePath = rightDir;
        await shell.Browser.NavigateToAsync(leftDir);
        shell.SetViewportSize(1920, 1080);
        await shell.ToggleDualPaneCommand.ExecuteAsync(null);

        _ = Select(shell.Browser, "copy.txt");
        Assert.True(shell.CanTransferPrimaryToSecondary);
        await shell.CopyPrimaryToSecondaryCommand.ExecuteAsync(null);

        var copied = Path.Combine(rightDir, "copy.txt");
        Assert.Equal("copy-me", await File.ReadAllTextAsync(copied, ct));
        Assert.Contains(shell.SecondaryBrowser.Items, item => item.Name == "copy.txt");

        _ = Select(shell.SecondaryBrowser, "move.txt");
        Assert.True(shell.CanTransferSecondaryToPrimary);
        await shell.MoveSecondaryToPrimaryCommand.ExecuteAsync(null);

        Assert.False(File.Exists(moveSource));
        Assert.Equal("move-me", await File.ReadAllTextAsync(Path.Combine(leftDir, "move.txt"), ct));
        Assert.Contains(shell.Browser.Items, item => item.Name == "move.txt");
        Assert.DoesNotContain(shell.SecondaryBrowser.Items, item => item.Name == "move.txt");
    }

    [Fact]
    public async Task LeavingExtraLargeRetainsSecondarySessionAsBackgroundTab()
    {
        var shell = CreateShell();
        var leftFile = CreateFile("fallback-left", "a.txt", "a");
        var rightFile = CreateFile("fallback-right", "b.txt", "b");
        var leftDir = Path.GetDirectoryName(leftFile)!;
        var rightDir = Path.GetDirectoryName(rightFile)!;
        shell.SettingsStore.Settings.DualPanePath = rightDir;
        await shell.Browser.NavigateToAsync(leftDir);
        shell.SetViewportSize(1920, 1080);
        await shell.ToggleDualPaneCommand.ExecuteAsync(null);
        Assert.DoesNotContain(shell.Browser.Tabs, tab => tab.Path == rightDir);

        shell.SetViewportSize(1440, 900);

        Assert.True(shell.IsDualPaneEnabled);
        Assert.False(shell.ShowDualPane);
        Assert.False(shell.IsSecondaryPaneActive);
        Assert.Same(shell.Browser, shell.ActiveBrowser);
        Assert.Equal(rightDir, shell.SecondaryBrowser.CurrentPath);
        Assert.Contains(shell.Browser.Tabs, tab => tab.Path == rightDir && !tab.IsActive);

        shell.SetViewportSize(1920, 1080);
        Assert.True(shell.ShowDualPane);
        Assert.Equal(rightDir, shell.SecondaryBrowser.CurrentPath);
    }

    [Fact]
    public async Task SecondaryNavigationDoesNotOverwritePrimaryTabPersistence()
    {
        var shell = CreateShell();
        var leftFile = CreateFile("persist-left", "a.txt", "a");
        var rightFile = CreateFile("persist-right", "b.txt", "b");
        var leftDir = Path.GetDirectoryName(leftFile)!;
        var rightDir = Path.GetDirectoryName(rightFile)!;
        await shell.Browser.NavigateToAsync(leftDir);
        await WaitForAsync(() => shell.SettingsStore.Settings.OpenTabPaths.Contains(leftDir));

        await shell.SecondaryBrowser.NavigateToAsync(rightDir, pushHistory: false);
        await shell.SecondaryBrowser.WhenIdleAsync();
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(leftDir, shell.Browser.CurrentPath);
        Assert.Equal(rightDir, shell.SecondaryBrowser.CurrentPath);
        Assert.Contains(leftDir, shell.SettingsStore.Settings.OpenTabPaths);
        Assert.DoesNotContain(rightDir, shell.SettingsStore.Settings.OpenTabPaths);
        Assert.Equal(rightDir, shell.SettingsStore.Settings.DualPanePath);
    }
}
