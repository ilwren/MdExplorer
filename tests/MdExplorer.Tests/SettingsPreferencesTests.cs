using MdExplorer.Infrastructure;
using MdExplorer.UI.Services;
using MdExplorer.UI.ViewModels;
using Xunit;

namespace MdExplorer.Tests;

/// <summary>v0.26：设置页新增的可调项必须真实生效、落盘并夹在支持区间内。</summary>
public sealed class SettingsPreferencesTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("mdexplorer-settings-").FullName;
    private readonly string _dataDir;
    private readonly string _startDir;
    private readonly string _manyDir;

    public SettingsPreferencesTests()
    {
        _dataDir = Directory.CreateDirectory(Path.Combine(_root, "data")).FullName;
        _startDir = Directory.CreateDirectory(Path.Combine(_root, "start")).FullName;
        _manyDir = Directory.CreateDirectory(Path.Combine(_startDir, "Many")).FullName;
        for (var i = 0; i < 150; i++)
            File.WriteAllText(Path.Combine(_manyDir, $"item-{i:000}.txt"), "settings");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private ShellViewModel CreateShell()
    {
        var shell = AppHost.CreateShell(AppHost.Build(_dataDir, _startDir));
        shell.Browser.SetWatchEnabled(false);
        shell.SecondaryBrowser.SetWatchEnabled(false);
        return shell;
    }

    [Fact]
    public void SettingsExposeDocumentedDefaults()
    {
        var shell = CreateShell();

        Assert.Equal(120, shell.ItemPageSize);
        Assert.Equal("120 项/页", shell.ItemPageSizeText);
        Assert.True(shell.AutoLoadMore);
        Assert.True(shell.EnableThumbnails);
        Assert.Equal("Comfortable", shell.Density);
        Assert.True(shell.IsDensityComfortable);
        Assert.True(shell.ConfirmBeforeTrash);
        Assert.True(shell.RestoreTabsOnLaunch);
        Assert.Equal(12, shell.RecentItemLimit);
        Assert.True(shell.IsRecentLimit12);
        Assert.Equal(72d, shell.PullRefreshTriggerDistance);
        Assert.True(shell.IsPullTrigger72);
        Assert.Equal("72 DIP", shell.PullTriggerText);
    }

    [Fact]
    public async Task LoadingSettingsClampsUnsupportedValues()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_dataDir, "settings.json"),
            """
            {
              "SchemaVersion": 1,
              "ItemPageSize": 7,
              "AutoLoadMore": false,
              "EnableThumbnails": false,
              "Density": "dense",
              "RecentItemLimit": 5000,
              "PullRefreshTriggerDistance": 999,
              "RestoreTabsOnLaunch": false
            }
            """,
            TestContext.Current.CancellationToken);

        var store = await SettingsStore.LoadAsync(_dataDir, TestContext.Current.CancellationToken);

        Assert.Equal(60, store.Settings.ItemPageSize);
        Assert.Equal("Comfortable", store.Settings.Density);
        Assert.Equal(60, store.Settings.RecentItemLimit);
        Assert.Equal(120d, store.Settings.PullRefreshTriggerDistance);
        Assert.False(store.Settings.AutoLoadMore);
        Assert.False(store.Settings.EnableThumbnails);
        Assert.False(store.Settings.RestoreTabsOnLaunch);
    }

    [Fact]
    public async Task PageSizeSettingControlsFirstPageAndImmediateFill()
    {
        var shell = CreateShell();
        shell.ItemPageSize = 60;
        Assert.Equal(60, shell.SettingsStore.Settings.ItemPageSize);
        Assert.Equal(60, shell.Browser.PageSize);

        await shell.Browser.NavigateToAsync(_manyDir, pushHistory: false);
        Assert.Equal(60, shell.Browser.Items.Count);
        Assert.Equal(150, shell.Browser.TotalItemCount);
        Assert.Equal("已显示 60 / 150 项", shell.Browser.PagingStatusText);

        // 页数调大后应就地补足到新的一页，而不是等下一次加载。
        shell.ItemPageSize = 120;
        Assert.Equal(120, shell.Browser.Items.Count);
        Assert.True(shell.Browser.HasMoreItems);

        await shell.Browser.LoadMoreItemsCommand.ExecuteAsync(null);
        Assert.Equal(150, shell.Browser.Items.Count);
        Assert.False(shell.Browser.HasMoreItems);
    }

    [Fact]
    public void AutoLoadMoreGateNeedsSettingAndScrollProximity()
    {
        var shell = CreateShell();

        Assert.True(shell.ShouldAutoLoadMore(200));
        Assert.False(shell.ShouldAutoLoadMore(400));

        shell.AutoLoadMore = false;
        Assert.False(shell.SettingsStore.Settings.AutoLoadMore);
        Assert.False(shell.ShouldAutoLoadMore(0));
        Assert.False(shell.ShouldAutoLoadMore(200));
    }

    [Fact]
    public void ThumbnailSettingReachesBothPanes()
    {
        var shell = CreateShell();
        Assert.True(shell.Browser.ThumbnailDecodingEnabled);

        shell.EnableThumbnails = false;
        Assert.False(shell.SettingsStore.Settings.EnableThumbnails);
        Assert.False(shell.Browser.ThumbnailDecodingEnabled);
        Assert.False(shell.SecondaryBrowser.ThumbnailDecodingEnabled);

        shell.EnableThumbnails = true;
        Assert.True(shell.Browser.ThumbnailDecodingEnabled);
        Assert.True(shell.SecondaryBrowser.ThumbnailDecodingEnabled);
    }

    [Fact]
    public void TrashConfirmationToggleNeverWeakensPermanentDelete()
    {
        var shell = CreateShell();
        var browser = shell.Browser;
        Assert.True(browser.RequiresTrashConfirmation);

        shell.ConfirmBeforeTrash = false;
        Assert.False(shell.SettingsStore.Settings.ConfirmBeforeTrash);
        Assert.False(browser.RequiresTrashConfirmation);
        Assert.False(shell.SecondaryBrowser.RequiresTrashConfirmation);

        shell.ConfirmBeforeTrash = true;
        Assert.True(browser.RequiresTrashConfirmation);
    }

    [Fact]
    public async Task RestoreTabsSettingSkipsSavedSession()
    {
        var otherDir = Directory.CreateDirectory(Path.Combine(_startDir, "Other")).FullName;
        var shell = CreateShell();
        shell.SettingsStore.Settings.OpenTabPaths = [_manyDir, otherDir];
        shell.SettingsStore.Settings.ActiveTabIndex = 1;
        shell.RestoreTabsOnLaunch = false;
        Assert.False(shell.SettingsStore.Settings.RestoreTabsOnLaunch);

        await shell.Browser.RestoreTabsAsync(_startDir);

        Assert.Single(shell.Browser.Tabs);
        Assert.Equal(_startDir, shell.Browser.CurrentPath);
    }

    [Fact]
    public async Task RecentLimitTrimsExistingEntriesAndIsPersisted()
    {
        var shell = CreateShell();
        var settings = shell.SettingsStore.Settings;
        for (var i = 0; i < 40; i++)
            settings.RecentFolderPaths.Add(Path.Combine(_startDir, $"folder-{i}"));
        for (var i = 0; i < 40; i++)
            settings.RecentFilePaths.Add(Path.Combine(_startDir, $"file-{i}.txt"));

        shell.RecentItemLimit = 24;

        Assert.Equal(24, shell.SettingsStore.Settings.RecentItemLimit);
        Assert.Equal(24, settings.RecentFolderPaths.Count);
        Assert.Equal(24, settings.RecentFilePaths.Count);
        Assert.Equal("24 条", shell.RecentLimitText);
        Assert.True(shell.IsRecentLimit24);
        Assert.False(shell.IsRecentLimit12);
        Assert.Equal(Path.Combine(_startDir, "folder-0"), settings.RecentFolderPaths[0]);

        // 主页最近访问的刷新是即发即忘；等它读完文件再结束用例，避免临时目录被提前删除。
        await Task.Delay(50, TestContext.Current.CancellationToken);
    }

    [Fact]
    public void DensityAndPullDistanceCommandsAcceptOnlySupportedValues()
    {
        var shell = CreateShell();

        shell.SetDensityCommand.Execute("Compact");
        Assert.Equal("Compact", shell.SettingsStore.Settings.Density);
        Assert.True(shell.IsCompactDensity);
        Assert.True(shell.IsDensityCompact);
        Assert.False(shell.IsDensityComfortable);
        Assert.Equal("紧凑", shell.DensityText);

        shell.SetDensityCommand.Execute("unknown");
        Assert.Equal("Comfortable", shell.Density);

        shell.SetPullRefreshTriggerDistanceCommand.Execute("96");
        Assert.Equal(96d, shell.PullRefreshTriggerDistance);
        Assert.True(shell.IsPullTrigger96);
        Assert.False(shell.IsPullTrigger72);

        shell.SetPullRefreshTriggerDistanceCommand.Execute("999");
        Assert.Equal(120d, shell.PullRefreshTriggerDistance);
        shell.SetPullRefreshTriggerDistanceCommand.Execute("not-a-number");
        Assert.Equal(120d, shell.PullRefreshTriggerDistance);
    }

    [Fact]
    public async Task SettingsSurviveReload()
    {
        var first = CreateShell();
        first.ItemPageSize = 240;
        first.AutoLoadMore = false;
        first.EnableThumbnails = false;
        first.SetDensityCommand.Execute("Compact");
        first.ConfirmBeforeTrash = false;
        first.RestoreTabsOnLaunch = false;
        first.RecentItemLimit = 48;
        first.SetPullRefreshTriggerDistanceCommand.Execute("96");
        await first.SettingsStore.SaveSettingsAsync(TestContext.Current.CancellationToken);

        var second = CreateShell();
        Assert.Equal(240, second.ItemPageSize);
        Assert.False(second.AutoLoadMore);
        Assert.False(second.EnableThumbnails);
        Assert.True(second.IsCompactDensity);
        Assert.False(second.ConfirmBeforeTrash);
        Assert.False(second.RestoreTabsOnLaunch);
        Assert.Equal(48, second.RecentItemLimit);
        Assert.Equal(96d, second.PullRefreshTriggerDistance);
    }
}
