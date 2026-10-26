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
    private readonly ISettingsStore _settingsStore;
    private readonly IDialogs _dialogs;
    private readonly PreviewViewModel _preview;

    private readonly Stack<string> _backStack = [];
    private readonly Stack<string> _forwardStack = [];
    private List<FileItem> _allItems = [];
    private IDisposable? _watchSubscription;
    private bool _watchEnabled;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _thumbCts;
    private Task _idleTask = Task.CompletedTask;

    private IReadOnlyList<string> _clipboardPaths = [];
    private bool _clipboardCut;

    public BrowserViewModel(
        IFileSystemService fileSystem, IFileOperationService operations, ITrashService trash,
        IArchiveService archive, ISearchService search, ISettingsStore settingsStore,
        IFileInspectorService inspector, IFolderWatchService folderWatch,
        IDialogs dialogs, PreviewViewModel preview)
    {
        _fileSystem = fileSystem;
        _operations = operations;
        _trash = trash;
        _archive = archive;
        _search = search;
        _inspector = inspector;
        _folderWatch = folderWatch;
        _settingsStore = settingsStore;
        _dialogs = dialogs;
        _preview = preview;

        var s = settingsStore.Settings;
        _watchEnabled = s.WatchFolderChanges;
        _viewMode = s.ViewMode switch
        {
            "Grid" => BrowserViewMode.Grid,
            "Details" => BrowserViewMode.Details,
            _ => BrowserViewMode.List,
        };
        _sortFieldLabel = SortFieldText(ParseSort(s.SortField));
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
    [ObservableProperty] private bool _isSelectionMode;
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private bool _canGoBack;
    [ObservableProperty] private bool _canGoForward;
    [ObservableProperty] private bool _canGoUp;

    public ObservableCollection<FileItemViewModel> Items { get; } = [];
    public ObservableCollection<FileItemViewModel> SelectedItems { get; } = [];
    public ObservableCollection<BreadcrumbSegment> Breadcrumbs { get; } = [];

    public bool IsListView => ViewMode == BrowserViewMode.List;
    public bool IsGridView => ViewMode == BrowserViewMode.Grid;
    public bool IsDetailsView => ViewMode == BrowserViewMode.Details;
    public bool HasSelection => SelectedCount > 0;
    public bool HasClipboard => _clipboardPaths.Count > 0;
    public string SelectionText => SelectedCount == 0 ? StatusText : $"已选 {SelectedCount} 项";

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
        _settingsStore.Settings.ViewMode = value.ToString();
        _ = SaveSettingsAsync();
    }

    partial void OnSelectedCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionText));
    }

    partial void OnFilterTextChanged(string value)
    {
        if (!IsSearchMode)
            RefilterAndSort();
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

        CurrentPath = path;
        CurrentFolderName = FileLocator.IsStorageUri(path)
            ? _fileSystem.GetBreadcrumbs(path).LastOrDefault()?.Name ?? path
            : FolderDisplayName(path);
        IsSearchMode = false;
        ExitSelectionMode();
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
    private SortField CurrentSortField => ParseSort(_settingsStore.Settings.SortField);
    private SortDirection CurrentSortDirection =>
        string.Equals(_settingsStore.Settings.SortDirection, "Descending", StringComparison.Ordinal)
            ? SortDirection.Descending : SortDirection.Ascending;

    public void SetSort(SortField field, SortDirection direction)
    {
        _settingsStore.Settings.SortField = field.ToString();
        _settingsStore.Settings.SortDirection = direction.ToString();
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
    private void CopySelection() => SetClipboard(cut: false);

    [RelayCommand]
    private void CutSelection() => SetClipboard(cut: true);

    private void SetClipboard(bool cut)
    {
        if (SelectedItems.Count == 0) return;
        _clipboardPaths = SelectedItems.Select(i => i.FullPath).ToList();
        _clipboardCut = cut;
        OnPropertyChanged(nameof(HasClipboard));
        _dialogs.Notify(cut ? $"已剪切 {_clipboardPaths.Count} 项" : $"已复制 {_clipboardPaths.Count} 项");
    }

    [RelayCommand]
    private async Task PasteAsync()
    {
        if (CurrentPath is null || _clipboardPaths.Count == 0) return;
        var sources = _clipboardPaths;
        var existing = sources
            .Select(s => Path.Combine(CurrentPath, Path.GetFileName(s.TrimEnd('/', '\\'))))
            .Where(p => _fileSystem.Exists(p))
            .ToList();
        var policy = ConflictPolicy.KeepBoth;
        if (existing.Count > 0 && !_clipboardCut)
        {
            // 复制到新位置才需要问；粘回原地默认保留两者
            var pastingInPlace = sources.All(s => PathsEqual(Path.GetDirectoryName(s), CurrentPath));
            if (!pastingInPlace)
            {
                var asked = await _dialogs.AskConflictAsync(Path.GetFileName(existing[0])).ConfigureAwait(true);
                if (asked is null) return;
                policy = asked.Value;
            }
        }

        var cut = _clipboardCut;
        var title = cut ? "正在移动…" : "正在复制…";
        var outcome = await _dialogs.RunOperationAsync(title, (progress, ct) =>
            cut
                ? _operations.MoveAsync(sources, CurrentPath, policy, progress, ct)
                : _operations.CopyAsync(sources, CurrentPath, policy, progress, ct)).ConfigureAwait(true);
        if (cut && outcome.Success)
        {
            _clipboardPaths = [];
            OnPropertyChanged(nameof(HasClipboard));
        }
        await ReloadAfterOperationAsync().ConfigureAwait(true);
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
    }
}
