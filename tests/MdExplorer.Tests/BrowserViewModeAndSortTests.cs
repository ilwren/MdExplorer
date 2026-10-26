using MdExplorer.Domain;
using MdExplorer.UI.Services;
using MdExplorer.UI.ViewModels;
using Xunit;

namespace MdExplorer.Tests;

/// <summary>视图模式三态与详细信息列头排序（无头构造 Shell：不进 InitializeAsync，只驱动状态机）。</summary>
public sealed class BrowserViewModeAndSortTests : IDisposable
{
    private readonly string _dataDir = Directory.CreateTempSubdirectory("mdexplorer-vm-").FullName;
    private readonly string _startDir = Directory.CreateTempSubdirectory("mdexplorer-vmstart-").FullName;

    public void Dispose()
    {
        foreach (var dir in new[] { _dataDir, _startDir })
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (IOException) { }
        }
    }

    private ShellViewModel CreateShell() => AppHost.CreateShell(AppHost.Build(_dataDir, _startDir));

    [Fact]
    public void ViewModeCycleCoversThreeModesAndPersists()
    {
        var shell = CreateShell();
        var browser = shell.Browser;
        Assert.True(browser.IsListView); // 默认列表

        browser.ToggleViewModeCommand.Execute(null);
        Assert.True(browser.IsGridView);
        Assert.Equal("Grid", shell.SettingsStore.Settings.ViewMode);

        browser.ToggleViewModeCommand.Execute(null);
        Assert.True(browser.IsDetailsView);
        Assert.False(browser.IsListView);
        Assert.False(browser.IsGridView);
        Assert.Equal("Details", shell.SettingsStore.Settings.ViewMode);

        browser.ToggleViewModeCommand.Execute(null);
        Assert.True(browser.IsListView);
        Assert.Equal("List", shell.SettingsStore.Settings.ViewMode);
    }

    [Fact]
    public void SetViewModeCommandAcceptsModeNames()
    {
        var shell = CreateShell();
        shell.Browser.SetViewModeCommand.Execute("Details");
        Assert.Equal(BrowserViewMode.Details, shell.Browser.ViewMode);
        shell.Browser.SetViewModeCommand.Execute("whatever");
        Assert.Equal(BrowserViewMode.List, shell.Browser.ViewMode); // 未知值回退列表
    }

    [Fact]
    public async Task PersistedDetailsModeIsRestoredOnConstruction()
    {
        var first = CreateShell();
        first.Browser.ViewMode = BrowserViewMode.Details;
        Assert.Equal("Details", first.SettingsStore.Settings.ViewMode);
        // VM 的保存是即发即忘；显式落盘后再以同数据目录重建，验证解析侧恢复
        await first.SettingsStore.SaveSettingsAsync(CancellationToken.None);

        var second = CreateShell();
        Assert.Equal(BrowserViewMode.Details, second.Browser.ViewMode);
        Assert.True(second.Browser.IsDetailsView);
    }

    [Fact]
    public void ColumnSortTogglesDirectionAndGlyphFollows()
    {
        var shell = CreateShell();
        var browser = shell.Browser;

        browser.ApplyColumnSort(SortField.Size); // 新字段 → 升序
        Assert.Equal("Size", shell.SettingsStore.Settings.SortField);
        Assert.Equal("Ascending", shell.SettingsStore.Settings.SortDirection);
        Assert.Equal("↑", browser.SizeSortGlyph);
        Assert.Equal("", browser.NameSortGlyph);

        browser.ApplyColumnSort(SortField.Size); // 同字段 → 翻转
        Assert.Equal("Descending", shell.SettingsStore.Settings.SortDirection);
        Assert.Equal("↓", browser.SizeSortGlyph);

        browser.ApplyColumnSort(SortField.Name); // 换字段 → 回到升序
        Assert.Equal("Ascending", shell.SettingsStore.Settings.SortDirection);
        Assert.Equal("↑", browser.NameSortGlyph);
        Assert.Equal("", browser.SizeSortGlyph);
        Assert.Equal("名称", browser.SortFieldLabel);
    }
}
