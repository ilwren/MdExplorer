using MdExplorer.UI.Services;
using MdExplorer.UI.ViewModels;
using Xunit;

namespace MdExplorer.Tests;

/// <summary>v0.11：灵动岛式搜索面板状态、返回优先级、预览双页签与窄屏悬浮预览开关。</summary>
public sealed class SearchIslandAndPreviewTabsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("mdexplorer-search-island-").FullName;
    private readonly string _dataDir;
    private readonly string _startDir;
    private readonly string _childDir;
    private readonly string _needlePath;
    private readonly string _plainPath;

    public SearchIslandAndPreviewTabsTests()
    {
        _dataDir = Directory.CreateDirectory(Path.Combine(_root, "data")).FullName;
        _startDir = Directory.CreateDirectory(Path.Combine(_root, "start")).FullName;
        _childDir = Directory.CreateDirectory(Path.Combine(_startDir, "Child")).FullName;
        _needlePath = Path.Combine(_childDir, "needle.txt");
        _plainPath = Path.Combine(_startDir, "plain.txt");
        File.WriteAllText(_needlePath, "needle");
        File.WriteAllText(_plainPath, "plain");
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
        return shell;
    }

    [Fact]
    public async Task SearchPanelFiltersThenRecursesAndBackClosesPanelBeforeExitingSearch()
    {
        var shell = CreateShell();
        var browser = shell.Browser;
        await shell.OpenFolderAsync(_startDir);

        browser.OpenSearchPanelCommand.Execute(null);
        Assert.True(browser.IsSearchPanelOpen);
        Assert.False(browser.HasActiveSearch);
        Assert.Equal("搜索", browser.SearchIslandText);
        Assert.Equal(string.Empty, browser.SearchResultBadgeText);
        Assert.Contains("当前列表筛选", browser.SearchPanelScopeText, StringComparison.Ordinal);

        browser.FilterText = "needle";
        Assert.True(browser.HasActiveSearch);
        Assert.Equal("needle", browser.SearchIslandText);
        Assert.Equal("0", browser.SearchResultBadgeText);
        Assert.Equal("当前列表匹配 0 项", browser.SearchPanelStatusText);
        Assert.Empty(browser.Items); // needle 只在子文件夹里，列表筛选看不到

        await browser.SearchRecursiveFromPanelCommand.ExecuteAsync(null);
        Assert.False(browser.IsSearchPanelOpen);
        Assert.True(browser.IsSearchMode);
        Assert.Equal("搜索到 1 项", browser.SearchIslandText);
        Assert.Equal("1", browser.SearchResultBadgeText);
        Assert.Equal("搜索到 1 项", browser.SearchPanelStatusText);
        Assert.Contains("递归搜索", browser.SearchPanelScopeText, StringComparison.Ordinal);
        Assert.Equal(_needlePath, Assert.Single(browser.Items).FullPath);

        browser.IsSearchPanelOpen = true;
        Assert.True(shell.WouldHandleBack);
        Assert.True(await shell.TryGoBackAsync());
        Assert.False(browser.IsSearchPanelOpen);
        Assert.True(browser.IsSearchMode); // 第一次返回只收面板，不退出搜索

        Assert.True(await shell.TryGoBackAsync());
        Assert.False(browser.IsSearchMode);
        Assert.Equal(string.Empty, browser.FilterText);
        Assert.Equal(_startDir, browser.CurrentPath);
    }

    [Fact]
    public async Task LeavingBrowserPageClosesSearchPanel()
    {
        var shell = CreateShell();
        await shell.OpenFolderAsync(_startDir);
        shell.Browser.IsSearchPanelOpen = true;

        shell.Page = ShellPage.Home;

        Assert.False(shell.Browser.IsSearchPanelOpen);
    }

    [Fact]
    public async Task PreviewTabsDefaultToContentForFilesAndPropertiesForFolders()
    {
        var shell = CreateShell();
        await shell.OpenFolderAsync(_startDir);
        var folder = Assert.Single(shell.Browser.Items, item => item.Name == "Child");
        var file = Assert.Single(shell.Browser.Items, item => item.Name == "plain.txt");

        await shell.Preview.LoadAsync(folder.Model);
        Assert.Equal(1, shell.Preview.SelectedTabIndex);
        Assert.False(shell.Preview.CanOpen);
        Assert.False(shell.Preview.HasQuickFacts);
        Assert.NotEmpty(shell.Preview.MetadataRows);

        await shell.Preview.LoadAsync(file.Model);
        Assert.Equal(0, shell.Preview.SelectedTabIndex);
        Assert.True(shell.Preview.CanOpen);
        Assert.True(shell.Preview.HasQuickFacts);
        Assert.False(string.IsNullOrWhiteSpace(shell.Preview.QuickSizeText));
        Assert.False(string.IsNullOrWhiteSpace(shell.Preview.QuickModifiedText));
        Assert.False(shell.Preview.HasImageDimensions);
        Assert.Equal("plain", shell.Preview.TextContent);
    }

    [Fact]
    public async Task FloatingPreviewOpensOnlyOnCompactOrMediumAndBackOrderIsPanelThenPreview()
    {
        var shell = CreateShell();
        await shell.OpenFolderAsync(_startDir);
        var file = Assert.Single(shell.Browser.Items, item => item.Name == "plain.txt");

        shell.SetViewportSize(1100, 760);
        shell.OnFileOpened(file);
        Assert.False(shell.IsPreviewPageOpen);

        shell.SetViewportSize(390, 844);
        shell.OnFileOpened(file);
        Assert.True(shell.IsPreviewPageOpen);

        shell.Browser.IsSearchPanelOpen = true;
        Assert.True(await shell.TryGoBackAsync());
        Assert.False(shell.Browser.IsSearchPanelOpen);
        Assert.True(shell.IsPreviewPageOpen);

        Assert.True(await shell.TryGoBackAsync());
        Assert.False(shell.IsPreviewPageOpen);
    }
}
