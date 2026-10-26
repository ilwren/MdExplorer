using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MdExplorer.Application;
using MdExplorer.Domain;
using MdExplorer.Platform.Abstractions;

namespace MdExplorer.UI.ViewModels;

/// <summary>一个浏览会话的全部状态：当前位置、条目、选集、搜索、剪贴板与写操作。</summary>
/// <summary>文件区呈现模式（与 AppSettings.ViewMode 字符串一一对应）。</summary>
public enum BrowserViewMode { List, Grid, Details }

public sealed partial class BrowserViewModel : ObservableObject
{
    private readonly IFileSystemService _fileSystem;
    private readonly IFileOperationService _operations;
    private readonly ITrashService _trash;
    private readonly IArchiveService _archive;
    private readonly ISearchService _search;
    private readonly IFileInspectorService _inspector;
    private readonly IFolderWatchService _folderWatch;
    private readonly ISystemClipboardService _systemClipboard;
    private readonly ISettingsStore _settingsStore;
    private readonly IDialogs _dialogs;
    private readonly PreviewViewModel _preview;
    private readonly Func<IFileLauncherService> _launcher;
    private IFileLauncherService Launcher => _launcher();

    // 指向当前标签页的历史栈（切换标签时随 ActiveTab 一起换绑）
    private Stack<string> _backStack;
    private Stack<string> _forwardStack;
    private bool _suppressTabFilterRefilter;
    private List<FileItem> _allItems = [];
    private IDisposable? _watchSubscription;
    private bool _watchEnabled;
    private SortField _currentSortField = SortField.Name;
    private SortDirection _currentSortDirection = SortDirection.Ascending;
    private bool _applyingFolderViewPreferences;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _thumbCts;
    private Task _idleTask = Task.CompletedTask;

    private IReadOnlyList<string> _clipboardPaths = [];
    private bool _clipboardCut;
    private IReadOnlyList<string> _externalClipboardBaseline = [];
    private bool _clipboardOwnedByApp;
    private readonly Stack<ClosedTabSnapshot> _closedTabs = [];

    private sealed record ClosedTabSnapshot(
        string Path,
        string Title,
        string[] BackPaths,
        string[] ForwardPaths,
        string FilterText,
        IReadOnlyList<string> SelectedPaths,
        int Index);

    public BrowserViewModel(
        IFileSystemService fileSystem, IFileOperationService operations, ITrashService trash,
        IArchiveService archive, ISearchService search, ISettingsStore settingsStore,
        IFileInspectorService inspector, IFolderWatchService folderWatch,
        ISystemClipboardService systemClipboard,
        IDialogs dialogs, PreviewViewModel preview, Func<IFileLauncherService> launcher)
    {
        _fileSystem = fileSystem;
        _operations = operations;
        _trash = trash;
        _archive = archive;
        _search = search;
        _inspector = inspector;
        _folderWatch = folderWatch;
        _systemClipboard = systemClipboard;
        _settingsStore = settingsStore;
        _dialogs = dialogs;
        _preview = preview;
        _launcher = launcher;

        var s = settingsStore.Settings;
        _watchEnabled = s.WatchFolderChanges;
        _viewMode = s.ViewMode switch
        {
            "Grid" => BrowserViewMode.Grid,
            "Details" => BrowserViewMode.Details,
            _ => BrowserViewMode.List,
        };
        _currentSortField = ParseSort(s.SortField);
        _currentSortDirection = ParseSortDirection(s.SortDirection);
        _sortFieldLabel = SortFieldText(_currentSortField);

        var initialTab = new BrowserTabViewModel(null, "新标签页") { IsActive = true };
        Tabs.Add(initialTab);
        _activeTab = initialTab;
        _backStack = initialTab.BackStack;
        _forwardStack = initialTab.ForwardStack;
        Tabs.CollectionChanged += (_, _) => UpdateTabMetadata();
        UpdateTabMetadata();

        SelectedItems.CollectionChanged += (_, _) => OnSelectionChanged();
    }

    // ---------- 基本状态 ----------
    [ObservableProperty] private string? _currentPath;
    [ObservableProperty] private string _currentFolderName = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _loadError;

    public bool HasError => LoadError is not null;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private BrowserViewMode _viewMode = BrowserViewMode.List;
    [ObservableProperty] private string _sortFieldLabel = "名称";
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private bool _isSearchMode;
    [ObservableProperty] private bool _isSearchPanelOpen;
    [ObservableProperty] private bool _isSelectionMode;
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private bool _canGoBack;
    [ObservableProperty] private bool _canGoForward;
    [ObservableProperty] private bool _canGoUp;
    [ObservableProperty] private bool _isAddressEditing;
    [ObservableProperty] private string _addressText = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAddressError))]
    private string? _addressError;

    public bool HasAddressError => !string.IsNullOrWhiteSpace(AddressError);

    public ObservableCollection<FileItemViewModel> Items { get; } = [];
    public ObservableCollection<FileItemViewModel> SelectedItems { get; } = [];
    public ObservableCollection<BreadcrumbSegment> Breadcrumbs { get; } = [];

    [ObservableProperty] private BrowserTabViewModel? _activeTab;
    public ObservableCollection<BrowserTabViewModel> Tabs { get; } = [];
    public bool HasMultipleTabs => Tabs.Count > 1;
    public bool HasClosedTabs => _closedTabs.Count > 0;

    /// <summary>最近访问文件夹/文件变化时通知 Shell 刷新主页入口。</summary>
    public event EventHandler? RecentsChanged;

    public bool IsListView => ViewMode == BrowserViewMode.List;
    public bool IsGridView => ViewMode == BrowserViewMode.Grid;
    public bool IsDetailsView => ViewMode == BrowserViewMode.Details;
    public bool HasSelection => SelectedCount > 0;
    public bool HasClipboard => _clipboardPaths.Count > 0;
    public string SelectionText => SelectedCount == 0 ? StatusText : $"已选 {SelectedCount} 项";

    /// <summary>迷你搜索岛是否应显示清除入口。</summary>
    public bool HasActiveSearch => IsSearchMode || !string.IsNullOrWhiteSpace(FilterText);

    /// <summary>迷你搜索岛文案：闲置只说“搜索”，活动中回显关键词或实时结果数。</summary>
    public string SearchIslandText
    {
        get
        {
            var query = FilterText.Trim();
            if (IsSearchMode)
                return string.IsNullOrWhiteSpace(StatusText) ? "搜索中" : StatusText;
            return query.Length == 0 ? "搜索" : query;
        }
    }

    public string SearchPanelScopeText => IsSearchMode
        ? $"递归搜索 · {CurrentFolderName}"
        : $"当前列表筛选 · {CurrentFolderName}";

    /// <summary>搜索面板状态行：列表筛选报匹配数，递归搜索报搜索进度/结果数。</summary>
    public string SearchPanelStatusText => !IsSearchMode && !string.IsNullOrWhiteSpace(FilterText)
        ? $"当前列表匹配 {Items.Count} 项"
        : StatusText;

    /// <summary>截图/测试等待当前加载与缩略图完成。</summary>
    public Task WhenIdleAsync() => _idleTask;

    /// <summary>同步预判「返回」能否被浏览器消费（系统返回事件需同步决定 Handled）。</summary>
    public bool CanConsumeBack => IsSelectionMode || IsSearchMode || _backStack.Count > 0;

    /// <summary>工具栏按钮：列表 → 网格 → 详细信息循环切换。</summary>
    [RelayCommand]
    private void ToggleViewMode() => ViewMode = ViewMode switch
    {
        BrowserViewMode.List => BrowserViewMode.Grid,
        BrowserViewMode.Grid => BrowserViewMode.Details,
        _ => BrowserViewMode.List,
    };

    [RelayCommand]
    private void SetViewMode(string? mode) => ViewMode = mode switch
    {
        "Grid" => BrowserViewMode.Grid,
        "Details" => BrowserViewMode.Details,
        _ => BrowserViewMode.List,
    };

    partial void OnViewModeChanged(BrowserViewMode value)
    {
        OnPropertyChanged(nameof(IsListView));
        OnPropertyChanged(nameof(IsGridView));
        OnPropertyChanged(nameof(IsDetailsView));
        if (_applyingFolderViewPreferences)
            return;

        if (IsFolderViewMemoryEnabled)
        {
            // 记忆开启时，全局 ViewMode 只代表未记忆文件夹的默认值；活动文件夹单独落盘。
            CaptureCurrentFolderViewPreferences();
        }
        else if (!IsSearchMode)
        {
            _settingsStore.Settings.ViewMode = value.ToString();
        }
        _ = SaveSettingsAsync();
    }

    partial void OnSelectedCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionText));
    }

    partial void OnFilterTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasActiveSearch));
        OnPropertyChanged(nameof(SearchIslandText));
        if (!IsSearchMode && !_suppressTabFilterRefilter)
            RefilterAndSort();
        OnPropertyChanged(nameof(SearchPanelStatusText));
    }

    partial void OnIsSearchModeChanged(bool value)
    {
        OnPropertyChanged(nameof(HasActiveSearch));
        OnPropertyChanged(nameof(SearchIslandText));
        OnPropertyChanged(nameof(SearchPanelScopeText));
        OnPropertyChanged(nameof(SearchPanelStatusText));
    }

    partial void OnStatusTextChanged(string value)
    {
        OnPropertyChanged(nameof(SearchIslandText));
        OnPropertyChanged(nameof(SearchPanelStatusText));
    }

    partial void OnCurrentFolderNameChanged(string value) => OnPropertyChanged(nameof(SearchPanelScopeText));

    [RelayCommand]
    private void OpenSearchPanel() => IsSearchPanelOpen = true;

    [RelayCommand]
    private void CloseSearchPanel() => IsSearchPanelOpen = false;

    [RelayCommand]
    private void ToggleSearchPanel() => IsSearchPanelOpen = !IsSearchPanelOpen;

    [RelayCommand]
    private void ClearFilter() => FilterText = string.Empty;

    [RelayCommand]
    private void CancelCurrentSearch() => CancelSearch();

    [RelayCommand]
    private async Task SearchRecursiveFromPanelAsync()
    {
        var query = FilterText.Trim();
        if (query.Length == 0)
            return;
        IsSearchPanelOpen = false;
        await SearchRecursiveAsync(query).ConfigureAwait(true);
    }

    partial void OnCurrentPathChanged(string? value)
    {
        if (!IsAddressEditing)
            AddressText = value ?? string.Empty;
    }

    // ---------- 按文件夹记忆视图/排序 ----------
    private bool IsFolderViewMemoryEnabled => _settingsStore.Settings.RememberFolderView;

    /// <summary>Shell 切换设置后同步当前文件夹：开启时立即记下当前呈现，关闭时把当前呈现收回全局默认。</summary>
    public void OnFolderViewMemorySettingChanged(bool enabled)
    {
        if (enabled)
        {
            CaptureCurrentFolderViewPreferences();
        }
        else if (!IsSearchMode)
        {
            var settings = _settingsStore.Settings;
            settings.ViewMode = ViewMode.ToString();
            settings.SortField = _currentSortField.ToString();
            settings.SortDirection = _currentSortDirection.ToString();
        }
        _ = SaveSettingsAsync();
    }

    public async Task ClearFolderViewPreferencesAsync()
    {
        _settingsStore.Settings.FolderViewStates.Clear();
        // 清除后不立刻改变眼前的文件夹；若记忆仍开启，下一次改视图/排序会重新建档。
        await SaveSettingsAsync().ConfigureAwait(true);
    }

    private void ApplyFolderViewPreferences(string path)
    {
        if (!IsFolderViewMemoryEnabled)
            return;

        var settings = _settingsStore.Settings;
        FolderViewPreferences preferences;
        if (settings.FolderViewStates.TryGetValue(FolderViewKey(path), out var saved))
        {
            preferences = saved;
        }
        else
        {
            preferences = new FolderViewPreferences
            {
                ViewMode = settings.ViewMode,
                SortField = settings.SortField,
                SortDirection = settings.SortDirection,
            };
        }

        _applyingFolderViewPreferences = true;
        try
        {
            ViewMode = ParseViewMode(preferences.ViewMode);
            _currentSortField = ParseSort(preferences.SortField);
            _currentSortDirection = ParseSortDirection(preferences.SortDirection);
            SortFieldLabel = SortFieldText(_currentSortField);
            RaiseSortGlyphs();
        }
        finally
        {
            _applyingFolderViewPreferences = false;
        }
    }

    private void CaptureCurrentFolderViewPreferences()
    {
        if (!IsFolderViewMemoryEnabled || _applyingFolderViewPreferences || IsSearchMode || CurrentPath is null)
            return;

        var states = _settingsStore.Settings.FolderViewStates;
        var key = FolderViewKey(CurrentPath);
        // 删除后重插使字典近似保持最近使用顺序，裁剪时优先丢最久未改过的文件夹。
        states.Remove(key);
        states[key] = new FolderViewPreferences
        {
            ViewMode = ViewMode.ToString(),
            SortField = _currentSortField.ToString(),
            SortDirection = _currentSortDirection.ToString(),
        };
        const int maxRememberedFolders = 200;
        while (states.Count > maxRememberedFolders)
            states.Remove(states.Keys.First());
    }

    private static string FolderViewKey(string path)
    {
        if (FileLocator.IsStorageUri(path))
            return path;
        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full) ?? string.Empty;
            if (full.Length > root.Length)
                full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return OperatingSystem.IsWindows() ? full.ToLowerInvariant() : full;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return OperatingSystem.IsWindows() ? path.ToLowerInvariant() : path;
        }
    }

    private static BrowserViewMode ParseViewMode(string? value) => value switch
    {
        "Grid" => BrowserViewMode.Grid,
        "Details" => BrowserViewMode.Details,
        _ => BrowserViewMode.List,
    };

    private static SortDirection ParseSortDirection(string? value) =>
        string.Equals(value, "Descending", StringComparison.Ordinal)
            ? SortDirection.Descending
            : SortDirection.Ascending;

    partial void OnAddressTextChanged(string value)
    {
        if (IsAddressEditing)
            AddressError = null;
    }

    partial void OnActiveTabChanged(BrowserTabViewModel? value)
    {
        foreach (var tab in Tabs)
            tab.IsActive = ReferenceEquals(tab, value);
        if (value is not null)
        {
            _backStack = value.BackStack;
            _forwardStack = value.ForwardStack;
        }
    }

    // ---------- 导航与加载 ----------
    public async Task NavigateToAsync(string path, bool pushHistory = true)
    {
        if (pushHistory && CurrentPath is not null && !PathsEqual(CurrentPath, path))
        {
            _backStack.Push(CurrentPath);
            _forwardStack.Clear();
        }
        await LoadFolderAsync(path).ConfigureAwait(true);
    }

    private async Task LoadFolderAsync(string path)
    {
        _loadCts?.Cancel();
        _thumbCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        IsAddressEditing = false;
        AddressError = null;
        CurrentPath = path;
        CurrentFolderName = TabTitleFor(path);
        if (ActiveTab is { } activeTab)
        {
            activeTab.Path = path;
            activeTab.Title = CurrentFolderName;
        }
        IsSearchMode = false;
        ExitSelectionMode();
        ApplyFolderViewPreferences(path);
        RebuildBreadcrumbs(path);
        UpdateHistoryFlags(path);
        LoadError = null;
        IsLoading = true;
        Items.Clear();
        SelectedItems.Clear();
        await _preview.LoadAsync(null).ConfigureAwait(true);

        _idleTask = LoadCoreAsync(path, ct);
        await _idleTask.ConfigureAwait(true);
        ArmFolderWatch(path);
        _ = PersistTabsAsync();
    }

    // ---------- 文件夹外部变更监视 ----------
    private void ArmFolderWatch(string path)
    {
        _watchSubscription?.Dispose();
        _watchSubscription = null;
        if (!_watchEnabled) return;
        if (FileLocator.IsStorageUri(path)) return; // 存储形态无变更通知，靠手动刷新
        _watchSubscription = _folderWatch.Watch(path, () =>
            Dispatcher.UIThread.Post(() =>
            {
                if (!IsSearchMode && CurrentPath is not null && PathsEqual(CurrentPath, path))
                    _ = SoftReloadAsync();
            }));
    }

    private void DisarmFolderWatch()
    {
        _watchSubscription?.Dispose();
        _watchSubscription = null;
    }

    /// <summary>设置页开关即时生效（Shell 调用）。</summary>
    public void SetWatchEnabled(bool enabled)
    {
        _watchEnabled = enabled;
        if (enabled && CurrentPath is not null && !IsSearchMode)
            ArmFolderWatch(CurrentPath);
        else
            DisarmFolderWatch();
    }

    /// <summary>软刷新：仅重枚举与再排序，保留选集与浏览模式（手动刷新/外部变更共用）。</summary>
    private async Task SoftReloadAsync()
    {
        if (CurrentPath is null || IsSearchMode) return;
        _loadCts?.Cancel();
        _thumbCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;
        LoadError = null;
        IsLoading = true;
        _idleTask = LoadCoreAsync(CurrentPath, ct);
        await _idleTask.ConfigureAwait(true);
    }

    private async Task LoadCoreAsync(string path, CancellationToken ct)
    {
        try
        {
            var collected = new List<FileItem>();
            await foreach (var item in _fileSystem.ListAsync(path, _settingsStore.Settings.ShowHidden, ct).ConfigureAwait(true))
                collected.Add(item);

            _allItems = collected;
            RefilterAndSort();
            StatusText = $"{collected.Count} 个项目";
            await RecordRecentFolderAsync(path).ConfigureAwait(true);
            StartThumbnails();
        }
        catch (OperationCanceledException) { }
        catch (UnauthorizedAccessException)
        {
            LoadError = "没有访问此文件夹的权限。可尝试选择其他位置，或在系统设置中调整权限。";
            _allItems = [];
            Items.Clear();
        }
        catch (DirectoryNotFoundException)
        {
            LoadError = "此文件夹已不存在，可能已被移动或删除。";
            _allItems = [];
            Items.Clear();
        }
        catch (IOException ex)
        {
            LoadError = $"无法打开此文件夹：{ex.Message}";
            _allItems = [];
            Items.Clear();
        }
        finally
        {
            IsLoading = false;
            IsEmpty = Items.Count == 0 && LoadError is null;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsSearchMode) { await ExitSearchAsync().ConfigureAwait(true); return; }
        await SoftReloadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task GoBackAsync()
    {
        if (IsSearchMode) { await ExitSearchAsync().ConfigureAwait(true); return; }
        if (_backStack.Count == 0 || CurrentPath is null) return;
        _forwardStack.Push(CurrentPath);
        await LoadFolderAsync(_backStack.Pop()).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task GoForwardAsync()
    {
        if (_forwardStack.Count == 0 || CurrentPath is null) return;
        _backStack.Push(CurrentPath);
        await LoadFolderAsync(_forwardStack.Pop()).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task GoUpAsync()
    {
        if (CurrentPath is null) return;
        var parent = _fileSystem.GetParentPath(CurrentPath);
        if (parent is null || PathsEqual(parent, CurrentPath)) return;
        await NavigateToAsync(parent).ConfigureAwait(true);
    }

    /// <summary>宿主/手势层询问浏览器能否消费一次「返回」；能则执行并返回 true。</summary>
    public async Task<bool> TryGoBackAsync()
    {
        if (IsSelectionMode) { ExitSelectionMode(); return true; }
        if (IsSearchMode) { await ExitSearchAsync().ConfigureAwait(true); return true; }
        if (_backStack.Count > 0) { await GoBackAsync().ConfigureAwait(true); return true; }
        return false;
    }

    // ---------- 地址栏与定位 ----------
    [RelayCommand]
    private void BeginEditAddress()
    {
        if (CurrentPath is null || IsSearchMode)
            return;
        if (FileLocator.IsStorageUri(CurrentPath))
        {
            _dialogs.Notify("已授权文件夹不支持手动输入路径，请用面包屑导航");
            return;
        }
        AddressText = CurrentPath;
        AddressError = null;
        IsAddressEditing = true;
    }

    [RelayCommand]
    private void CancelEditAddress()
    {
        IsAddressEditing = false;
        AddressError = null;
        AddressText = CurrentPath ?? string.Empty;
    }

    [RelayCommand]
    private async Task CommitAddressAsync() => await NavigateToAddressAsync(AddressText).ConfigureAwait(true);

    /// <summary>解析地址栏输入并导航。文件路径会进入其所在文件夹并选中该文件，方便继续在 Eidoria 内预览。</summary>
    public async Task<bool> NavigateToAddressAsync(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            AddressError = "请输入文件夹或文件路径";
            IsAddressEditing = true;
            return false;
        }

        string candidate;
        try
        {
            candidate = ResolveAddressInput(input.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or UriFormatException)
        {
            AddressError = "路径格式无效";
            IsAddressEditing = true;
            return false;
        }

        if (FileLocator.IsStorageUri(candidate))
        {
            AddressError = "不能手动输入存储提供者 URI";
            IsAddressEditing = true;
            return false;
        }

        FileItem? target;
        try
        {
            target = await _fileSystem.StatAsync(candidate, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            AddressError = $"无法访问：{ex.Message}";
            IsAddressEditing = true;
            return false;
        }

        if (target is null)
        {
            AddressError = "找不到这个路径";
            IsAddressEditing = true;
            return false;
        }

        if (target.IsFolder)
        {
            await NavigateToAsync(candidate).ConfigureAwait(true);
        }
        else
        {
            await NavigateToFileLocationAsync(candidate).ConfigureAwait(true);
        }
        return true;
    }

    private string ResolveAddressInput(string input)
    {
        var value = input.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            value = value[1..^1].Trim();

        if (value.StartsWith("file:", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(value, UriKind.Absolute, out var fileUri))
            value = fileUri.LocalPath;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (value == "~")
        {
            value = home;
        }
        else if (value.StartsWith("~/", StringComparison.Ordinal) || value.StartsWith("~\\", StringComparison.Ordinal))
        {
            value = Path.Combine(home, value[2..]);
        }
        else if (value == "$HOME")
        {
            value = home;
        }
        else if (value.StartsWith("$HOME/", StringComparison.Ordinal) || value.StartsWith("$HOME\\", StringComparison.Ordinal))
        {
            value = Path.Combine(home, value[6..]);
        }

        value = Environment.ExpandEnvironmentVariables(value);
        return Path.GetFullPath(value, CurrentPath ?? home);
    }

    /// <summary>进入目标所在文件夹并单选目标（搜索结果/最近文件/文件地址输入共用）。</summary>
    public async Task NavigateToFileLocationAsync(string path)
    {
        FilterText = string.Empty;
        var parent = _fileSystem.GetParentPath(path) ?? path;
        if (IsSearchMode || CurrentPath is null || !PathsEqual(CurrentPath, parent))
            await NavigateToAsync(parent).ConfigureAwait(true);

        SelectedItems.Clear();
        if (Items.FirstOrDefault(item => PathsEqual(item.FullPath, path)) is { } target)
            SelectedItems.Add(target);
    }

    [RelayCommand]
    private async Task OpenContainingFolderAsync(FileItemViewModel? item)
    {
        item ??= SelectedItems.FirstOrDefault();
        if (item is not null)
            await NavigateToFileLocationAsync(item.FullPath).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RevealInSystemFileManagerAsync(FileItemViewModel? item)
    {
        item ??= SelectedItems.FirstOrDefault();
        if (item is null)
            return;
        if (FileLocator.IsStorageUri(item.FullPath))
        {
            _dialogs.Notify("已授权文件夹中的项目无法在系统文件管理器中定位");
            return;
        }

        var revealed = await Launcher.RevealAsync(item.FullPath, CancellationToken.None).ConfigureAwait(true);
        if (!revealed)
            _dialogs.Notify("当前平台不支持在系统文件管理器中显示");
    }

    [RelayCommand]
    private async Task CopyPathAsync(FileItemViewModel? item)
    {
        IReadOnlyList<string> paths = item is not null
            ? [item.FullPath]
            : SelectedItems.Select(selected => selected.FullPath).ToArray();
        await CopyPathsTextAsync(paths).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task CopyCurrentPathAsync()
    {
        if (CurrentPath is not null)
            await CopyPathsTextAsync([CurrentPath]).ConfigureAwait(true);
    }

    private async Task CopyPathsTextAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
            return;
        await _systemClipboard.SetTextAsync(string.Join(Environment.NewLine, paths), CancellationToken.None).ConfigureAwait(true);

        // 文本写入会替换系统文件剪贴板；同步清掉应用内待粘贴集合，避免 Ctrl+V 仍粘贴旧文件。
        _clipboardPaths = [];
        _clipboardCut = false;
        _clipboardOwnedByApp = false;
        _externalClipboardBaseline = [];
        OnPropertyChanged(nameof(HasClipboard));
        _dialogs.Notify(paths.Count == 1 ? "已复制路径" : $"已复制 {paths.Count} 条路径");
    }

    // ---------- 最近访问 ----------
    public async Task RecordRecentFileAsync(string path)
    {
        if (FileLocator.IsStorageUri(path))
            return;
        await RecordRecentAsync(_settingsStore.Settings.RecentFilePaths, path).ConfigureAwait(true);
    }

    private async Task RecordRecentFolderAsync(string path)
    {
        if (FileLocator.IsStorageUri(path))
            return;
        await RecordRecentAsync(_settingsStore.Settings.RecentFolderPaths, path).ConfigureAwait(true);
    }

    private async Task RecordRecentAsync(List<string> target, string path)
    {
        if (target.Count > 0 && PathsEqual(target[0], path))
            return;
        target.RemoveAll(existing => PathsEqual(existing, path));
        target.Insert(0, path);
        if (target.Count > 12)
            target.RemoveRange(12, target.Count - 12);
        await SaveSettingsAsync().ConfigureAwait(true);
        RecentsChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---------- 标签页 ----------
    // 每个标签页保存自己的路径、前进/后退栈、列表过滤词与选集快照；剪贴板与视图/排序是全局会话状态，不随标签拆分。
    // 切换时按快照重载目标文件夹（顺带拿到外部最新状态），滚动位置与一次性递归搜索结果不持久化。
    private string TabTitleFor(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "新标签页";
        return FileLocator.IsStorageUri(path)
            ? _fileSystem.GetBreadcrumbs(path).LastOrDefault()?.Name ?? path
            : FolderDisplayName(path);
    }

    private BrowserTabViewModel CreateTab(string? path) => new(path, TabTitleFor(path));

    private void SnapshotActiveTab()
    {
        if (ActiveTab is not { } tab)
            return;
        tab.Path = CurrentPath;
        tab.Title = string.IsNullOrWhiteSpace(CurrentFolderName) ? TabTitleFor(CurrentPath) : CurrentFolderName;
        tab.FilterTextSnapshot = IsSearchMode ? string.Empty : FilterText;
        tab.SelectedPathsSnapshot = IsSearchMode
            ? []
            : SelectedItems.Select(item => item.FullPath).ToArray();
    }

    private async Task ActivateTabCoreAsync(BrowserTabViewModel tab, bool snapshotCurrent)
    {
        if (string.IsNullOrWhiteSpace(tab.Path))
            return;
        if (snapshotCurrent)
            SnapshotActiveTab();

        ActiveTab = tab;
        var selectedPaths = tab.SelectedPathsSnapshot;
        _suppressTabFilterRefilter = true;
        FilterText = tab.FilterTextSnapshot;
        _suppressTabFilterRefilter = false;

        await LoadFolderAsync(tab.Path).ConfigureAwait(true);

        if (selectedPaths.Count > 0)
        {
            foreach (var item in Items.Where(item => selectedPaths.Any(path => PathsEqual(path, item.FullPath))))
                SelectedItems.Add(item);
        }
        _ = PersistTabsAsync();
    }

    [RelayCommand]
    private async Task ActivateTabAsync(BrowserTabViewModel? tab)
    {
        if (tab is null || ReferenceEquals(tab, ActiveTab))
            return;
        await ActivateTabCoreAsync(tab, snapshotCurrent: true).ConfigureAwait(true);
    }

    public async Task OpenInNewTabAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        SnapshotActiveTab();
        var tab = CreateTab(path);
        Tabs.Add(tab);
        await ActivateTabCoreAsync(tab, snapshotCurrent: false).ConfigureAwait(true);
    }

    public async Task OpenItemInNewTabAsync(FileItemViewModel? item)
    {
        if (item is null)
            return;
        if (!item.IsFolder)
        {
            _dialogs.Notify("只有文件夹可以在新标签页中打开");
            return;
        }
        await OpenInNewTabAsync(item.FullPath).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task NewTabAsync()
    {
        if (ActiveTab is not null)
            await DuplicateTabAsync(ActiveTab).ConfigureAwait(true);
        else if (CurrentPath is not null)
            await OpenInNewTabAsync(CurrentPath).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DuplicateTabAsync(BrowserTabViewModel? tab)
    {
        if (tab is null || string.IsNullOrWhiteSpace(tab.Path))
            return;
        if (ReferenceEquals(tab, ActiveTab))
            SnapshotActiveTab();

        var copy = CreateTab(tab.Path);
        copy.Title = tab.Title;
        CopyStack(tab.BackStack, copy.BackStack);
        CopyStack(tab.ForwardStack, copy.ForwardStack);
        copy.FilterTextSnapshot = tab.FilterTextSnapshot;
        copy.SelectedPathsSnapshot = tab.SelectedPathsSnapshot.ToArray();
        Tabs.Insert(Tabs.IndexOf(tab) + 1, copy);
        await ActivateTabCoreAsync(copy, snapshotCurrent: false).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task CloseTabAsync(BrowserTabViewModel? tab)
    {
        if (tab is null || Tabs.Count <= 1)
            return;
        var index = Tabs.IndexOf(tab);
        if (index < 0)
            return;

        var closingActive = ReferenceEquals(tab, ActiveTab);
        if (closingActive)
            SnapshotActiveTab();
        RememberClosedTab(tab, index);
        Tabs.Remove(tab);
        tab.IsActive = false;
        if (!closingActive)
        {
            _ = PersistTabsAsync();
            return;
        }

        var target = Tabs[Math.Clamp(index, 0, Tabs.Count - 1)];
        await ActivateTabCoreAsync(target, snapshotCurrent: false).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task CloseOtherTabsAsync(BrowserTabViewModel? tab)
    {
        tab ??= ActiveTab;
        if (tab is null || Tabs.Count <= 1 || !Tabs.Contains(tab))
            return;

        SnapshotActiveTab();
        var others = Tabs.Where(candidate => !ReferenceEquals(candidate, tab)).ToArray();
        // 反序入栈：连续按 Ctrl+Shift+T 时按原来的从左到右顺序恢复。
        for (var i = others.Length - 1; i >= 0; i--)
            RememberClosedTab(others[i], Tabs.IndexOf(others[i]), trim: false);
        TrimClosedTabs();

        foreach (var other in others)
        {
            Tabs.Remove(other);
            other.IsActive = false;
        }

        if (!ReferenceEquals(tab, ActiveTab))
            await ActivateTabCoreAsync(tab, snapshotCurrent: false).ConfigureAwait(true);
        else
            _ = PersistTabsAsync();
    }

    [RelayCommand]
    private async Task ReopenClosedTabAsync()
    {
        if (_closedTabs.Count == 0)
            return;
        SnapshotActiveTab();
        var snapshot = _closedTabs.Pop();
        OnPropertyChanged(nameof(HasClosedTabs));

        var tab = CreateTab(snapshot.Path);
        tab.Title = snapshot.Title;
        RestoreStack(tab.BackStack, snapshot.BackPaths);
        RestoreStack(tab.ForwardStack, snapshot.ForwardPaths);
        tab.FilterTextSnapshot = snapshot.FilterText;
        tab.SelectedPathsSnapshot = snapshot.SelectedPaths;
        Tabs.Insert(Math.Clamp(snapshot.Index, 0, Tabs.Count), tab);
        await ActivateTabCoreAsync(tab, snapshotCurrent: false).ConfigureAwait(true);
    }

    private void RememberClosedTab(BrowserTabViewModel tab, int index, bool trim = true)
    {
        if (string.IsNullOrWhiteSpace(tab.Path))
            return;
        _closedTabs.Push(new ClosedTabSnapshot(
            tab.Path,
            tab.Title,
            tab.BackStack.ToArray(),
            tab.ForwardStack.ToArray(),
            tab.FilterTextSnapshot,
            tab.SelectedPathsSnapshot.ToArray(),
            index));
        if (trim)
            TrimClosedTabs();
        OnPropertyChanged(nameof(HasClosedTabs));
    }

    private void TrimClosedTabs()
    {
        const int maxClosedTabs = 10;
        if (_closedTabs.Count <= maxClosedTabs)
            return;
        var retained = _closedTabs.Take(maxClosedTabs).Reverse().ToArray();
        _closedTabs.Clear();
        foreach (var snapshot in retained)
            _closedTabs.Push(snapshot);
    }

    private static void CopyStack(Stack<string> source, Stack<string> destination) =>
        RestoreStack(destination, source.ToArray());

    private static void RestoreStack(Stack<string> destination, IReadOnlyList<string> topToBottom)
    {
        destination.Clear();
        for (var i = topToBottom.Count - 1; i >= 0; i--)
            destination.Push(topToBottom[i]);
    }

    [RelayCommand]
    private async Task CloseActiveTabAsync() => await CloseTabAsync(ActiveTab).ConfigureAwait(true);

    [RelayCommand]
    private async Task NextTabAsync()
    {
        if (Tabs.Count < 2 || ActiveTab is null)
            return;
        var index = Tabs.IndexOf(ActiveTab);
        await ActivateTabAsync(Tabs[(index + 1) % Tabs.Count]).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task PreviousTabAsync()
    {
        if (Tabs.Count < 2 || ActiveTab is null)
            return;
        var index = Tabs.IndexOf(ActiveTab);
        await ActivateTabAsync(Tabs[(index - 1 + Tabs.Count) % Tabs.Count]).ConfigureAwait(true);
    }

    /// <summary>启动恢复：优先打开上次会话的标签页（最多 12 个）；没有记录时打开宿主起始文件夹。</summary>
    public async Task RestoreTabsAsync(string fallbackPath)
    {
        var savedPaths = _settingsStore.Settings.OpenTabPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Take(12)
            .ToArray();

        Tabs.Clear();
        foreach (var path in savedPaths)
            Tabs.Add(CreateTab(path));
        if (Tabs.Count == 0)
            Tabs.Add(CreateTab(fallbackPath));

        var index = Math.Clamp(_settingsStore.Settings.ActiveTabIndex, 0, Tabs.Count - 1);
        await ActivateTabCoreAsync(Tabs[index], snapshotCurrent: false).ConfigureAwait(true);
    }

    private async Task PersistTabsAsync()
    {
        var settings = _settingsStore.Settings;
        settings.OpenTabPaths = Tabs
            .Where(tab => !string.IsNullOrWhiteSpace(tab.Path))
            .Select(tab => tab.Path!)
            .Take(12)
            .ToList();
        settings.ActiveTabIndex = ActiveTab is null ? 0 : Math.Max(0, Tabs.IndexOf(ActiveTab));
        await SaveSettingsAsync().ConfigureAwait(true);
    }

    private void UpdateTabMetadata()
    {
        foreach (var tab in Tabs)
            tab.CanClose = Tabs.Count > 1;
        OnPropertyChanged(nameof(HasMultipleTabs));
        OnPropertyChanged(nameof(HasClosedTabs));
    }

    // ---------- 打开与选择 ----------
    public async Task OpenItemAsync(FileItemViewModel? item)
    {
        if (item is null) return;
        if (IsSelectionMode)
        {
            ToggleChecked(item);
            return;
        }
        if (item.IsFolder)
            await NavigateToAsync(item.FullPath).ConfigureAwait(true);
        // 文件的打开语义由视图层按断点决定（Compact 进预览页、桌面进预览窗格），在此只做目录进入。
    }

    public async Task OpenItemExternalAsync(FileItemViewModel? item)
    {
        if (item is null || item.IsFolder) return;
        await RecordRecentFileAsync(item.FullPath).ConfigureAwait(true);
        await _preview.OpenCommand.ExecuteAsync(null).ConfigureAwait(true);
    }

    private void OnSelectionChanged()
    {
        SelectedCount = SelectedItems.Count;
        var single = SelectedItems.Count == 1 ? SelectedItems[0].Model : null;
        _ = _preview.LoadAsync(single);
    }

    [RelayCommand]
    private void EnterSelectionMode() => IsSelectionMode = true;

    [RelayCommand]
    private void ExitSelectionMode()
    {
        IsSelectionMode = false;
        foreach (var item in Items) item.IsChecked = false;
    }

    public void ToggleChecked(FileItemViewModel item)
    {
        item.IsChecked = !item.IsChecked;
        if (item.IsChecked && !SelectedItems.Contains(item)) SelectedItems.Add(item);
        else if (!item.IsChecked) SelectedItems.Remove(item);
    }

    [RelayCommand]
    private void SelectAll()
    {
        SelectedItems.Clear();
        foreach (var item in Items)
        {
            item.IsChecked = IsSelectionMode;
            SelectedItems.Add(item);
        }
    }

    // ---------- 排序 / 过滤 ----------
    public SortField CurrentSortField => _currentSortField;
    public SortDirection CurrentSortDirection => _currentSortDirection;

    public void SetSort(SortField field, SortDirection direction)
    {
        _currentSortField = field;
        _currentSortDirection = direction;
        if (IsFolderViewMemoryEnabled)
        {
            CaptureCurrentFolderViewPreferences();
        }
        else if (!IsSearchMode)
        {
            _settingsStore.Settings.SortField = field.ToString();
            _settingsStore.Settings.SortDirection = direction.ToString();
        }
        SortFieldLabel = SortFieldText(field);
        RaiseSortGlyphs();
        _ = SaveSettingsAsync();
        RefilterAndSort();
    }

    /// <summary>详细信息视图点列头：同字段翻转方向、新字段升序；最终仍经 SetSort → RefilterAndSort 管线，
    /// 保证自然序与「文件夹在前」不被控件自排覆盖（视图层把 DataGrid 的 Sorting 事件标记 Handled 后调用至此）。</summary>
    public void ApplyColumnSort(SortField field)
    {
        var direction = CurrentSortField == field && CurrentSortDirection == SortDirection.Ascending
            ? SortDirection.Descending
            : SortDirection.Ascending;
        SetSort(field, direction);
    }

    // 详细信息列头方向指示（控件默认箭头随其自排机制走，我们自己管排序故自绘）
    public string NameSortGlyph => SortGlyph(SortField.Name);
    public string SizeSortGlyph => SortGlyph(SortField.Size);
    public string KindSortGlyph => SortGlyph(SortField.Kind);
    public string ModifiedSortGlyph => SortGlyph(SortField.LastWriteTime);

    private string SortGlyph(SortField field) =>
        CurrentSortField != field ? "" : CurrentSortDirection == SortDirection.Ascending ? "↑" : "↓";

    private void RaiseSortGlyphs()
    {
        OnPropertyChanged(nameof(NameSortGlyph));
        OnPropertyChanged(nameof(SizeSortGlyph));
        OnPropertyChanged(nameof(KindSortGlyph));
        OnPropertyChanged(nameof(ModifiedSortGlyph));
    }

    public void RefilterAndSort()
    {
        IEnumerable<FileItem> filtered = _allItems;
        if (!IsSearchMode && !string.IsNullOrWhiteSpace(FilterText))
        {
            var query = FilterText.Trim();
            filtered = filtered.Where(i => i.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
        }
        var sorted = FileItemOrdering.Sort(filtered, CurrentSortField, CurrentSortDirection, _settingsStore.Settings.FoldersFirst);
        var previousSelection = SelectedItems.Select(s => s.FullPath).ToHashSet();
        Items.Clear();
        foreach (var item in sorted)
        {
            var vm = new FileItemViewModel(item) { IsFavorite = _settingsStore.IsFavorite(item.FullPath), IsChecked = previousSelection.Contains(item.FullPath) && IsSelectionMode };
            Items.Add(vm);
        }
        SelectedItems.Clear();
        foreach (var vm in Items.Where(i => previousSelection.Contains(i.FullPath)))
            SelectedItems.Add(vm);
        IsEmpty = Items.Count == 0 && LoadError is null && !IsLoading;
        OnPropertyChanged(nameof(SearchPanelStatusText));
    }

    // ---------- 搜索（递归） ----------
    public async Task SearchRecursiveAsync(string query, IReadOnlySet<FileKind>? kinds = null, string? rootPath = null)
    {
        var root = rootPath ?? CurrentPath;
        if (root is null) return;
        if (FileLocator.IsStorageUri(root))
        {
            _dialogs.Notify("授权文件夹内暂不支持递归搜索，可用列表内筛选按名称过滤");
            return;
        }

        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;
        DisarmFolderWatch();
        IsSearchMode = true;
        ExitSelectionMode();
        Items.Clear();
        SelectedItems.Clear();
        LoadError = null;
        IsLoading = true;
        StatusText = "正在搜索…";
        var found = 0;
        var batch = new List<FileItem>();
        _idleTask = Task.CompletedTask;
        try
        {
            var spec = new SearchSpec(query, root, kinds, _settingsStore.Settings.ShowHidden);
            await foreach (var hit in _search.SearchByNameAsync(spec, ct).ConfigureAwait(true))
            {
                batch.Add(hit);
                found++;
                if (batch.Count >= 50)
                {
                    FlushBatch(batch);
                    batch.Clear();
                }
                StatusText = $"已找到 {found} 项…";
            }
            FlushBatch(batch);
            _allItems = Items.Select(i => i.Model).ToList();
            StatusText = $"搜索到 {found} 项";
        }
        catch (OperationCanceledException)
        {
            StatusText = $"搜索已取消 · {found} 项";
        }
        finally
        {
            IsLoading = false;
            IsEmpty = Items.Count == 0;
            StartThumbnails();
        }
    }

    private void FlushBatch(List<FileItem> batch)
    {
        foreach (var item in batch)
            Items.Add(new FileItemViewModel(item) { IsFavorite = _settingsStore.IsFavorite(item.FullPath) });
    }

    [RelayCommand]
    private async Task ExitSearchAsync()
    {
        IsSearchPanelOpen = false;
        IsSearchMode = false;
        FilterText = string.Empty;
        if (CurrentPath is not null)
            await LoadFolderAsync(CurrentPath).ConfigureAwait(true);
    }

    public void CancelSearch() => _loadCts?.Cancel();

    // ---------- 写操作 ----------
    [RelayCommand]
    private async Task NewFolderAsync()
    {
        if (CurrentPath is null) return;
        var name = await _dialogs.PromptAsync("新建文件夹", "新建文件夹", "创建", ValidateNewName).ConfigureAwait(true);
        if (name is null) return;
        await RunSimpleAsync("正在创建…", async () =>
        {
            await _fileSystem.CreateFolderAsync(CurrentPath, name.Trim(), CancellationToken.None).ConfigureAwait(true);
            _dialogs.Notify($"已创建 {name.Trim()}");
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task NewFileAsync()
    {
        if (CurrentPath is null) return;
        var name = await _dialogs.PromptAsync("新建文件", "新建文本文件.txt", "创建", ValidateNewName).ConfigureAwait(true);
        if (name is null) return;
        await RunSimpleAsync("正在创建…", async () =>
        {
            await _fileSystem.CreateFileAsync(CurrentPath, name.Trim(), CancellationToken.None).ConfigureAwait(true);
            _dialogs.Notify($"已创建 {name.Trim()}");
        }).ConfigureAwait(true);
    }

    private string? ValidateNewName(string value)
    {
        var error = NameValidator.Validate(value);
        if (error is not null) return error;
        if (CurrentPath is not null && _fileSystem.Exists(Path.Combine(CurrentPath, value.Trim())))
            return "已存在同名项目";
        return null;
    }

    [RelayCommand]
    private async Task RenameAsync(FileItemViewModel? item)
    {
        item ??= SelectedItems.FirstOrDefault();
        if (item is null) return;
        var name = await _dialogs.PromptAsync("重命名", item.Name, "重命名", value =>
        {
            var error = NameValidator.Validate(value);
            if (error is not null) return error;
            var dest = Path.Combine(Path.GetDirectoryName(item.FullPath)!, value.Trim());
            if (!PathsEqual(dest, item.FullPath) && _fileSystem.Exists(dest)) return "已存在同名项目";
            return null;
        }).ConfigureAwait(true);
        if (name is null || string.Equals(name.Trim(), item.Name, StringComparison.Ordinal)) return;
        await RunSimpleAsync("正在重命名…", async () =>
        {
            await _fileSystem.RenameAsync(item.FullPath, name.Trim(), CancellationToken.None).ConfigureAwait(true);
            _dialogs.Notify("已重命名");
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task CopySelectionAsync() => await SetClipboardAsync(cut: false).ConfigureAwait(true);

    [RelayCommand]
    private async Task CutSelectionAsync() => await SetClipboardAsync(cut: true).ConfigureAwait(true);

    private async Task SetClipboardAsync(bool cut)
    {
        if (SelectedItems.Count == 0) return;
        var paths = SelectedItems.Select(i => i.FullPath).ToList();

        // 先记住操作前系统剪贴板的文件集合：粘贴/窗口激活时据此判断外部内容是否
        // 在本应用复制/剪切之后又被另一个应用更新，避免旧外部内容覆盖应用内剪切状态。
        _externalClipboardBaseline = await _systemClipboard.TryGetFilesAsync(CancellationToken.None).ConfigureAwait(true);
        _clipboardPaths = paths;
        _clipboardCut = cut;
        _clipboardOwnedByApp = true;
        OnPropertyChanged(nameof(HasClipboard));

        if (!cut)
        {
            // Avalonia 高层文件格式没有跨应用的统一“剪切效果”编码：复制与系统同步，
            // 剪切保留在应用内，避免把用户预期为移动的动作在外部悄悄变成复制。
            await _systemClipboard.SetFilesAsync(paths, CancellationToken.None).ConfigureAwait(true);
            _externalClipboardBaseline = paths.Where(p => !FileLocator.IsStorageUri(p)).ToList();
        }

        _dialogs.Notify(cut ? $"已剪切 {paths.Count} 项" : $"已复制 {paths.Count} 项");
    }

    /// <summary>视图挂载/窗口激活时导入其他应用新写入的文件剪贴板。</summary>
    public async Task RefreshExternalClipboardAsync() =>
        await ImportExternalClipboardAsync().ConfigureAwait(true);

    private async Task ImportExternalClipboardAsync()
    {
        var external = await _systemClipboard.TryGetFilesAsync(CancellationToken.None).ConfigureAwait(true);
        if (external.Count == 0)
            return;
        if (_clipboardOwnedByApp && PathSetsEqual(external, _externalClipboardBaseline))
            return;
        if (PathSetsEqual(external, _clipboardPaths))
        {
            _externalClipboardBaseline = external;
            return;
        }

        _clipboardPaths = external.ToList();
        _clipboardCut = false;
        _clipboardOwnedByApp = false;
        _externalClipboardBaseline = external;
        OnPropertyChanged(nameof(HasClipboard));
    }

    [RelayCommand]
    private async Task PasteAsync()
    {
        if (CurrentPath is null) return;
        await ImportExternalClipboardAsync().ConfigureAwait(true);
        if (_clipboardPaths.Count == 0) return;

        var sources = _clipboardPaths.ToList();
        var cut = _clipboardCut;
        var outcome = await TransferItemsAsync(sources, CurrentPath, cut).ConfigureAwait(true);
        if (cut && outcome?.Success == true)
        {
            _clipboardPaths = [];
            _clipboardOwnedByApp = false;
            _externalClipboardBaseline = [];
            OnPropertyChanged(nameof(HasClipboard));
        }
    }

    /// <summary>
    /// 通用文件传输：粘贴与拖放共用。含同源/后代目标防护、复制冲突询问、进度对话框与必要时刷新。
    /// 全程使用文件系统服务的名称/父链语义，本地路径与 storage URI 均不做 Path.* 拼接。
    /// </summary>
    public async Task<OperationOutcome?> TransferItemsAsync(
        IReadOnlyList<string> sourcePaths, string destinationFolder, bool isMove)
    {
        var sources = sourcePaths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (sources.Count == 0 || string.IsNullOrWhiteSpace(destinationFolder))
            return null;
        if (!CanTransferItems(sources, destinationFolder))
        {
            _dialogs.Notify("目标文件夹不能是所选文件夹本身或其子文件夹");
            return null;
        }

        var names = new List<string>(sources.Count);
        foreach (var source in sources)
            names.Add(await GetTransferDisplayNameAsync(source).ConfigureAwait(true));

        var destinationNames = await GetDestinationNamesAsync(destinationFolder).ConfigureAwait(true);
        var conflict = names.FirstOrDefault(destinationNames.Contains);
        var policy = ConflictPolicy.KeepBoth;
        var allFromDestination = sources.All(s =>
            TransferPathsEqual(_fileSystem.GetParentPath(s), destinationFolder));
        if (conflict is not null && !isMove && !allFromDestination)
        {
            var asked = await _dialogs.AskConflictAsync(conflict).ConfigureAwait(true);
            if (asked is null) return null;
            policy = asked.Value;
        }

        var outcome = await _dialogs.RunOperationAsync(isMove ? "正在移动…" : "正在复制…", (progress, ct) =>
            isMove
                ? _operations.MoveAsync(sources, destinationFolder, policy, progress, ct)
                : _operations.CopyAsync(sources, destinationFolder, policy, progress, ct)).ConfigureAwait(true);

        var affectsCurrentFolder = TransferPathsEqual(destinationFolder, CurrentPath)
            || sources.Any(s => TransferPathsEqual(_fileSystem.GetParentPath(s), CurrentPath));
        if (affectsCurrentFolder)
            await ReloadAfterOperationAsync().ConfigureAwait(true);
        return outcome;
    }

    /// <summary>拖放 DragOver 阶段的目标合法性预判（禁止落入来源自身或其子文件夹）。</summary>
    public bool CanTransferItems(IReadOnlyList<string> sourcePaths, string destinationFolder) =>
        sourcePaths.All(source => !FileTransferRules.IsSameOrDescendant(
            destinationFolder, source, _fileSystem.GetParentPath, static (a, b) => TransferPathsEqual(a, b)));

    private async Task<string> GetTransferDisplayNameAsync(string source)
    {
        var known = _allItems.FirstOrDefault(i => TransferPathsEqual(i.FullPath, source));
        if (known is not null)
            return known.Name;
        if (!FileLocator.IsStorageUri(source))
        {
            var localName = Path.GetFileName(source.TrimEnd('/', '\\'));
            return string.IsNullOrEmpty(localName) ? source : localName;
        }
        var stat = await _fileSystem.StatAsync(source, CancellationToken.None).ConfigureAwait(true);
        return stat?.Name ?? source;
    }

    private async Task<HashSet<string>> GetDestinationNamesAsync(string destinationFolder)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await foreach (var item in _fileSystem.ListAsync(destinationFolder, includeHidden: true, CancellationToken.None).ConfigureAwait(true))
                names.Add(item.Name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 冲突预检尽力而为；真正的打开/写入错误由操作管线汇总报告。
        }
        return names;
    }

    internal static bool PathSetsEqual(IReadOnlyList<string> left, IReadOnlyList<string> right) =>
        left.Count == right.Count
        && left.All(a => right.Any(b => TransferPathsEqual(a, b)))
        && right.All(b => left.Any(a => TransferPathsEqual(a, b)));

    internal static bool TransferPathsEqual(string? a, string? b)
    {
        if (a is null || b is null) return false;
        if (FileLocator.IsStorageUri(a) || FileLocator.IsStorageUri(b))
            return string.Equals(a, b, StringComparison.Ordinal);
        return PathsEqual(a, b);
    }

    [RelayCommand]
    private async Task DeleteSelectionAsync()
    {
        if (SelectedItems.Count == 0) return;
        var items = SelectedItems.ToList();
        var canTrash = _trash.CanMoveToTrash;
        var title = canTrash ? "移入回收站？" : "永久删除？";
        var message = canTrash
            ? $"将 {items.Count} 个项目移入回收站，之后可从回收站恢复。"
            : $"将永久删除 {items.Count} 个项目，此操作无法撤销。";
        if (!await _dialogs.ConfirmAsync(title, message, canTrash ? "移入回收站" : "永久删除", destructive: true).ConfigureAwait(true))
            return;

        if (canTrash)
        {
            var outcome = await _dialogs.RunOperationAsync("正在移入回收站…", async (progress, ct) =>
            {
                var errors = new List<OperationError>();
                var done = 0;
                foreach (var item in items)
                {
                    ct.ThrowIfCancellationRequested();
                    try { await _trash.MoveToTrashAsync(item.FullPath, ct).ConfigureAwait(true); done++; }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
                    {
                        errors.Add(new OperationError(item.Name, ex.Message));
                    }
                    progress.Report(new OperationProgress((done + errors.Count) / (double)Math.Max(1, items.Count), item.Name, done + errors.Count, items.Count));
                }
                return new OperationOutcome(done, errors, []);
            }).ConfigureAwait(true);
            _ = outcome;
        }
        else
        {
            await _dialogs.RunOperationAsync("正在删除…", (progress, ct) =>
                _operations.DeletePermanentlyAsync(items.Select(i => i.FullPath).ToList(), progress, ct)).ConfigureAwait(true);
        }
        await ReloadAfterOperationAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task CompressSelectionAsync()
    {
        if (SelectedItems.Count == 0 || CurrentPath is null) return;
        if (FileLocator.IsStorageUri(CurrentPath))
        {
            _dialogs.Notify("授权文件夹内暂不支持压缩");
            return;
        }
        var first = SelectedItems[0];
        var defaultName = (SelectedItems.Count == 1 ? Path.GetFileNameWithoutExtension(first.Name) : CurrentFolderName) + ".zip";
        var dest = Path.Combine(CurrentPath, defaultName);
        var i = 2;
        while (_fileSystem.Exists(dest))
            dest = Path.Combine(CurrentPath, PathText.KeepBothName(defaultName, i++));
        var sources = SelectedItems.Select(s => s.FullPath).ToList();
        await _dialogs.RunOperationAsync("正在压缩…", (progress, ct) =>
            _archive.CompressToZipAsync(sources, dest, progress, ct)).ConfigureAwait(true);
        await ReloadAfterOperationAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ExtractSelectionAsync(FileItemViewModel? item)
    {
        item ??= SelectedItems.FirstOrDefault();
        if (item is null || CurrentPath is null) return;
        if (FileLocator.IsStorageUri(item.FullPath) || FileLocator.IsStorageUri(CurrentPath))
        {
            _dialogs.Notify("授权文件夹内暂不支持解压");
            return;
        }
        var destDir = Path.Combine(CurrentPath, Path.GetFileNameWithoutExtension(item.Name));
        await _dialogs.RunOperationAsync("正在解压…", (progress, ct) =>
            _archive.ExtractAsync(item.FullPath, destDir, progress, ct)).ConfigureAwait(true);
        await ReloadAfterOperationAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(FileItemViewModel? item)
    {
        item ??= SelectedItems.FirstOrDefault();
        if (item is null) return;
        var now = !_settingsStore.IsFavorite(item.FullPath);
        await _settingsStore.SetFavoriteAsync(item.FullPath, now, CancellationToken.None).ConfigureAwait(true);
        item.IsFavorite = now;
        FavoritesChanged?.Invoke(this, EventArgs.Empty);
        _dialogs.Notify(now ? "已加入收藏" : "已取消收藏");
    }

    public event EventHandler? FavoritesChanged;

    [RelayCommand]
    private async Task ShowPropertiesAsync(FileItemViewModel? item)
    {
        item ??= SelectedItems.FirstOrDefault();
        if (item is null) return;
        var m = item.Model;
        var body =
            $"名称：{m.Name}\n" +
            $"类型：{FileKindClassifier.Describe(m)}\n" +
            (m.IsFolder ? "" : $"大小：{Formatting.Bytes(m.Length)}（{m.Length:N0} 字节）\n") +
            $"修改时间：{Formatting.FullDateTime(m.LastWriteTime)}\n" +
            $"路径：{m.FullPath}" +
            (m.IsSymlink ? "\n链接：符号链接" : "") +
            (m.IsHidden ? "\n可见性：隐藏" : "");
        if (m.IsFolder && !m.Locator.IsStorage)
        {
            try
            {
                var size = await _inspector.GetFolderSizeAsync(m.FullPath, CancellationToken.None).ConfigureAwait(true);
                body += $"\n大小：{Formatting.Bytes(size.TotalBytes)}（{size.FileCount:N0} 个文件、{size.FolderCount:N0} 个文件夹）"
                        + (size.IsPartial ? "（部分项目无权访问，结果为下限）" : "");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                body += "\n大小：（计算失败）";
            }
        }
        await _dialogs.ShowPropertiesAsync("属性", body).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ComputeChecksumsAsync(FileItemViewModel? item)
    {
        item ??= SelectedItems.FirstOrDefault();
        if (item is null || item.Model.IsFolder) return;
        if (item.Model.Locator.IsStorage)
        {
            _dialogs.Notify("授权文件夹内暂不支持计算校验和");
            return;
        }
        var path = item.FullPath;
        var name = item.Name;
        string? sha256 = null, md5 = null;
        var outcome = await _dialogs.RunOperationAsync("计算校验和", async (progress, ct) =>
        {
            var fraction = new Progress<double>(v =>
                progress.Report(new OperationProgress(v, name, 0, 0)));
            sha256 = await _inspector.ComputeHashAsync(path, "SHA256", fraction, ct).ConfigureAwait(true);
            md5 = await _inspector.ComputeHashAsync(path, "MD5", fraction, ct).ConfigureAwait(true);
            return new OperationOutcome(1, [], []);
        }).ConfigureAwait(true);
        if (outcome.Success && sha256 is not null)
        {
            await _dialogs.ShowPropertiesAsync("校验和",
                $"文件：{name}\n\nSHA-256：\n{sha256}\n\nMD5：\n{md5}").ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task BatchRenameSelectionAsync()
    {
        if (CurrentPath is null) return;
        var targets = SelectedItems.ToList();
        if (targets.Count < 2)
        {
            _dialogs.Notify("请先选择至少 2 个项目");
            return;
        }
        var names = targets.Select(t => t.Name).ToList();
        var siblings = _allItems.Select(i => i.Name)
            .Where(n => !names.Contains(n, StringComparer.OrdinalIgnoreCase))
            .ToList();
        var proposed = await _dialogs.BatchRenameAsync(names, siblings).ConfigureAwait(true);
        if (proposed is null) return;

        var folder = CurrentPath;
        await RunSimpleAsync("批量重命名", async () =>
        {
            // 两段改名：先全部改至临时名再落终名，消除互换名/占用顺序问题
            var stamp = Guid.NewGuid().ToString("N")[..8];
            var changed = targets
                .Select((t, i) => (t, i))
                .Where(x => !string.Equals(names[x.i], proposed[x.i], StringComparison.Ordinal))
                .ToList();
            var temps = new Dictionary<int, string>();
            foreach (var (target, i) in changed)
            {
                var temp = $".mdexplorer-rename-{stamp}-{i}";
                await _fileSystem.RenameAsync(target.FullPath, temp, CancellationToken.None).ConfigureAwait(true);
                temps[i] = Path.Combine(folder, temp);
            }
            foreach (var (_, i) in changed)
                await _fileSystem.RenameAsync(temps[i], proposed[i], CancellationToken.None).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private async Task RunSimpleAsync(string title, Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ConfirmAsync("操作失败", ex.Message, "知道了").ConfigureAwait(true);
        }
        await ReloadAfterOperationAsync().ConfigureAwait(true);
    }

    private async Task ReloadAfterOperationAsync()
    {
        if (IsSearchMode)
        {
            await ExitSearchAsync().ConfigureAwait(true);
        }
        else if (CurrentPath is not null)
        {
            await LoadFolderAsync(CurrentPath).ConfigureAwait(true);
        }
    }

    // ---------- 缩略图 ----------
    private void StartThumbnails()
    {
        _thumbCts?.Cancel();
        _thumbCts = new CancellationTokenSource();
        var ct = _thumbCts.Token;
        var targets = Items.Where(i => i.Kind == FileKind.Image).Take(300).ToList();
        if (targets.Count == 0) { _idleTask = Task.CompletedTask; return; }
        var gate = new SemaphoreSlim(OperatingSystem.IsAndroid() ? 2 : 4);
        var tasks = targets.Select(async item =>
        {
            try
            {
                await gate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    await using var stream = await _fileSystem.OpenReadAsync(item.FullPath, ct).ConfigureAwait(false);
                    var bitmap = await Task.Run(() => Bitmap.DecodeToWidth(stream, 192), ct).ConfigureAwait(false);
                    await Dispatcher.UIThread.InvokeAsync(() => item.Thumbnail = bitmap);
                }
                finally { gate.Release(); }
            }
            catch (OperationCanceledException) { }
            catch (Exception) { /* 坏图回落图标 */ }
        });
        var all = Task.WhenAll(tasks);
        var previous = _idleTask;
        _idleTask = Task.WhenAll(previous, all);
    }

    // ---------- 杂项 ----------
    private void RebuildBreadcrumbs(string path)
    {
        Breadcrumbs.Clear();
        foreach (var segment in _fileSystem.GetBreadcrumbs(path))
            Breadcrumbs.Add(new BreadcrumbSegment(segment.Name, segment.FullPath));
    }

    private void UpdateHistoryFlags(string currentPath)
    {
        CanGoBack = _backStack.Count > 0;
        CanGoForward = _forwardStack.Count > 0;
        var parent = _fileSystem.GetParentPath(currentPath);
        CanGoUp = parent is not null && !PathsEqual(parent, currentPath);
    }

    public async Task NavigateBreadcrumbAsync(BreadcrumbSegment segment) => await NavigateToAsync(segment.FullPath).ConfigureAwait(true);

    private async Task SaveSettingsAsync()
    {
        try { await _settingsStore.SaveSettingsAsync(CancellationToken.None).ConfigureAwait(true); }
        catch (IOException) { }
    }

    private static string FolderDisplayName(string path)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrEmpty(name) ? path : name;
    }

    private static SortField ParseSort(string? value) => Enum.TryParse<SortField>(value, out var field) ? field : SortField.Name;

    private static string SortFieldText(SortField field) => field switch
    {
        SortField.Name => "名称",
        SortField.LastWriteTime => "日期",
        SortField.Size => "大小",
        SortField.Kind => "类型",
        _ => "名称",
    };

    internal static bool PathsEqual(string? a, string? b)
    {
        if (a is null || b is null) return false;
        try
        {
            var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return string.Equals(
                Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), cmp);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (ArgumentException) { return false; }
        catch (NotSupportedException) { return false; }
    }
}

/// <summary>一个浏览器标签页的会话快照。条目集合仍由 BrowserViewModel 持有，切换标签时按路径重载。</summary>
public sealed partial class BrowserTabViewModel : ObservableObject
{
    internal BrowserTabViewModel(string? path, string title)
    {
        _path = path;
        _title = string.IsNullOrWhiteSpace(title) ? "新标签页" : title;
    }

    [ObservableProperty] private string? _path;
    [ObservableProperty] private string _title;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInactive))]
    private bool _isActive;

    [ObservableProperty] private bool _canClose;

    public bool IsInactive => !IsActive;

    internal Stack<string> BackStack { get; } = [];
    internal Stack<string> ForwardStack { get; } = [];
    internal string FilterTextSnapshot { get; set; } = string.Empty;
    internal IReadOnlyList<string> SelectedPathsSnapshot { get; set; } = [];
}
