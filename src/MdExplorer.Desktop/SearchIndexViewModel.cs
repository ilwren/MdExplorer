using System.Collections.ObjectModel;

namespace MdExplorer.Desktop;

/// <summary>UI model for the v3 search/index planning preview. Data is deliberately synthetic.</summary>
public sealed class SearchIndexViewModel : ObservableObject
{
    private string _searchText = "";
    private string _searchMode = "文件名";
    private string _typeFilter = "全部类型";
    private string _statusText = "演示数据 · 尚未连接本地索引服务";
    private string _indexStateText = "索引维护已暂停（演示）";
    private string _indexSizeText = "1.28 GB";
    private string _indexedItemsText = "128,480";
    private bool _isClearConfirmationVisible;
    private bool _isIndexPaused = true;
    private bool _includeHome = true;
    private bool _includeDriveD = true;

    public SearchIndexViewModel()
    {
        AllResults = new ObservableCollection<SearchResult>
        {
            new("▧", "海岸线-晨光.jpg", "图片 · 3.1 MB", "此电脑 / 图片 / 旅行 / 海边", "…清晨的海面泛着金色的光，远处有低矮的岛屿和…", "图片"),
            new("▤", "项目提案.md", "Markdown · 18 KB", "此电脑 / 文档 / 项目 / 2026", "…离线优先的文件管理体验，包含本地搜索与可访问性…", "文档"),
            new("▧", "野花样本-04.png", "图片 · 2.4 MB", "D: / 素材 / 植物 / 春季", "…紫色花瓣、绿色叶片，背景是虚化的草地…", "图片"),
            new("▤", "会议记录.txt", "文本 · 6 KB", "此电脑 / 文档 / 会议", "…需要在周五之前完成评审，并确认索引根目录…", "文档"),
            new("▧", "山间步道.webp", "图片 · 1.7 MB", "D: / 图片 / 徒步", "…林间步道沿着山脊延伸，午后有薄雾…", "图片")
        };
        Results = new ObservableCollection<SearchResult>(AllResults);
    }

    private ObservableCollection<SearchResult> AllResults { get; }
    public ObservableCollection<SearchResult> Results { get; }

    public string SearchText
    {
        get => _searchText;
        set { if (SetField(ref _searchText, value)) ApplySearch(); }
    }
    public string SearchMode
    {
        get => _searchMode;
        set { if (SetField(ref _searchMode, value)) ApplySearch(); }
    }
    public string TypeFilter
    {
        get => _typeFilter;
        set { if (SetField(ref _typeFilter, value)) ApplySearch(); }
    }
    public bool IncludeHome
    {
        get => _includeHome;
        set { if (SetField(ref _includeHome, value)) ApplySearch(); }
    }
    public bool IncludeDriveD
    {
        get => _includeDriveD;
        set { if (SetField(ref _includeDriveD, value)) ApplySearch(); }
    }
    public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }
    public string IndexStateText { get => _indexStateText; private set => SetField(ref _indexStateText, value); }
    public string IndexSizeText { get => _indexSizeText; private set => SetField(ref _indexSizeText, value); }
    public string IndexedItemsText { get => _indexedItemsText; private set => SetField(ref _indexedItemsText, value); }
    public bool IsClearConfirmationVisible
    {
        get => _isClearConfirmationVisible;
        set => SetField(ref _isClearConfirmationVisible, value);
    }
    public bool IsIndexPaused => _isIndexPaused;
    public string ModeNote => SearchMode == "正文" ? "正在匹配已索引正文片段" : "正在匹配文件名与路径";
    public string ResultCount => $"{Results.Count} 个结果";

    public void SetType(string type) => TypeFilter = type;

    public void TogglePause()
    {
        _isIndexPaused = !_isIndexPaused;
        OnPropertyChanged(nameof(IsIndexPaused));
        IndexStateText = _isIndexPaused ? "索引维护已暂停（演示）" : "索引维护运行中（演示）";
        StatusText = _isIndexPaused ? "演示：已暂停索引维护" : "演示：已恢复索引维护";
    }

    public void RebuildIndex() => StatusText = "演示：已将重建请求加入概念任务队列；未扫描磁盘";

    public void RequestClear() => IsClearConfirmationVisible = true;
    public void CancelClear() => IsClearConfirmationVisible = false;

    public void ConfirmClear()
    {
        IsClearConfirmationVisible = false;
        StatusText = "演示：清空确认已演示；没有真实索引或文件被删除";
        IndexSizeText = "0 B";
        IndexedItemsText = "0";
        IndexStateText = "索引数据为空（演示状态）";
    }

    private void ApplySearch()
    {
        var query = SearchText.Trim();
        var filtered = AllResults.Where(result =>
            (result.Path.StartsWith("此电脑", StringComparison.Ordinal) ? IncludeHome : result.Path.StartsWith("D:", StringComparison.OrdinalIgnoreCase) ? IncludeDriveD : true) &&
            (TypeFilter == "全部类型" || result.Category == TypeFilter) &&
            (query.Length == 0 || (SearchMode == "正文"
                ? result.Excerpt.Contains(query, StringComparison.CurrentCultureIgnoreCase) || result.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                : result.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) || result.Path.Contains(query, StringComparison.CurrentCultureIgnoreCase))))
            .ToArray();
        Results.Clear();
        foreach (var item in filtered) Results.Add(item);
        OnPropertyChanged(nameof(ResultCount));
        OnPropertyChanged(nameof(ModeNote));
    }
}

public sealed record SearchResult(string Glyph, string Name, string Meta, string Path, string Excerpt, string Category);
