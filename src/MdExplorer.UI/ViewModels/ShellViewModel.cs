using System.Collections.ObjectModel;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Md3.Avalonia.Controls;
using MdExplorer.Application;
using MdExplorer.Domain;
using MdExplorer.Platform.Abstractions;
using MdExplorer.UI.Services;
using MdExplorer.UI.Preview;

namespace MdExplorer.UI.ViewModels;

public enum ShellPage { Home, Browser, Favorites, Trash, Settings, SearchCenter, StorageInsights, ArchiveWorkbench }
public enum SizeClass { Compact, Medium, Expanded, Large, ExtraLarge }
internal enum DialogKind { None, Prompt, Confirm, Conflict, Message, Progress, BatchRename }

/// <summary>根视图模型：页面切换、断点状态、主页数据、设置、对话框与操作进度（实现 IDialogs）。</summary>
public sealed partial class ShellViewModel : ObservableObject, IDialogs
{
    private readonly AppHost.AppServices _services;
    private readonly SemaphoreSlim _favoritesRefreshGate = new(1, 1);
    private readonly SemaphoreSlim _recentsRefreshGate = new(1, 1);
    private int _favoritesRefreshGeneration;
    private int _recentsRefreshGeneration;
    private CancellationTokenSource? _searchCenterCts;
    private List<ArchiveEntryItem> _archiveEntries = [];
    private readonly Dictionary<string, ArchiveFolderAggregate> _archiveFolders = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<ArchiveEntryItem>> _archiveFilesByParent = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _archiveChildFolders = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _archiveDescendantsByFolder = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ArchiveEntryItem> _archiveExplicitFolders = new(StringComparer.Ordinal);
    private readonly HashSet<string> _selectedArchiveEntries = new(StringComparer.Ordinal);
    private int _archiveGeneration;
    private int _archivePreviewGeneration;
    private Func<IFileLauncherService> _launcher;
    private IFileLauncherService Launcher => _launcher();

    public BrowserViewModel Browser { get; }
    public PreviewViewModel Preview { get; }
    public PlatformCapabilities Capabilities => _services.Capabilities;
    public ISettingsStore SettingsStore => _services.SettingsStore;

    private readonly List<AuthorizedRoot> _persistedRoots = [];

    /// <summary>已授权文件夹入口（Android SAF；含会话内与书签恢复）。</summary>
    public ObservableCollection<AuthorizedRootModel> AuthorizedRoots { get; } = [];

    public bool HasAuthorizedRoots => AuthorizedRoots.Count > 0;
    public int AvailableAuthorizedRootCount => AuthorizedRoots.Count(root => root.IsAvailable);
    public int UnavailableAuthorizedRootCount => AuthorizedRoots.Count(root => root.NeedsReauthorization);
    public bool HasUnavailableAuthorizedRoots => UnavailableAuthorizedRootCount > 0;
    public string AuthorizedRootsSummaryText => AuthorizedRoots.Count == 0
        ? "尚未授权文件夹"
        : UnavailableAuthorizedRootCount == 0
            ? $"{AvailableAuthorizedRootCount} 个可用"
            : $"{AvailableAuthorizedRootCount} 个可用 · {UnavailableAuthorizedRootCount} 个需重新授权";

    public ShellViewModel(AppHost.AppServices services)
    {
        _services = services;
        _launcher = () => services.Launcher;
        Preview = new PreviewViewModel(MdExplorer.UI.Preview.BuiltInPreviewProviders.Create(services.Archive, services.FileSystem), () => Launcher);
        _persistedRoots.AddRange(services.PersistedRoots);
        AuthorizedRoots.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasAuthorizedRoots));
            OnPropertyChanged(nameof(AvailableAuthorizedRootCount));
            OnPropertyChanged(nameof(UnavailableAuthorizedRootCount));
            OnPropertyChanged(nameof(HasUnavailableAuthorizedRoots));
            OnPropertyChanged(nameof(AuthorizedRootsSummaryText));
        };
        Browser = new BrowserViewModel(
            services.FileSystem, services.Operations, services.Trash,
            services.Archive, services.Search, services.SettingsStore,
            services.Inspector, services.FolderWatch, services.SystemClipboard,
            this, Preview, () => Launcher);
        Browser.FavoritesChanged += async (_, _) => await RefreshFavoritesAsync().ConfigureAwait(true);
        Browser.RecentsChanged += async (_, _) => await RefreshRecentsAsync().ConfigureAwait(true);
        Browser.ArchiveWorkbenchRequested += async (_, path) => await OpenArchiveWorkbenchAsync(path).ConfigureAwait(true);
        Preview.ArchiveWorkbenchRequested += async (_, path) => await OpenArchiveWorkbenchAsync(path).ConfigureAwait(true);
        Browser.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(BrowserViewModel.CurrentFolderName))
                OnPropertyChanged(nameof(PageTitle));
            if (e.PropertyName is nameof(BrowserViewModel.IsSearchMode))
                OnPropertyChanged(nameof(PageTitle));
            if (e.PropertyName is nameof(BrowserViewModel.HasMultipleTabs))
                OnPropertyChanged(nameof(ShowBrowserTabs));
            if (e.PropertyName is nameof(BrowserViewModel.IsAddressEditing))
            {
                OnPropertyChanged(nameof(ShowBreadcrumbBar));
                OnPropertyChanged(nameof(ShowCompactPath));
                OnPropertyChanged(nameof(ShowPathRowActions));
            }
        };
        FavoriteItems.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasFavorites));
        RecentFolders.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasRecentFolders));
            OnPropertyChanged(nameof(HasRecents));
        };
        RecentFiles.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasRecentFiles));
            OnPropertyChanged(nameof(HasRecents));
        };
        TrashItems.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ShowTrashEmpty));
            OnPropertyChanged(nameof(HasTrashItems));
            OnPropertyChanged(nameof(TrashItemCountText));
            OnPropertyChanged(nameof(TrashSummaryText));
        };
        SearchIndexRoots.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasSearchIndexRoots));
            OnPropertyChanged(nameof(SearchIndexRootCountText));
            OnPropertyChanged(nameof(SearchIndexStateText));
        };
        SearchCenterResults.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasSearchCenterResults));
            OnPropertyChanged(nameof(ShowSearchCenterEmpty));
        };
        StorageKindUsages.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasStorageKindUsages));
        StorageTopFolders.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasStorageTopFolders));
        StorageLargestFiles.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasStorageLargestFiles));
        StorageDuplicateGroups.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasStorageDuplicateGroups));
        ArchiveVisibleEntries.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasArchiveVisibleEntries));
            OnPropertyChanged(nameof(ShowArchiveEmpty));
        };
        OperationTasks.CollectionChanged += (_, _) => RefreshTaskCenterSummary();
        Preview.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(PreviewViewModel.HasItem))
            {
                OnPropertyChanged(nameof(ShowPreviewPaneNow));
                OnPropertyChanged(nameof(ShowPreviewPaneEffective));
            }
        };

        var s = services.SettingsStore.Settings;
        _themeMode = s.ThemeMode;
        _seedColor = s.SeedColor;
        _showHidden = s.ShowHidden;
        _foldersFirst = s.FoldersFirst;
        _showPreviewPane = s.ShowPreviewPane;
        _reduceMotion = s.ReduceMotion;
        _rememberFolderView = s.RememberFolderView;
        _watchFolderChanges = s.WatchFolderChanges;
        _useSearchIndex = s.UseSearchIndex;
        RefreshSearchIndexRoots();
    }

    /// <summary>Android 挂载后将打开方式切换为 Avalonia Launcher 实现。</summary>
    public void UseLauncher(IFileLauncherService launcher) => _launcher = () => launcher;

    /// <summary>视图挂载后把 TopLevel 提供者交给 Avalonia 系统剪贴板实现（测试替身则忽略）。</summary>
    public void UseSystemClipboard(Func<TopLevel?> topLevelProvider)
    {
        if (_services.SystemClipboard is AvaloniaSystemClipboardService clipboard)
            clipboard.TopLevelProvider = topLevelProvider;
    }

    // ---------- 页面与断点 ----------
    [ObservableProperty] private ShellPage _page = ShellPage.Home;
    [ObservableProperty] private SizeClass _sizeClass = SizeClass.Large;
    [ObservableProperty] private bool _isDrawerOpen;
    [ObservableProperty] private bool _isPreviewPageOpen;

    public string DataDirectoryText => _services.DataDirectory;

    /// <summary>设置页版本串：跟随程序集信息版本，避免每次发版手工改 XAML。</summary>
    public string AppVersionText
    {
        get
        {
            var assembly = typeof(ShellViewModel).Assembly;
            var informational = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational))
                return informational.Split('+', 2)[0];
            var version = assembly.GetName().Version;
            return version is null ? "开发版" : $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    public string PageTitle => Page switch
    {
        ShellPage.Home => "文件",
        ShellPage.Favorites => "收藏",
        ShellPage.Trash => Capabilities.UsesAppTrash ? "应用内回收站" : "回收站",
        ShellPage.Settings => "设置",
        ShellPage.SearchCenter => "搜索中心",
        ShellPage.StorageInsights => "存储洞察",
        ShellPage.ArchiveWorkbench => string.IsNullOrWhiteSpace(ArchiveName) ? "压缩包" : ArchiveName,
        _ => Browser.IsSearchMode ? "搜索结果" : Browser.CurrentFolderName,
    };

    public bool HasFavorites => FavoriteItems.Count > 0;
    public bool HasRecentFolders => RecentFolders.Count > 0;
    public bool HasRecentFiles => RecentFiles.Count > 0;
    public bool HasRecents => HasRecentFolders || HasRecentFiles;
    /// <summary>可浏览回收站为空时展示占位（Windows 仍走平台说明分支）。</summary>
    public bool ShowTrashEmpty => Capabilities.CanBrowseTrash && TrashItems.Count == 0;
    public bool HasTrashItems => TrashItems.Count > 0;
    public string TrashItemCountText => TrashItems.Count.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

    public bool IsHome => Page == ShellPage.Home;
    public bool IsBrowser => Page == ShellPage.Browser;
    public bool IsFavoritesPage => Page == ShellPage.Favorites;
    public bool IsTrashPage => Page == ShellPage.Trash;
    public bool IsSettingsPage => Page == ShellPage.Settings;
    public bool IsSearchCenterPage => Page == ShellPage.SearchCenter;
    public bool IsStorageInsightsPage => Page == ShellPage.StorageInsights;
    public bool IsArchiveWorkbenchPage => Page == ShellPage.ArchiveWorkbench;

    public bool IsCompact => SizeClass == SizeClass.Compact;
    public bool IsMedium => SizeClass == SizeClass.Medium;
    public bool IsExpandedOrAbove => SizeClass >= SizeClass.Expanded;
    public bool IsLargeOrAbove => SizeClass >= SizeClass.Large;
    public bool IsCompactOrMedium => SizeClass <= SizeClass.Medium;
    public bool ShowBottomNav => IsCompact;
    public bool ShowRail => IsMedium;
    public bool ShowDrawerPane => IsExpandedOrAbove;
    public bool ShowPreviewPaneNow => IsExpandedOrAbove && SettingsStore.Settings.ShowPreviewPane && Preview.HasItem;
    public bool ShowPreviewPaneEffective => IsBrowser && ShowPreviewPaneNow;
    public bool ShowPreviewPageNow => IsPreviewPageOpen && IsCompactOrMedium;
    /// <summary>Compact 单标签时隐藏标签轨，避免与顶栏标题重复；出现第二个标签后即显示。</summary>
    public bool ShowBrowserTabs => !IsCompact || Browser.HasMultipleTabs;
    /// <summary>完整面包屑只在非 Compact 显示；窄屏折叠为当前目录胶囊。</summary>
    public bool ShowBreadcrumbBar => !IsCompact && !Browser.IsAddressEditing;
    public bool ShowCompactPath => IsCompact && !Browser.IsAddressEditing;
    /// <summary>Compact 编辑地址时让路径输入独占整行，导航/搜索/溢出按钮暂时让位。</summary>
    public bool ShowPathRowActions => !IsCompact || !Browser.IsAddressEditing;

    partial void OnPageChanged(ShellPage value)
    {
        if (value != ShellPage.Browser)
            Browser.IsSearchPanelOpen = false;
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(IsHome));
        OnPropertyChanged(nameof(IsBrowser));
        OnPropertyChanged(nameof(IsFavoritesPage));
        OnPropertyChanged(nameof(IsTrashPage));
        OnPropertyChanged(nameof(IsSettingsPage));
        OnPropertyChanged(nameof(IsSearchCenterPage));
        OnPropertyChanged(nameof(IsStorageInsightsPage));
        OnPropertyChanged(nameof(IsArchiveWorkbenchPage));
        OnPropertyChanged(nameof(ShowPreviewPaneEffective));
    }

    partial void OnSizeClassChanged(SizeClass value)
    {
        foreach (var name in new[]
                 {
                     nameof(IsCompact), nameof(IsMedium), nameof(IsExpandedOrAbove), nameof(IsLargeOrAbove), nameof(IsCompactOrMedium),
                     nameof(ShowBottomNav), nameof(ShowRail), nameof(ShowDrawerPane), nameof(ShowBrowserTabs),
                     nameof(ShowBreadcrumbBar), nameof(ShowCompactPath), nameof(ShowPathRowActions),
                     nameof(ShowPreviewPaneNow), nameof(ShowPreviewPageNow), nameof(ShowPreviewPaneEffective),
                 })
            OnPropertyChanged(name);
        if (IsExpandedOrAbove)
        {
            IsDrawerOpen = false;
            IsPreviewPageOpen = false;
        }
    }

    partial void OnIsPreviewPageOpenChanged(bool value) => OnPropertyChanged(nameof(ShowPreviewPageNow));

    /// <summary>由视图在尺寸变化时调用（开发文档 §7：MD3 五档断点）。</summary>
    public void SetViewportSize(double widthDip, double heightDip)
    {
        var next = widthDip switch
        {
            < 600 => SizeClass.Compact,
            < 840 => SizeClass.Medium,
            < 1200 => SizeClass.Expanded,
            < 1600 => SizeClass.Large,
            _ => SizeClass.ExtraLarge,
        };
        if (next != SizeClass)
            SizeClass = next;
    }

    public void RefreshPreviewVisibility()
    {
        OnPropertyChanged(nameof(ShowPreviewPaneNow));
        OnPropertyChanged(nameof(ShowPreviewPaneEffective));
    }

    // ---------- 主页数据 ----------
    public ObservableCollection<VolumeItemModel> Volumes { get; } = [];
    public ObservableCollection<NavEntry> Places { get; } = [];
    public ObservableCollection<FileItemViewModel> FavoriteItems { get; } = [];
    public ObservableCollection<NavEntry> RecentFolders { get; } = [];
    public ObservableCollection<FileItemViewModel> RecentFiles { get; } = [];
    public ObservableCollection<TrashItemModel> TrashItems { get; } = [];
    public ObservableCollection<SearchIndexRootModel> SearchIndexRoots { get; } = [];
    public ObservableCollection<FileItemViewModel> SearchCenterResults { get; } = [];
    public ObservableCollection<StorageKindUsageModel> StorageKindUsages { get; } = [];
    public ObservableCollection<StorageFolderUsageModel> StorageTopFolders { get; } = [];
    public ObservableCollection<FileItemViewModel> StorageLargestFiles { get; } = [];
    public ObservableCollection<StorageDuplicateGroupModel> StorageDuplicateGroups { get; } = [];
    public ObservableCollection<ArchiveWorkbenchEntryViewModel> ArchiveVisibleEntries { get; } = [];
    public ObservableCollection<ArchiveBreadcrumbModel> ArchiveBreadcrumbs { get; } = [];
    public ObservableCollection<OperationTaskViewModel> OperationTasks { get; } = [];

    [ObservableProperty] private bool _isTaskCenterOpen;

    public bool HasOperationTasks => OperationTasks.Count > 0;
    public int ActiveOperationTaskCount => OperationTasks.Count(task => task.IsActive);
    public bool HasActiveOperationTasks => ActiveOperationTaskCount > 0;
    public string ActiveOperationTaskCountText => ActiveOperationTaskCount > 99
        ? "99+"
        : ActiveOperationTaskCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public string TaskCenterText => ActiveOperationTaskCount switch
    {
        0 => "任务",
        1 => "任务 · 运行中",
        var count => $"任务 · {count} 个进行中",
    };


    private static readonly (KnownFolder Folder, string Title, Func<string?> Glyph)[] KnownPlaceDefs =
    [
        (KnownFolder.Documents, "文档", () => MdSymbols.Description),
        (KnownFolder.Downloads, "下载", () => MdSymbols.Download),
        (KnownFolder.Pictures, "图片", () => MdSymbols.Image),
        (KnownFolder.Music, "音乐", () => MdSymbols.MusicNote),
        (KnownFolder.Videos, "视频", () => MdSymbols.Movie),
        (KnownFolder.Desktop, "桌面", () => MdSymbols.Terminal),
        (KnownFolder.Home, "主目录", () => MdSymbols.Home),
    ];

    public async Task InitializeAsync()
    {
        await RefreshHomeAsync().ConfigureAwait(true);
        await Browser.RestoreTabsAsync(_services.StartFolder).ConfigureAwait(true);
    }

    public async Task RefreshHomeAsync()
    {
        Places.Clear();
        foreach (var (folder, title, glyph) in KnownPlaceDefs)
        {
            var path = _services.KnownFolders.Get(folder);
            if (path is not null)
                Places.Add(new NavEntry(title, glyph(), path));
        }
        await RefreshFavoritesAsync().ConfigureAwait(true);
        await RefreshRecentsAsync().ConfigureAwait(true);
        Volumes.Clear();
        var volumes = await _services.Volumes.GetVolumesAsync(CancellationToken.None).ConfigureAwait(true);
        foreach (var volume in volumes)
        {
            var usage = volume.TotalBytes is not null
                ? $"已用 {Formatting.Bytes(volume.UsedBytes)} / {Formatting.Bytes(volume.TotalBytes)}"
                : volume.Root.Path;
            Volumes.Add(new VolumeItemModel(
                volume.DisplayName, volume.Root.Path, usage,
                (volume.UsedFraction ?? 0) * 100,
                volume.IsRemovable ? MdSymbols.SdCard : MdSymbols.Storage));
        }
    }

    private async Task RefreshFavoritesAsync()
    {
        var generation = Interlocked.Increment(ref _favoritesRefreshGeneration);
        await _favoritesRefreshGate.WaitAsync().ConfigureAwait(true);
        try
        {
            var resolved = new List<FileItemViewModel>();
            foreach (var path in _services.SettingsStore.Favorites)
            {
                var item = await _services.FileSystem.StatAsync(path, CancellationToken.None).ConfigureAwait(true);
                if (item is not null)
                    resolved.Add(new FileItemViewModel(item) { IsFavorite = true });
            }
            if (Volatile.Read(ref _favoritesRefreshGeneration) != generation)
                return;
            FavoriteItems.Clear();
            foreach (var item in resolved)
                FavoriteItems.Add(item);
        }
        finally
        {
            _favoritesRefreshGate.Release();
        }
    }

    private async Task RefreshRecentsAsync()
    {
        // 初始化、导航与打开文件可连续触发刷新；串行化并先构建快照，避免 Clear/Add 交错产生重复项。
        // 代次同时让清除动作失效所有在途快照，防止旧结果在清除后回填。
        var generation = Interlocked.Increment(ref _recentsRefreshGeneration);
        await _recentsRefreshGate.WaitAsync().ConfigureAwait(true);
        try
        {
            var folders = new List<NavEntry>();
            foreach (var path in _services.SettingsStore.Settings.RecentFolderPaths.Take(12))
            {
                var item = await _services.FileSystem.StatAsync(path, CancellationToken.None).ConfigureAwait(true);
                if (item?.IsFolder == true)
                {
                    // 字形由 Home 视图模板提供：MdSymbols 需要 Avalonia 字体服务，VM 保持可在无头单元测试中独立刷新。
                    folders.Add(new NavEntry(
                        item.Name,
                        null,
                        item.FullPath,
                        _services.FileSystem.GetParentPath(item.FullPath)));
                }
            }

            var files = new List<FileItemViewModel>();
            foreach (var path in _services.SettingsStore.Settings.RecentFilePaths.Take(12))
            {
                var item = await _services.FileSystem.StatAsync(path, CancellationToken.None).ConfigureAwait(true);
                if (item is not null && !item.IsFolder)
                    files.Add(new FileItemViewModel(item) { IsFavorite = _services.SettingsStore.IsFavorite(path) });
            }

            if (Volatile.Read(ref _recentsRefreshGeneration) != generation)
                return;
            RecentFolders.Clear();
            foreach (var folder in folders)
                RecentFolders.Add(folder);
            RecentFiles.Clear();
            foreach (var file in files)
                RecentFiles.Add(file);
        }
        finally
        {
            _recentsRefreshGate.Release();
        }
    }

    [RelayCommand]
    private async Task ClearRecentsAsync()
    {
        Interlocked.Increment(ref _recentsRefreshGeneration);
        _services.SettingsStore.Settings.RecentFolderPaths.Clear();
        _services.SettingsStore.Settings.RecentFilePaths.Clear();
        RecentFolders.Clear();
        RecentFiles.Clear();
        await SaveAsync().ConfigureAwait(true);
        Notify("已清除最近访问");
    }

    public async Task OpenRecentFileAsync(FileItemViewModel item)
    {
        Page = ShellPage.Browser;
        IsDrawerOpen = false;
        IsPreviewPageOpen = false;
        await Browser.NavigateToFileLocationAsync(item.FullPath).ConfigureAwait(true);
        await Browser.RecordRecentFileAsync(item.FullPath).ConfigureAwait(true);
    }

    // ---------- 导航命令 ----------
    [RelayCommand]
    private void GoHome()
    {
        Page = ShellPage.Home;
        IsDrawerOpen = false;
        IsPreviewPageOpen = false;
        _ = RefreshHomeAsync();
    }

    [RelayCommand]
    private void GoFavorites()
    {
        Page = ShellPage.Favorites;
        IsDrawerOpen = false;
        IsPreviewPageOpen = false;
        _ = RefreshFavoritesAsync();
    }

    [RelayCommand]
    private async Task GoTrashAsync()
    {
        Page = ShellPage.Trash;
        IsDrawerOpen = false;
        await RefreshTrashAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void GoSettings()
    {
        Page = ShellPage.Settings;
        IsDrawerOpen = false;
        IsPreviewPageOpen = false;
        _ = RefreshSearchIndexAsync();
        if (Capabilities.CanBrowseTrash)
            _ = RefreshTrashAsync();
    }

    [RelayCommand]
    private async Task GoSearchCenterAsync()
    {
        Page = ShellPage.SearchCenter;
        IsDrawerOpen = false;
        IsPreviewPageOpen = false;
        await RefreshSearchIndexAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task GoStorageInsightsAsync()
    {
        Page = ShellPage.StorageInsights;
        IsDrawerOpen = false;
        IsPreviewPageOpen = false;
        if (!HasStorageRoot)
        {
            var current = Browser.CurrentPath;
            if (!string.IsNullOrWhiteSpace(current) && !FileLocator.IsStorageUri(current) && Directory.Exists(current))
                TrySetStorageRoot(current, notifyOnFailure: false);
            else
                TrySetStorageRoot(_services.StartFolder, notifyOnFailure: false);
        }

        await RefreshHomeAsync().ConfigureAwait(true);
    }

    public async Task OpenFolderAsync(string path)
    {
        Page = ShellPage.Browser;
        IsDrawerOpen = false;
        IsPreviewPageOpen = false;
        await Browser.NavigateToAsync(path).ConfigureAwait(true);
    }

    /// <summary>MainView 挂载后注入平台 StorageProvider 并恢复书签授权（Android SAF；桌面不调用）。</summary>
    public async Task AttachStorageAsync(IStorageProvider provider)
    {
        _services.StorageFileSystem.StorageProvider = provider;
        foreach (var root in _persistedRoots.ToArray())
        {
            IStorageFolder? folder = null;
            try
            {
                folder = await provider.OpenFolderBookmarkAsync(root.Bookmark).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException)
            {
                // 在下方保留失效行，供用户定点重新授权。
            }

            if (folder is null)
            {
                AddAuthorizedRootModel(root.Name, string.Empty, root.Bookmark, isAvailable: false, isPersistent: true);
                continue;
            }

            var uri = _services.StorageFileSystem.RegisterRoot(folder);
            if (await IsAuthorizedRootReachableAsync(uri).ConfigureAwait(true))
            {
                AddAuthorizedRootModel(folder.Name, uri, root.Bookmark, isAvailable: true, isPersistent: true);
            }
            else
            {
                _services.StorageFileSystem.ForgetRoot(uri);
                AddAuthorizedRootModel(root.Name, string.Empty, root.Bookmark, isAvailable: false, isPersistent: true);
            }
        }
    }

    private async Task<bool> IsAuthorizedRootReachableAsync(string uri)
    {
        try
        {
            await using var enumerator = _services.FileSystem
                .ListAsync(uri, includeHidden: false, CancellationToken.None)
                .GetAsyncEnumerator();
            _ = await enumerator.MoveNextAsync().ConfigureAwait(true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    [RelayCommand]
    private async Task PickAuthorizedFolderAsync() =>
        await PickAuthorizedFolderCoreAsync().ConfigureAwait(true);

    private async Task<bool> PickAuthorizedFolderCoreAsync()
    {
        var provider = _services.StorageFileSystem.StorageProvider;
        if (provider is null || !provider.CanPickFolder)
        {
            Notify("当前环境不支持文件夹选择");
            return false;
        }
        IReadOnlyList<IStorageFolder> picked;
        try
        {
            picked = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "选择要在 Eidoria 中浏览的文件夹",
                AllowMultiple = true,
            }).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException)
        {
            Notify("无法打开系统文件夹选择器");
            return false;
        }
        string? firstUri = null;
        foreach (var folder in picked)
        {
            var uri = _services.StorageFileSystem.RegisterRoot(folder);
            firstUri ??= uri;
            string? bookmark = null;
            try
            {
                // 书签持久化尽力而为：失败时该授权仅本次会话有效，管理行会如实标注。
                if (folder.CanBookmark)
                    bookmark = await folder.SaveBookmarkAsync().ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException) { }

            if (bookmark is not null)
            {
                _persistedRoots.RemoveAll(r => r.Name == folder.Name || r.Bookmark == bookmark);
                _persistedRoots.Add(new AuthorizedRoot(folder.Name, bookmark));
            }
            AddAuthorizedRootModel(folder.Name, uri, bookmark, isAvailable: true, isPersistent: bookmark is not null);
        }
        if (picked.Count > 0)
        {
            await SaveRootsAsync().ConfigureAwait(true);
            if (firstUri is not null)
                await OpenFolderAsync(firstUri).ConfigureAwait(true);
            return true;
        }
        return false;
    }

    /// <summary>可用授权直接打开；失效授权定点进入系统选择器续授。</summary>
    public async Task OpenAuthorizedRootAsync(AuthorizedRootModel root)
    {
        if (root.IsAvailable && !string.IsNullOrWhiteSpace(root.Path))
            await OpenFolderAsync(root.Path).ConfigureAwait(true);
        else
            await ReauthorizeAuthorizedRootAsync(root).ConfigureAwait(true);
    }

    public async Task ReauthorizeAuthorizedRootAsync(AuthorizedRootModel root)
    {
        if (root.IsAvailable)
        {
            await OpenAuthorizedRootAsync(root).ConfigureAwait(true);
            return;
        }

        if (!await PickAuthorizedFolderCoreAsync().ConfigureAwait(true))
            return;

        AuthorizedRoots.Remove(root);
        if (!string.IsNullOrWhiteSpace(root.Bookmark))
            _persistedRoots.RemoveAll(r => r.Bookmark == root.Bookmark);
        await SaveRootsAsync().ConfigureAwait(true);
    }

    /// <summary>移除 Eidoria 中的授权入口与书签；不删除真实文件，系统侧授权仍可在系统设置中撤销。</summary>
    public async Task RemoveAuthorizedRootAsync(AuthorizedRootModel root)
    {
        var leavingCurrent = root.IsAvailable &&
                             Browser.CurrentPath is { } current &&
                             IsPathWithinAuthorizedRoot(current, root.Path);
        AuthorizedRoots.Remove(root);
        _persistedRoots.RemoveAll(r =>
            (!string.IsNullOrWhiteSpace(root.Bookmark) && r.Bookmark == root.Bookmark) ||
            r.Name == root.Name);
        if (root.IsAvailable && !string.IsNullOrWhiteSpace(root.Path))
            _services.StorageFileSystem.ForgetRoot(root.Path);
        await SaveRootsAsync().ConfigureAwait(true);
        if (leavingCurrent)
            await OpenFolderAsync(_services.StartFolder).ConfigureAwait(true);
        Notify($"已移除授权入口：{root.Name}（未删除文件）");
    }

    private bool IsPathWithinAuthorizedRoot(string path, string rootPath)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        string? current = path;
        while (current is not null && visited.Add(current))
        {
            if (string.Equals(current, rootPath, StringComparison.Ordinal))
                return true;
            current = _services.FileSystem.GetParentPath(current);
        }
        return false;
    }

    /// <summary>父文件夹定位串（双形态统一，供收藏跳转等跨界调用）。</summary>
    public string? BrowserParentOf(string path) => _services.FileSystem.GetParentPath(path);

    private void AddAuthorizedRootModel(
        string name,
        string uri,
        string? bookmark,
        bool isAvailable,
        bool isPersistent)
    {
        var model = new AuthorizedRootModel(name, uri, bookmark, isAvailable, isPersistent);
        var index = -1;
        for (var i = 0; i < AuthorizedRoots.Count; i++)
        {
            var existing = AuthorizedRoots[i];
            if ((isAvailable && existing.Path == uri) ||
                (!string.IsNullOrWhiteSpace(bookmark) && existing.Bookmark == bookmark) ||
                (!existing.IsAvailable && existing.Name == name))
            {
                index = i;
                break;
            }
        }

        if (index >= 0)
            AuthorizedRoots[index] = model;
        else
            AuthorizedRoots.Add(model);
    }

    private async Task SaveRootsAsync()
    {
        try { await AuthorizedRootsStore.SaveAsync(_services.DataDirectory, _persistedRoots).ConfigureAwait(true); }
        catch (IOException) { }
    }

    [RelayCommand]
    private void ToggleDrawer() => IsDrawerOpen = !IsDrawerOpen;

    [RelayCommand]
    private void ClosePreviewPage() => IsPreviewPageOpen = false;

    [RelayCommand]
    private void ToggleTaskCenter() => IsTaskCenterOpen = !IsTaskCenterOpen;

    [RelayCommand]
    private void CloseTaskCenter() => IsTaskCenterOpen = false;

    [RelayCommand]
    private void ClearFinishedTasks()
    {
        foreach (var task in OperationTasks.Where(task => task.IsFinished).ToArray())
        {
            OperationTasks.Remove(task);
            task.Dispose();
        }
    }

    [RelayCommand]
    private async Task ShowKeyboardShortcutsAsync()
    {
        await ShowPropertiesAsync("键盘快捷键", string.Join("\n", [
            "导航",
            "· Ctrl+L：编辑路径    Ctrl+F / Ctrl+K：悬浮搜索面板（回车递归搜索）",
            "· Alt+← / Alt+→：后退 / 前进    Alt+↑：上一级",
            "· F5 或 Ctrl+R：刷新",
            "",
            "标签页",
            "· Ctrl+T：复制当前标签    Ctrl+W：关闭标签",
            "· Ctrl+Tab / Ctrl+Shift+Tab：切换标签    Ctrl+Shift+T：恢复已关闭标签",
            "",
            "文件操作",
            "· Ctrl+C / Ctrl+X / Ctrl+V：复制 / 剪切 / 粘贴",
            "· Ctrl+Shift+C：复制路径    Ctrl+A：全选",
            "· F2：重命名    Delete：删除    Enter：打开",
            "",
            "F1：打开此快捷键说明",
        ])).ConfigureAwait(true);
    }

    private void RefreshTaskCenterSummary()
    {
        OnPropertyChanged(nameof(HasOperationTasks));
        OnPropertyChanged(nameof(ActiveOperationTaskCount));
        OnPropertyChanged(nameof(HasActiveOperationTasks));
        OnPropertyChanged(nameof(ActiveOperationTaskCountText));
        OnPropertyChanged(nameof(TaskCenterText));
    }

    /// <summary>文件被点开时由视图调用：小断点进预览页，大断点预览窗格随选集变化即可。</summary>
    public void OnFileOpened(FileItemViewModel item)
    {
        _ = Browser.RecordRecentFileAsync(item.FullPath);
        if (IsCompactOrMedium)
            IsPreviewPageOpen = true;
    }

    /// <summary>类别入口：优先打开对应已知目录。</summary>
    public async Task OpenCategoryAsync(FileKind kind)
    {
        var folder = kind switch
        {
            FileKind.Image => KnownFolder.Pictures,
            FileKind.Video => KnownFolder.Videos,
            FileKind.Audio => KnownFolder.Music,
            FileKind.Document => KnownFolder.Documents,
            _ => (KnownFolder?)null,
        };
        var path = folder is { } f ? _services.KnownFolders.Get(f) : null;
        if (path is not null)
        {
            await OpenFolderAsync(path).ConfigureAwait(true);
        }
        else
        {
            Page = ShellPage.Browser;
            var home = _services.KnownFolders.Get(KnownFolder.Home) ?? _services.StartFolder;
            await Browser.SearchRecursiveAsync(string.Empty, new HashSet<FileKind> { kind }, home).ConfigureAwait(true);
        }
    }

    /// <summary>同步预判系统返回是否会被消费（决定 RoutedEventArgs.Handled）。</summary>
    public bool WouldHandleBack =>
        IsDialogOpen || IsTaskCenterOpen || IsDrawerOpen || IsPreviewPageOpen || Browser.IsSearchPanelOpen ||
        Page != ShellPage.Home ||
        Browser.CanConsumeBack;

    /// <summary>顶级返回处理（Android 返回键/桌面 Alt+← 的兜底序）。返回是否已消费。</summary>
    public async Task<bool> TryGoBackAsync()
    {
        if (IsDialogOpen) return true; // 模态开着时吞掉返回，由对话框按钮收尾
        if (IsTaskCenterOpen) { IsTaskCenterOpen = false; return true; }
        if (Browser.IsSearchPanelOpen) { Browser.IsSearchPanelOpen = false; return true; }
        if (IsDrawerOpen) { IsDrawerOpen = false; return true; }
        if (IsPreviewPageOpen) { IsPreviewPageOpen = false; return true; }
        if (Page == ShellPage.ArchiveWorkbench)
        {
            if (!string.IsNullOrEmpty(ArchiveCurrentFolder))
            {
                NavigateArchiveFolder(ArchiveParentFolder(ArchiveCurrentFolder));
            }
            else
            {
                Page = ShellPage.Browser;
            }
            return true;
        }
        if (Page == ShellPage.Browser && await Browser.TryGoBackAsync().ConfigureAwait(true)) return true;
        if (Page != ShellPage.Home) { GoHome(); return true; }
        return false;
    }

    // ---------- 回收站页 ----------
    [ObservableProperty] private long _trashUsedBytes;
    [ObservableProperty] private long? _trashQuotaBytes;
    [ObservableProperty] private int? _trashRetentionDays;
    [ObservableProperty] private double _trashUsedPercent;

    public string TrashTitle => Capabilities.UsesAppTrash ? "应用内回收站" : "回收站";
    public bool HasTrashQuota => TrashQuotaBytes is > 0;
    public string TrashUsedText => Formatting.Bytes(TrashUsedBytes);
    public string TrashQuotaText => TrashQuotaBytes is { } quota ? Formatting.Bytes(quota) : "系统管理";
    public string TrashRetentionText => TrashRetentionDays is { } days ? $"{days} 天后自动清理" : "由系统管理";
    public string TrashSummaryText => $"{TrashItemCountText} 项 · {TrashUsedText}";

    partial void OnTrashUsedBytesChanged(long value)
    {
        OnPropertyChanged(nameof(TrashUsedText));
        OnPropertyChanged(nameof(TrashSummaryText));
    }

    partial void OnTrashQuotaBytesChanged(long? value)
    {
        OnPropertyChanged(nameof(HasTrashQuota));
        OnPropertyChanged(nameof(TrashQuotaText));
    }

    partial void OnTrashRetentionDaysChanged(int? value) => OnPropertyChanged(nameof(TrashRetentionText));

    private async Task RefreshTrashAsync()
    {
        TrashItems.Clear();
        if (!Capabilities.CanBrowseTrash)
        {
            ApplyTrashStats(new TrashStats(0, 0));
            return;
        }

        try
        {
            var items = await _services.Trash.ListAsync(CancellationToken.None).ConfigureAwait(true);
            foreach (var item in items)
                TrashItems.Add(new TrashItemModel(
                    item.Id,
                    item.OriginalName,
                    item.OriginalPath,
                    item.DeletedAt == default ? "" : Formatting.DateTime(item.DeletedAt, DateTimeOffset.Now),
                    item.IsFolder,
                    item.SizeBytes));
            ApplyTrashStats(await _services.Trash.GetStatsAsync(CancellationToken.None).ConfigureAwait(true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            ApplyTrashStats(new TrashStats(0, 0));
            Notify($"无法读取回收站：{ex.Message}");
        }
    }

    private void ApplyTrashStats(TrashStats stats)
    {
        TrashUsedBytes = stats.UsedBytes;
        TrashQuotaBytes = stats.QuotaBytes;
        TrashRetentionDays = stats.RetentionDays;
        TrashUsedPercent = stats.UsedPercent;
        OnPropertyChanged(nameof(TrashSummaryText));
    }

    [RelayCommand]
    private async Task RestoreTrashAsync(TrashItemModel? row)
    {
        if (row is null) return;
        try
        {
            await _services.Trash.RestoreAsync(row.Id, CancellationToken.None).ConfigureAwait(true);
            Notify($"已还原 {row.Name}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            await ConfirmAsync("还原失败", ex.Message, "知道了").ConfigureAwait(true);
        }
        await RefreshTrashAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DeleteTrashAsync(TrashItemModel? row)
    {
        if (row is null) return;
        if (!await ConfirmAsync("永久删除？", $"将永久删除回收站中的「{row.Name}」。", "永久删除", destructive: true).ConfigureAwait(true))
            return;
        try { await _services.Trash.DeletePermanentlyAsync(row.Id, CancellationToken.None).ConfigureAwait(true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            await ConfirmAsync("删除失败", ex.Message, "知道了").ConfigureAwait(true);
        }
        await RefreshTrashAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task EmptyTrashAsync()
    {
        if (!HasTrashItems) return;
        var title = Capabilities.UsesAppTrash ? "清空应用内回收站？" : "清空回收站？";
        var message = $"将永久删除 {TrashSummaryText}。此操作无法撤销。";
        if (!await ConfirmAsync(title, message, "清空回收站", destructive: true).ConfigureAwait(true))
            return;

        try
        {
            await _services.Trash.EmptyAsync(CancellationToken.None).ConfigureAwait(true);
            Notify("回收站已清空");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            await ConfirmAsync("清空失败", ex.Message, "知道了").ConfigureAwait(true);
        }
        await RefreshTrashAsync().ConfigureAwait(true);
    }

    // ---------- 存储洞察（只读） ----------
    [ObservableProperty] private string _storageRootPath = string.Empty;
    [ObservableProperty] private bool _isStorageAnalyzing;
    [ObservableProperty] private bool _isStorageFindingDuplicates;
    [ObservableProperty] private bool _hasStorageAnalysis;
    [ObservableProperty] private bool _hasStorageDuplicateScan;
    [ObservableProperty] private string _storageStatusText = "选择文件夹后开始分析";
    [ObservableProperty] private string _storageDuplicateStatusText = "尚未查找重复文件";
    [ObservableProperty] private long _storageTotalBytes;
    [ObservableProperty] private long _storageFileCount;
    [ObservableProperty] private long _storageFolderCount;
    [ObservableProperty] private bool _storageIsPartial;
    [ObservableProperty] private bool _storageWasTruncated;
    [ObservableProperty] private long _storageDuplicateReclaimableBytes;
    [ObservableProperty] private int _storageDuplicateCandidateCount;
    [ObservableProperty] private int _storageDuplicateFilesSkipped;
    [ObservableProperty] private bool _storageDuplicateIsPartial;
    [ObservableProperty] private bool _storageDuplicateWasTruncated;

    public bool HasStorageRoot => !string.IsNullOrWhiteSpace(StorageRootPath);
    public bool IsStorageBusy => IsStorageAnalyzing || IsStorageFindingDuplicates;
    public bool ShowStorageEmpty => !HasStorageAnalysis && !IsStorageAnalyzing;
    public bool HasStorageKindUsages => StorageKindUsages.Count > 0;
    public bool HasStorageTopFolders => StorageTopFolders.Count > 0;
    public bool HasStorageLargestFiles => StorageLargestFiles.Count > 0;
    public bool HasStorageDuplicateGroups => StorageDuplicateGroups.Count > 0;
    public string StorageRootName
    {
        get
        {
            if (!HasStorageRoot)
                return "未选择";
            var trimmed = StorageRootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var name = Path.GetFileName(trimmed);
            return string.IsNullOrWhiteSpace(name) ? StorageRootPath : name;
        }
    }
    public string StorageTotalSizeText => Formatting.Bytes(StorageTotalBytes);
    public string StorageFileCountText => StorageFileCount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
    public string StorageFolderCountText => StorageFolderCount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
    public string StorageAnalysisStateText =>
        !HasStorageAnalysis ? "未分析" :
        StorageWasTruncated ? "已截断" :
        StorageIsPartial ? "部分结果" : "完整";
    public string StorageDuplicateReclaimableText => Formatting.Bytes(StorageDuplicateReclaimableBytes);
    public string StorageDuplicateStateText =>
        !HasStorageDuplicateScan ? "未查找" :
        StorageDuplicateWasTruncated ? "已截断" :
        StorageDuplicateIsPartial ? "部分结果" : "完整";

    partial void OnStorageRootPathChanged(string value)
    {
        OnPropertyChanged(nameof(HasStorageRoot));
        OnPropertyChanged(nameof(StorageRootName));
        ClearStorageResults(HasStorageRoot ? "等待分析" : "选择文件夹后开始分析");
    }

    partial void OnIsStorageAnalyzingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsStorageBusy));
        OnPropertyChanged(nameof(ShowStorageEmpty));
    }

    partial void OnIsStorageFindingDuplicatesChanged(bool value) => OnPropertyChanged(nameof(IsStorageBusy));
    partial void OnHasStorageAnalysisChanged(bool value) => OnPropertyChanged(nameof(ShowStorageEmpty));

    public bool TrySetStorageRoot(string? path, bool notifyOnFailure = true)
    {
        if (TryNormalizeSearchIndexRoot(path) is not { } normalized || !Directory.Exists(normalized))
        {
            if (notifyOnFailure)
                Notify("存储洞察只能分析本地文件夹");
            return false;
        }

        StorageRootPath = normalized;
        return true;
    }

    [RelayCommand]
    private void UseCurrentFolderForStorage()
    {
        var path = Browser.CurrentPath;
        if (string.IsNullOrWhiteSpace(path) || FileLocator.IsStorageUri(path) || !Directory.Exists(path))
        {
            Notify("当前文件夹不能用于存储洞察");
            return;
        }

        TrySetStorageRoot(path);
    }

    [RelayCommand]
    private async Task AnalyzeStorageAsync()
    {
        if (IsStorageBusy)
            return;
        if (TryNormalizeSearchIndexRoot(StorageRootPath) is not { } root || !Directory.Exists(root))
        {
            StorageStatusText = "只能分析本地文件夹";
            Notify("存储洞察只能分析本地文件夹");
            return;
        }

        StorageRootPath = root;
        ClearStorageResults("正在分析…");
        IsStorageAnalyzing = true;
        StorageAnalysisResult? result = null;
        try
        {
            var options = new StorageAnalysisOptions(IncludeHidden: ShowHidden);
            var outcome = await RunOperationAsync($"正在分析「{StorageRootName}」…", async (progress, ct) =>
            {
                result = await _services.StorageInsights
                    .AnalyzeAsync(root, options, progress, ct).ConfigureAwait(true);
                return new OperationOutcome(
                    result.FileCount > int.MaxValue ? int.MaxValue : (int)result.FileCount,
                    [],
                    []);
            }).ConfigureAwait(true);

            if (outcome.Cancelled)
            {
                StorageStatusText = "分析已取消";
                return;
            }
            if (!outcome.Success || result is null)
            {
                StorageStatusText = "分析未完成";
                return;
            }

            ApplyStorageAnalysis(result);
        }
        finally
        {
            IsStorageAnalyzing = false;
        }
    }

    [RelayCommand]
    private async Task AnalyzeStorageFolderAsync(StorageFolderUsageModel? folder)
    {
        if (folder is not null && TrySetStorageRoot(folder.Path))
            await AnalyzeStorageAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task FindStorageDuplicatesAsync()
    {
        if (IsStorageBusy)
            return;
        if (TryNormalizeSearchIndexRoot(StorageRootPath) is not { } root || !Directory.Exists(root))
        {
            StorageDuplicateStatusText = "只能分析本地文件夹";
            Notify("存储洞察只能分析本地文件夹");
            return;
        }

        StorageRootPath = root;
        ClearStorageDuplicateResults("正在查找重复文件…");
        IsStorageFindingDuplicates = true;
        StorageDuplicateResult? result = null;
        try
        {
            var options = new StorageAnalysisOptions(IncludeHidden: ShowHidden);
            var outcome = await RunOperationAsync("正在查找重复文件…", async (progress, ct) =>
            {
                result = await _services.StorageInsights
                    .FindDuplicatesAsync(root, options, progress, ct).ConfigureAwait(true);
                return new OperationOutcome(result.Groups.Count, [], []);
            }).ConfigureAwait(true);

            if (outcome.Cancelled)
            {
                StorageDuplicateStatusText = "重复查找已取消";
                return;
            }
            if (!outcome.Success || result is null)
            {
                StorageDuplicateStatusText = "重复查找未完成";
                return;
            }

            ApplyStorageDuplicates(result);
        }
        finally
        {
            IsStorageFindingDuplicates = false;
        }
    }

    public async Task OpenStorageItemAsync(FileItemViewModel item)
    {
        if (item.IsFolder)
        {
            await OpenFolderAsync(item.FullPath).ConfigureAwait(true);
            return;
        }

        Page = ShellPage.Browser;
        IsDrawerOpen = false;
        IsPreviewPageOpen = false;
        await Browser.NavigateToFileLocationAsync(item.FullPath).ConfigureAwait(true);
    }

    private void ClearStorageResults(string status)
    {
        StorageKindUsages.Clear();
        StorageTopFolders.Clear();
        StorageLargestFiles.Clear();
        StorageTotalBytes = 0;
        StorageFileCount = 0;
        StorageFolderCount = 0;
        StorageIsPartial = false;
        StorageWasTruncated = false;
        HasStorageAnalysis = false;
        ClearStorageDuplicateResults("尚未查找重复文件");
        StorageStatusText = status;
        NotifyStorageComputed();
    }

    private void ClearStorageDuplicateResults(string status)
    {
        StorageDuplicateGroups.Clear();
        StorageDuplicateReclaimableBytes = 0;
        StorageDuplicateCandidateCount = 0;
        StorageDuplicateFilesSkipped = 0;
        StorageDuplicateIsPartial = false;
        StorageDuplicateWasTruncated = false;
        HasStorageDuplicateScan = false;
        StorageDuplicateStatusText = status;
        OnPropertyChanged(nameof(StorageDuplicateReclaimableText));
        OnPropertyChanged(nameof(StorageDuplicateStateText));
    }

    private void ApplyStorageAnalysis(StorageAnalysisResult result)
    {
        StorageTotalBytes = result.TotalBytes;
        StorageFileCount = result.FileCount;
        StorageFolderCount = result.FolderCount;
        StorageIsPartial = result.IsPartial;
        StorageWasTruncated = result.WasTruncated;

        var denominator = Math.Max(1, result.TotalBytes);
        foreach (var usage in result.KindUsages)
        {
            StorageKindUsages.Add(new StorageKindUsageModel(
                usage.Kind,
                StorageKindName(usage.Kind),
                StorageKindGlyph(usage.Kind),
                usage.TotalBytes,
                usage.FileCount,
                (double)usage.TotalBytes / denominator * 100));
        }

        foreach (var folder in result.TopFolders)
        {
            StorageTopFolders.Add(new StorageFolderUsageModel(
                folder.IsRootFiles ? "根目录文件" : folder.Name,
                folder.Path,
                folder.TotalBytes,
                folder.FileCount,
                folder.FolderCount,
                (double)folder.TotalBytes / denominator * 100,
                folder.IsRootFiles));
        }

        foreach (var file in result.LargestFiles)
        {
            StorageLargestFiles.Add(new FileItemViewModel(file)
            {
                IsFavorite = _services.SettingsStore.IsFavorite(file.FullPath),
            });
        }

        HasStorageAnalysis = true;
        StorageStatusText = $"{StorageFileCountText} 个文件 · {StorageTotalSizeText}" +
                             (result.WasTruncated ? " · 已截断" : result.IsPartial ? " · 部分结果" : string.Empty);
        NotifyStorageComputed();
    }

    private void ApplyStorageDuplicates(StorageDuplicateResult result)
    {
        StorageDuplicateReclaimableBytes = result.ReclaimableBytes;
        StorageDuplicateCandidateCount = result.CandidateFileCount;
        StorageDuplicateFilesSkipped = result.FilesSkipped;
        StorageDuplicateIsPartial = result.IsPartial;
        StorageDuplicateWasTruncated = result.WasTruncated;

        foreach (var group in result.Groups.Take(50))
        {
            var files = group.Files
                .Select(file => new FileItemViewModel(file)
                {
                    IsFavorite = _services.SettingsStore.IsFavorite(file.FullPath),
                })
                .ToArray();
            StorageDuplicateGroups.Add(new StorageDuplicateGroupModel(group.FileSize, group.Sha256, files));
        }

        HasStorageDuplicateScan = true;
        StorageDuplicateStatusText = result.Groups.Count == 0
            ? "未发现完全相同的文件"
            : $"{result.Groups.Count} 组 · {result.CandidateFileCount} 个候选 · 可复核 {StorageDuplicateReclaimableText}" +
              (result.WasTruncated ? " · 已截断" : result.IsPartial ? " · 部分结果" : string.Empty) +
              (result.FilesSkipped > 0 ? $" · 跳过 {result.FilesSkipped}" : string.Empty);
        OnPropertyChanged(nameof(StorageDuplicateReclaimableText));
        OnPropertyChanged(nameof(StorageDuplicateStateText));
    }

    private void NotifyStorageComputed()
    {
        OnPropertyChanged(nameof(StorageTotalSizeText));
        OnPropertyChanged(nameof(StorageFileCountText));
        OnPropertyChanged(nameof(StorageFolderCountText));
        OnPropertyChanged(nameof(StorageAnalysisStateText));
        OnPropertyChanged(nameof(StorageDuplicateReclaimableText));
        OnPropertyChanged(nameof(StorageDuplicateStateText));
    }

    private static string StorageKindName(FileKind kind) => kind switch
    {
        FileKind.Image => "图像",
        FileKind.Video => "视频",
        FileKind.Audio => "音频",
        FileKind.Document => "文档",
        FileKind.Archive => "压缩包",
        FileKind.Code => "代码",
        FileKind.Text => "文本",
        FileKind.Data => "数据",
        FileKind.Font => "字体",
        FileKind.Executable => "程序",
        FileKind.Folder => "文件夹",
        _ => "其他",
    };

    private static string? StorageKindGlyph(FileKind kind) => kind switch
    {
        FileKind.Folder => MdSymbols.Folder,
        FileKind.Image => MdSymbols.Image,
        FileKind.Video => MdSymbols.Movie,
        FileKind.Audio => MdSymbols.MusicNote,
        FileKind.Document => MdSymbols.Description,
        FileKind.Archive => MdSymbols.FolderZip,
        FileKind.Code => MdSymbols.Code,
        FileKind.Data => MdSymbols.DataObject,
        FileKind.Font => MdSymbols.FontDownload,
        FileKind.Executable => MdSymbols.Terminal,
        FileKind.Text => MdSymbols.TextSnippet,
        _ => MdSymbols.InsertDriveFile,
    };

    // ---------- 压缩包工作台（ZIP 家族只读虚拟文件夹） ----------
    private const int MaxArchiveEntries = 10_000;
    private const int MaxVisibleArchiveEntries = 2_000;
    private const int MaxArchivePreviewBytes = 16 * 1024 * 1024;
    private const int MaxArchivePreviewTextBytes = 256 * 1024;

    [ObservableProperty] private string _archivePath = string.Empty;
    [ObservableProperty] private string _archiveName = string.Empty;
    [ObservableProperty] private string _archiveCurrentFolder = string.Empty;
    [ObservableProperty] private string _archiveFilterText = string.Empty;
    [ObservableProperty] private bool _isArchiveBusy;
    [ObservableProperty] private bool _hasArchive;
    [ObservableProperty] private bool _archiveIsTruncated;
    [ObservableProperty] private string _archiveStatusText = "从文件列表打开 ZIP、JAR 或 APK";
    [ObservableProperty] private string _archiveDestinationPath = string.Empty;
    [ObservableProperty] private bool _archivePreserveStructure = true;
    [ObservableProperty] private bool _archiveOpenAfterExtract;
    [ObservableProperty] private string _archiveConflictPolicyName = nameof(ConflictPolicy.KeepBoth);
    [ObservableProperty] private ArchiveWorkbenchEntryViewModel? _selectedArchiveEntry;
    [ObservableProperty] private Bitmap? _archivePreviewImage;
    [ObservableProperty] private string? _archivePreviewText;
    [ObservableProperty] private string _archivePreviewMessage = "选择一个条目查看详情";
    [ObservableProperty] private int _archiveEntryCount;
    [ObservableProperty] private int _archiveFileCount;
    [ObservableProperty] private int _archiveFolderCount;
    [ObservableProperty] private long _archiveUncompressedBytes;
    [ObservableProperty] private long _archiveCompressedBytes;
    [ObservableProperty] private int _archiveSelectedCount;
    [ObservableProperty] private long _archiveSelectedBytes;

    public bool HasArchiveVisibleEntries => ArchiveVisibleEntries.Count > 0;
    public bool ShowArchiveEmpty => HasArchive && !IsArchiveBusy && ArchiveVisibleEntries.Count == 0;
    public bool IsArchiveFiltering => !string.IsNullOrWhiteSpace(ArchiveFilterText);
    public bool CanArchiveGoUp => HasArchive && !IsArchiveFiltering && !string.IsNullOrEmpty(ArchiveCurrentFolder);
    public bool HasSelectedArchiveEntry => SelectedArchiveEntry is not null;
    public bool HasArchivePreviewImage => ArchivePreviewImage is not null;
    public bool HasArchivePreviewText => ArchivePreviewText is not null;
    public bool HasArchiveSelection => ArchiveSelectedCount > 0;
    public string ArchiveUncompressedSizeText => Formatting.Bytes(ArchiveUncompressedBytes);
    public string ArchiveCompressedSizeText => Formatting.Bytes(ArchiveCompressedBytes);
    public string ArchiveCompressionRatioText => ArchiveUncompressedBytes > 0
        ? (ArchiveCompressedBytes * 100.0 / ArchiveUncompressedBytes).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%"
        : "—";
    public string ArchiveCountsText =>
        $"{ArchiveFileCount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} 个文件 · " +
        $"{ArchiveFolderCount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} 个文件夹";
    public string ArchiveSelectedSummaryText => ArchiveSelectedCount == 0
        ? "尚未勾选条目"
        : $"已选 {ArchiveSelectedCount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} 个文件 · {Formatting.Bytes(ArchiveSelectedBytes)}";
    public string ArchiveDestinationName => string.IsNullOrWhiteSpace(ArchiveDestinationPath)
        ? "未选择"
        : Path.GetFileName(ArchiveDestinationPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    public string ArchiveConflictPolicyText => ArchiveConflictPolicyName switch
    {
        nameof(ConflictPolicy.Overwrite) => "覆盖同名文件",
        nameof(ConflictPolicy.Skip) => "跳过同名文件",
        _ => "两者保留（自动改名）",
    };
    public bool IsArchiveKeepBoth => ArchiveConflictPolicyName == nameof(ConflictPolicy.KeepBoth);
    public bool IsArchiveSkipConflicts => ArchiveConflictPolicyName == nameof(ConflictPolicy.Skip);
    public bool IsArchiveOverwriteConflicts => ArchiveConflictPolicyName == nameof(ConflictPolicy.Overwrite);
    public string? SelectedArchiveGlyph => SelectedArchiveEntry?.Glyph;
    public string SelectedArchiveTitle => SelectedArchiveEntry?.Name ?? "压缩包概览";
    public string SelectedArchivePathText => SelectedArchiveEntry?.DisplayPath ?? ArchivePath;
    public string SelectedArchiveOriginalSizeText => SelectedArchiveEntry is { } entry ? Formatting.Bytes(entry.Length) : ArchiveUncompressedSizeText;
    public string SelectedArchiveCompressedSizeText => SelectedArchiveEntry is { } entry ? Formatting.Bytes(entry.CompressedLength) : ArchiveCompressedSizeText;
    public string SelectedArchiveRatioText => SelectedArchiveEntry?.CompressionRatioText ?? ArchiveCompressionRatioText;
    public string SelectedArchiveModifiedText => SelectedArchiveEntry?.FullModifiedText ?? "—";

    partial void OnArchiveNameChanged(string value) => OnPropertyChanged(nameof(PageTitle));

    partial void OnArchiveCurrentFolderChanged(string value)
    {
        OnPropertyChanged(nameof(CanArchiveGoUp));
        BuildArchiveBreadcrumbs();
    }

    partial void OnArchiveFilterTextChanged(string value)
    {
        OnPropertyChanged(nameof(IsArchiveFiltering));
        OnPropertyChanged(nameof(CanArchiveGoUp));
        if (HasArchive)
            RefreshArchiveVisibleEntries();
    }

    partial void OnIsArchiveBusyChanged(bool value) => OnPropertyChanged(nameof(ShowArchiveEmpty));
    partial void OnHasArchiveChanged(bool value) => OnPropertyChanged(nameof(ShowArchiveEmpty));

    partial void OnArchiveDestinationPathChanged(string value) => OnPropertyChanged(nameof(ArchiveDestinationName));

    partial void OnArchiveConflictPolicyNameChanged(string value)
    {
        OnPropertyChanged(nameof(ArchiveConflictPolicyText));
        OnPropertyChanged(nameof(IsArchiveKeepBoth));
        OnPropertyChanged(nameof(IsArchiveSkipConflicts));
        OnPropertyChanged(nameof(IsArchiveOverwriteConflicts));
    }

    partial void OnSelectedArchiveEntryChanged(ArchiveWorkbenchEntryViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedArchiveEntry));
        OnPropertyChanged(nameof(SelectedArchiveGlyph));
        OnPropertyChanged(nameof(SelectedArchiveTitle));
        OnPropertyChanged(nameof(SelectedArchivePathText));
        OnPropertyChanged(nameof(SelectedArchiveOriginalSizeText));
        OnPropertyChanged(nameof(SelectedArchiveCompressedSizeText));
        OnPropertyChanged(nameof(SelectedArchiveRatioText));
        OnPropertyChanged(nameof(SelectedArchiveModifiedText));
    }

    partial void OnArchivePreviewImageChanged(Bitmap? value) => OnPropertyChanged(nameof(HasArchivePreviewImage));
    partial void OnArchivePreviewTextChanged(string? value) => OnPropertyChanged(nameof(HasArchivePreviewText));

    partial void OnArchiveSelectedCountChanged(int value)
    {
        OnPropertyChanged(nameof(ArchiveSelectedSummaryText));
        OnPropertyChanged(nameof(HasArchiveSelection));
    }

    partial void OnArchiveSelectedBytesChanged(long value) => OnPropertyChanged(nameof(ArchiveSelectedSummaryText));

    public async Task OpenArchiveWorkbenchAsync(string path)
    {
        if (FileLocator.IsStorageUri(path))
        {
            Notify("授权文件夹中的压缩包暂不支持工作台");
            return;
        }
        if (!_services.Archive.CanOpen(path))
        {
            Notify("当前仅支持 ZIP、JAR、APK 压缩包");
            return;
        }

        string fullPath;
        try { fullPath = Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            Notify("压缩包路径无效");
            return;
        }
        if (!File.Exists(fullPath))
        {
            Notify("压缩包不存在");
            return;
        }

        var generation = ++_archiveGeneration;
        _archivePreviewGeneration++;
        Page = ShellPage.ArchiveWorkbench;
        IsDrawerOpen = false;
        IsPreviewPageOpen = false;
        ResetArchiveWorkbench(fullPath);
        IsArchiveBusy = true;
        try
        {
            var listed = await _services.Archive
                .ListEntriesAsync(fullPath, MaxArchiveEntries + 1, CancellationToken.None)
                .ConfigureAwait(true);
            if (generation != _archiveGeneration) return;

            ArchiveIsTruncated = listed.Count > MaxArchiveEntries;
            _archiveEntries = listed.Take(MaxArchiveEntries).ToList();
            BuildArchiveTree();
            HasArchive = true;
            UpdateArchiveStats();
            RefreshArchiveVisibleEntries();
            ArchivePreviewMessage = ArchiveFileCount == 0
                ? "这个压缩包里没有文件条目"
                : "选择一个条目查看详情；文本与图片可直接预览";
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            if (generation == _archiveGeneration)
            {
                HasArchive = false;
                ArchiveStatusText = "无法读取这个压缩包";
                ArchivePreviewMessage = $"无法读取：{ex.Message}";
                Notify("无法打开压缩包");
            }
        }
        finally
        {
            if (generation == _archiveGeneration)
                IsArchiveBusy = false;
        }
    }

    [RelayCommand]
    private void CloseArchiveWorkbench()
    {
        _archivePreviewGeneration++;
        Page = ShellPage.Browser;
    }

    public void NavigateArchiveFolder(string? folderPath)
    {
        var normalized = NormalizeArchiveFolderPath(folderPath);
        if (normalized.Length > 0 && !_archiveFolders.ContainsKey(normalized))
            return;

        SelectedArchiveEntry = null;
        ClearArchivePreview("选择一个条目查看详情");
        if (!string.IsNullOrEmpty(ArchiveFilterText))
            ArchiveFilterText = string.Empty; // 先清筛选会在旧目录刷新一次，随后由目标目录刷新覆盖。
        ArchiveCurrentFolder = normalized;
        RefreshArchiveVisibleEntries();
    }

    [RelayCommand]
    private void ArchiveGoUp()
    {
        if (CanArchiveGoUp)
            NavigateArchiveFolder(ArchiveParentFolder(ArchiveCurrentFolder));
    }

    [RelayCommand]
    private void ClearArchiveFilter() => ArchiveFilterText = string.Empty;

    [RelayCommand]
    private void SelectVisibleArchiveEntries()
    {
        foreach (var row in ArchiveVisibleEntries)
            row.IsChecked = true;
        RefreshArchiveSelectionSummary();
    }

    [RelayCommand]
    private void ClearArchiveSelection()
    {
        _selectedArchiveEntries.Clear();
        foreach (var row in ArchiveVisibleEntries)
            row.IsChecked = false;
        RefreshArchiveSelectionSummary();
    }

    [RelayCommand]
    private void SetArchiveConflictPolicy(string? policy)
    {
        if (Enum.TryParse<ConflictPolicy>(policy, out var parsed))
            ArchiveConflictPolicyName = parsed.ToString();
    }

    public async Task SelectArchiveEntryAsync(ArchiveWorkbenchEntryViewModel? row)
    {
        if (row is null) return;
        SelectedArchiveEntry = row;
        var generation = ++_archivePreviewGeneration;
        ClearArchivePreview(row.IsFolder
            ? $"{row.DescendantFileCount:N0} 个后代文件；双击或点箭头进入"
            : "正在读取条目…");
        if (row.IsFolder) return;

        if (row.Length > MaxArchivePreviewBytes)
        {
            ArchivePreviewMessage = $"条目为 {Formatting.Bytes(row.Length)}，超过 {Formatting.Bytes(MaxArchivePreviewBytes)} 的免解压预览上限";
            return;
        }
        if (row.Kind is not (FileKind.Image or FileKind.Text or FileKind.Code or FileKind.Data))
        {
            ArchivePreviewMessage = "此类型在工作台仅显示元数据，可解压后用完整预览打开";
            return;
        }
        if (row.Entry is null)
        {
            ArchivePreviewMessage = "找不到这个条目的原始记录";
            return;
        }

        try
        {
            var bytes = await _services.Archive.ReadEntryBytesAsync(
                ArchivePath, row.Entry.FullName, MaxArchivePreviewBytes, CancellationToken.None).ConfigureAwait(true);
            if (generation != _archivePreviewGeneration) return;
            if (bytes is null)
            {
                ArchivePreviewMessage = "无法在预览上限内读取这个条目";
                return;
            }

            if (row.Kind == FileKind.Image)
            {
                try
                {
                    using var stream = new MemoryStream(bytes);
                    SetArchivePreviewImage(new Bitmap(stream));
                    ArchivePreviewMessage = "免整包解压预览";
                }
                catch (Exception)
                {
                    ArchivePreviewMessage = "这张图片无法解码，可解压后重试";
                }
                return;
            }

            if (ContainsNullByte(bytes))
            {
                ArchivePreviewMessage = "检测到二进制内容，工作台仅显示元数据";
                return;
            }

            var text = DecodeArchivePreviewText(bytes);
            ArchivePreviewText = bytes.Length > MaxArchivePreviewTextBytes
                ? text + "\n\n… 预览已截断"
                : text;
            ArchivePreviewMessage = bytes.Length == 0 ? "空文件" : "免整包解压预览";
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            if (generation == _archivePreviewGeneration)
                ArchivePreviewMessage = $"预览失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ExtractArchiveAllAsync() => await ExtractArchiveAsync(selectedOnly: false).ConfigureAwait(true);

    [RelayCommand]
    private async Task ExtractArchiveSelectedAsync() => await ExtractArchiveAsync(selectedOnly: true).ConfigureAwait(true);

    private async Task ExtractArchiveAsync(bool selectedOnly)
    {
        if (IsArchiveBusy || !HasArchive) return;
        if (ArchiveFileCount == 0)
        {
            Notify("压缩包中没有可解压的文件");
            return;
        }

        HashSet<string>? selectedNames = null;
        if (selectedOnly)
        {
            selectedNames = _archiveEntries
                .Where(entry => !entry.IsFolder && _selectedArchiveEntries.Contains(NormalizeArchiveVirtualPath(entry.FullName)))
                .Select(entry => entry.FullName)
                .ToHashSet(StringComparer.Ordinal);
            if (selectedNames.Count == 0)
            {
                Notify("先勾选要解压的条目");
                return;
            }
        }

        string destination;
        try { destination = Path.GetFullPath(ArchiveDestinationPath); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            Notify("解压目标路径无效");
            return;
        }

        var policy = Enum.TryParse<ConflictPolicy>(ArchiveConflictPolicyName, out var parsed)
            ? parsed
            : ConflictPolicy.KeepBoth;
        var options = new ArchiveExtractOptions(selectedNames, ArchivePreserveStructure, policy);
        var archivePath = ArchivePath;
        var title = selectedOnly
            ? $"正在解压所选 {selectedNames!.Count} 个文件…"
            : $"正在解压「{ArchiveName}」…";

        IsArchiveBusy = true;
        try
        {
            var outcome = await RunOperationAsync(title, (progress, ct) =>
                _services.Archive.ExtractEntriesAsync(archivePath, destination, options, progress, ct)).ConfigureAwait(true);

            if (outcome.Cancelled)
            {
                ArchiveStatusText = "解压已取消；已完成的文件保留在目标文件夹";
            }
            else if (outcome.Success)
            {
                ArchiveStatusText = $"已解压 {outcome.SucceededCount:N0} 项到 {destination}";
            }
            else
            {
                ArchiveStatusText = "解压未全部完成，逐项原因见任务中心";
            }

            await Browser.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);
            if (outcome.Success && ArchiveOpenAfterExtract && Directory.Exists(destination))
                await OpenFolderAsync(destination).ConfigureAwait(true);
        }
        finally
        {
            IsArchiveBusy = false;
        }
    }

    private void ResetArchiveWorkbench(string fullPath)
    {
        HasArchive = false;
        ArchivePath = fullPath;
        ArchiveName = Path.GetFileName(fullPath);
        var parent = Path.GetDirectoryName(fullPath);
        var stem = Path.GetFileNameWithoutExtension(fullPath);
        ArchiveDestinationPath = string.IsNullOrWhiteSpace(parent)
            ? fullPath + "-extracted"
            : Path.Combine(parent, string.IsNullOrWhiteSpace(stem) ? "extracted" : stem);
        ArchiveCurrentFolder = string.Empty;
        ArchiveFilterText = string.Empty;
        ArchiveIsTruncated = false;
        ArchivePreserveStructure = true;
        ArchiveOpenAfterExtract = false;
        ArchiveConflictPolicyName = nameof(ConflictPolicy.KeepBoth);
        SelectedArchiveEntry = null;
        _archiveEntries = [];
        _archiveFolders.Clear();
        _archiveFilesByParent.Clear();
        _archiveChildFolders.Clear();
        _archiveDescendantsByFolder.Clear();
        _archiveExplicitFolders.Clear();
        _selectedArchiveEntries.Clear();
        ArchiveVisibleEntries.Clear();
        ArchiveEntryCount = 0;
        ArchiveFileCount = 0;
        ArchiveFolderCount = 0;
        ArchiveUncompressedBytes = 0;
        ArchiveCompressedBytes = 0;
        ArchiveSelectedCount = 0;
        ArchiveSelectedBytes = 0;
        ArchiveStatusText = "正在读取条目…";
        ClearArchivePreview("正在读取条目…");
        BuildArchiveBreadcrumbs();
        NotifyArchiveStats();
    }

    private void BuildArchiveTree()
    {
        _archiveFolders.Clear();
        _archiveFilesByParent.Clear();
        _archiveChildFolders.Clear();
        _archiveDescendantsByFolder.Clear();
        _archiveExplicitFolders.Clear();
        GetArchiveFolderAggregate(string.Empty);
        _archiveDescendantsByFolder[string.Empty] = [];

        foreach (var entry in _archiveEntries)
        {
            var virtualPath = NormalizeArchiveVirtualPath(entry.FullName);
            if (entry.IsFolder)
            {
                var folderPath = NormalizeArchiveFolderPath(virtualPath);
                EnsureArchiveFolderChain(folderPath);
                _archiveExplicitFolders[folderPath] = entry;
                var aggregate = GetArchiveFolderAggregate(folderPath);
                if (entry.LastWriteTime > aggregate.LastWriteTime)
                    aggregate.LastWriteTime = entry.LastWriteTime;
                continue;
            }

            var parent = ArchiveParentFolder(virtualPath);
            if (parent.Length > 0)
                EnsureArchiveFolderChain(parent);
            if (!_archiveFilesByParent.TryGetValue(parent, out var siblings))
                _archiveFilesByParent[parent] = siblings = [];
            siblings.Add(entry);

            foreach (var ancestor in ArchiveAncestorFolders(parent))
            {
                var aggregate = GetArchiveFolderAggregate(ancestor);
                aggregate.FileCount++;
                aggregate.Length += entry.Length;
                aggregate.CompressedLength += entry.CompressedLength;
                if (entry.LastWriteTime > aggregate.LastWriteTime)
                    aggregate.LastWriteTime = entry.LastWriteTime;
                if (!_archiveDescendantsByFolder.TryGetValue(ancestor, out var descendants))
                    _archiveDescendantsByFolder[ancestor] = descendants = [];
                descendants.Add(virtualPath);
            }
        }
    }

    private void RefreshArchiveVisibleEntries()
    {
        ArchiveVisibleEntries.Clear();
        if (!HasArchive) return;

        var rows = new List<ArchiveWorkbenchEntryViewModel>();
        var query = ArchiveFilterText.Trim();
        int total;
        if (query.Length > 0)
        {
            var matches = _archiveEntries
                .Where(entry => !entry.IsFolder && NormalizeArchiveVirtualPath(entry.FullName)
                    .Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(entry => NormalizeArchiveVirtualPath(entry.FullName), StringComparer.OrdinalIgnoreCase)
                .ToArray();
            total = matches.Length;
            foreach (var entry in matches.Take(MaxVisibleArchiveEntries))
                rows.Add(CreateArchiveFileRow(entry));
        }
        else
        {
            var folderPaths = _archiveChildFolders.TryGetValue(ArchiveCurrentFolder, out var children)
                ? children.OrderBy(path => ArchiveSegmentName(path), StringComparer.CurrentCultureIgnoreCase).ToArray()
                : [];
            var files = _archiveFilesByParent.TryGetValue(ArchiveCurrentFolder, out var directFiles)
                ? directFiles.OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase).ToArray()
                : [];
            total = folderPaths.Length + files.Length;
            foreach (var folderPath in folderPaths)
            {
                if (rows.Count >= MaxVisibleArchiveEntries) break;
                rows.Add(CreateArchiveFolderRow(folderPath));
            }
            foreach (var entry in files)
            {
                if (rows.Count >= MaxVisibleArchiveEntries) break;
                rows.Add(CreateArchiveFileRow(entry));
            }
        }

        foreach (var row in rows)
            ArchiveVisibleEntries.Add(row);

        ArchiveStatusText = IsArchiveFiltering
            ? $"匹配 {total:N0} 个文件"
            : ArchiveCountsText;
        if (rows.Count < total)
            ArchiveStatusText += $" · 显示前 {rows.Count:N0} 项";
        if (ArchiveIsTruncated)
            ArchiveStatusText += $" · 仅载入前 {MaxArchiveEntries:N0} 条";
        OnPropertyChanged(nameof(ShowArchiveEmpty));
    }

    private ArchiveWorkbenchEntryViewModel CreateArchiveFileRow(ArchiveEntryItem entry)
    {
        var virtualPath = NormalizeArchiveVirtualPath(entry.FullName);
        var row = ArchiveWorkbenchEntryViewModel.FromFile(entry, _selectedArchiveEntries.Contains(virtualPath));
        row.CheckedChanged += OnArchiveEntryCheckedChanged;
        return row;
    }

    private ArchiveWorkbenchEntryViewModel CreateArchiveFolderRow(string folderPath)
    {
        var aggregate = GetArchiveFolderAggregate(folderPath);
        var descendants = _archiveDescendantsByFolder.TryGetValue(folderPath, out var names)
            ? names.ToArray()
            : [];
        var allChecked = descendants.Length > 0 && descendants.All(name => _selectedArchiveEntries.Contains(name));
        _archiveExplicitFolders.TryGetValue(folderPath, out var explicitEntry);
        var row = ArchiveWorkbenchEntryViewModel.FromFolder(
            folderPath,
            explicitEntry,
            aggregate.Length,
            aggregate.CompressedLength,
            aggregate.LastWriteTime,
            descendants,
            allChecked);
        row.CheckedChanged += OnArchiveEntryCheckedChanged;
        return row;
    }

    private void OnArchiveEntryCheckedChanged(object? sender, EventArgs e)
    {
        if (sender is not ArchiveWorkbenchEntryViewModel row) return;
        if (row.IsChecked)
        {
            foreach (var name in row.DescendantEntryNames)
                _selectedArchiveEntries.Add(name);
        }
        else
        {
            foreach (var name in row.DescendantEntryNames)
                _selectedArchiveEntries.Remove(name);
        }
        RefreshArchiveSelectionSummary();
    }

    private void RefreshArchiveSelectionSummary()
    {
        var selected = _archiveEntries
            .Where(entry => !entry.IsFolder && _selectedArchiveEntries.Contains(NormalizeArchiveVirtualPath(entry.FullName)))
            .ToArray();
        ArchiveSelectedCount = selected.Length;
        ArchiveSelectedBytes = selected.Sum(entry => entry.Length);
    }

    private void UpdateArchiveStats()
    {
        ArchiveEntryCount = _archiveEntries.Count;
        ArchiveFileCount = _archiveEntries.Count(entry => !entry.IsFolder);
        ArchiveFolderCount = _archiveFolders.Keys.Count(path => path.Length > 0);
        var root = GetArchiveFolderAggregate(string.Empty);
        ArchiveUncompressedBytes = root.Length;
        ArchiveCompressedBytes = root.CompressedLength;
        RefreshArchiveSelectionSummary();
        NotifyArchiveStats();
    }

    private void NotifyArchiveStats()
    {
        OnPropertyChanged(nameof(ArchiveUncompressedSizeText));
        OnPropertyChanged(nameof(ArchiveCompressedSizeText));
        OnPropertyChanged(nameof(ArchiveCompressionRatioText));
        OnPropertyChanged(nameof(ArchiveCountsText));
        OnPropertyChanged(nameof(SelectedArchiveOriginalSizeText));
        OnPropertyChanged(nameof(SelectedArchiveCompressedSizeText));
        OnPropertyChanged(nameof(SelectedArchiveRatioText));
    }

    private void BuildArchiveBreadcrumbs()
    {
        ArchiveBreadcrumbs.Clear();
        ArchiveBreadcrumbs.Add(new ArchiveBreadcrumbModel(
            string.IsNullOrWhiteSpace(ArchiveName) ? "压缩包" : ArchiveName, string.Empty));
        var current = string.Empty;
        foreach (var segment in ArchiveCurrentFolder.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current += segment + "/";
            ArchiveBreadcrumbs.Add(new ArchiveBreadcrumbModel(segment, current));
        }
    }

    private ArchiveFolderAggregate GetArchiveFolderAggregate(string folderPath)
    {
        if (!_archiveFolders.TryGetValue(folderPath, out var aggregate))
            _archiveFolders[folderPath] = aggregate = new ArchiveFolderAggregate();
        return aggregate;
    }

    private void EnsureArchiveFolderChain(string folderPath)
    {
        var normalized = NormalizeArchiveFolderPath(folderPath);
        GetArchiveFolderAggregate(string.Empty);
        if (normalized.Length == 0) return;

        var current = string.Empty;
        foreach (var segment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var child = current + segment + "/";
            GetArchiveFolderAggregate(child);
            if (!_archiveChildFolders.TryGetValue(current, out var children))
                _archiveChildFolders[current] = children = [];
            if (!children.Contains(child))
                children.Add(child);
            current = child;
        }
    }

    private void ClearArchivePreview(string message)
    {
        SetArchivePreviewImage(null);
        ArchivePreviewText = null;
        ArchivePreviewMessage = message;
    }

    private void SetArchivePreviewImage(Bitmap? next)
    {
        var previous = ArchivePreviewImage;
        ArchivePreviewImage = next;
        if (previous is not null && !ReferenceEquals(previous, next))
            previous.Dispose();
    }

    private static bool ContainsNullByte(byte[] bytes)
    {
        var sample = Math.Min(bytes.Length, 8192);
        for (var i = 0; i < sample; i++)
        {
            if (bytes[i] == 0) return true;
        }
        return false;
    }

    private static string DecodeArchivePreviewText(byte[] bytes)
    {
        var length = Math.Min(bytes.Length, MaxArchivePreviewTextBytes);
        if (length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return System.Text.Encoding.UTF8.GetString(bytes, 3, length - 3);
        if (length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return System.Text.Encoding.Unicode.GetString(bytes, 0, length);
        if (length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return System.Text.Encoding.BigEndianUnicode.GetString(bytes, 0, length);
        return System.Text.Encoding.UTF8.GetString(bytes, 0, length);
    }

    private static string NormalizeArchiveVirtualPath(string path) =>
        path.Replace('\\', '/').TrimStart('/');

    private static string NormalizeArchiveFolderPath(string? path)
    {
        var normalized = NormalizeArchiveVirtualPath(path ?? string.Empty).Trim();
        if (normalized.Length == 0) return string.Empty;
        return normalized.EndsWith('/') ? normalized : normalized + "/";
    }

    private static string ArchiveParentFolder(string path)
    {
        var normalized = NormalizeArchiveVirtualPath(path).TrimEnd('/');
        var slash = normalized.LastIndexOf('/');
        return slash >= 0 ? normalized[..(slash + 1)] : string.Empty;
    }

    private static IEnumerable<string> ArchiveAncestorFolders(string parent)
    {
        yield return string.Empty;
        var current = string.Empty;
        foreach (var segment in parent.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current += segment + "/";
            yield return current;
        }
    }

    private static string ArchiveSegmentName(string folderPath)
    {
        var trimmed = folderPath.TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        return slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
    }

    private sealed class ArchiveFolderAggregate
    {
        public int FileCount;
        public long Length;
        public long CompressedLength;
        public DateTimeOffset LastWriteTime;
    }

    // ---------- 搜索中心 / 文件名索引 ----------
    [ObservableProperty] private bool _useSearchIndex = true;
    [ObservableProperty] private string _searchCenterQuery = string.Empty;
    [ObservableProperty] private string _searchCenterKindFilter = "All";
    [ObservableProperty] private bool _isSearchCenterBusy;
    [ObservableProperty] private string _searchCenterStatusText = "输入关键词开始搜索";
    [ObservableProperty] private int _searchIndexEntryCount;
    [ObservableProperty] private long _searchIndexBytes;
    [ObservableProperty] private string _searchIndexUpdatedText = "尚未建立";
    [ObservableProperty] private bool _searchIndexIsCurrent;
    [ObservableProperty] private bool _searchIndexIsPartial;

    public bool HasSearchIndexRoots => SearchIndexRoots.Count > 0;
    public bool HasSearchCenterResults => SearchCenterResults.Count > 0;
    public bool ShowSearchCenterEmpty => !IsSearchCenterBusy && SearchCenterResults.Count == 0;
    public string SearchIndexRootCountText => SearchIndexRoots.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public string SearchIndexEntryCountText => SearchIndexEntryCount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
    public string SearchIndexSizeText => Formatting.Bytes(SearchIndexBytes);
    public string SearchIndexStateText =>
        !UseSearchIndex ? "已暂停" :
        SearchIndexRoots.Count == 0 ? "未配置" :
        SearchIndexIsCurrent ? (SearchIndexIsPartial ? "已更新 · 部分" : "已更新") :
        SearchIndexEntryCount > 0 ? "需重建" : "未建立";

    public bool IsSearchKindAll => SearchCenterKindFilter == "All";
    public bool IsSearchKindFolder => SearchCenterKindFilter == nameof(FileKind.Folder);
    public bool IsSearchKindImage => SearchCenterKindFilter == nameof(FileKind.Image);
    public bool IsSearchKindVideo => SearchCenterKindFilter == nameof(FileKind.Video);
    public bool IsSearchKindAudio => SearchCenterKindFilter == nameof(FileKind.Audio);
    public bool IsSearchKindDocument => SearchCenterKindFilter == nameof(FileKind.Document);
    public bool IsSearchKindArchive => SearchCenterKindFilter == nameof(FileKind.Archive);
    public bool IsSearchKindCode => SearchCenterKindFilter == nameof(FileKind.Code);

    partial void OnUseSearchIndexChanged(bool value)
    {
        _services.SettingsStore.Settings.UseSearchIndex = value;
        OnPropertyChanged(nameof(SearchIndexStateText));
        _ = SaveAsync();
    }

    partial void OnSearchCenterKindFilterChanged(string value)
    {
        foreach (var name in new[]
                 {
                     nameof(IsSearchKindAll), nameof(IsSearchKindFolder), nameof(IsSearchKindImage),
                     nameof(IsSearchKindVideo), nameof(IsSearchKindAudio), nameof(IsSearchKindDocument),
                     nameof(IsSearchKindArchive), nameof(IsSearchKindCode),
                 })
            OnPropertyChanged(name);
    }

    partial void OnIsSearchCenterBusyChanged(bool value) => OnPropertyChanged(nameof(ShowSearchCenterEmpty));

    public async Task RefreshSearchIndexAsync()
    {
        RefreshSearchIndexRoots();
        try
        {
            var stats = await _services.SearchIndex.GetStatsAsync(CancellationToken.None).ConfigureAwait(true);
            SearchIndexEntryCount = stats.EntryCount;
            SearchIndexBytes = stats.IndexBytes;
            SearchIndexIsCurrent = stats.IsCurrent;
            SearchIndexIsPartial = stats.IsPartial;
            SearchIndexUpdatedText = stats.UpdatedAt is { } updated
                ? Formatting.FullDateTime(updated)
                : "尚未建立";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SearchIndexUpdatedText = "不可读取";
        }

        OnPropertyChanged(nameof(SearchIndexEntryCountText));
        OnPropertyChanged(nameof(SearchIndexSizeText));
        OnPropertyChanged(nameof(SearchIndexStateText));
    }

    private void RefreshSearchIndexRoots()
    {
        SearchIndexRoots.Clear();
        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var path in _services.SettingsStore.Settings.SearchIndexRootPaths ?? [])
        {
            if (TryNormalizeSearchIndexRoot(path) is not { } normalized || !seen.Add(normalized))
                continue;
            var trimmed = normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var name = Path.GetFileName(trimmed);
            SearchIndexRoots.Add(new SearchIndexRootModel(
                string.IsNullOrWhiteSpace(name) ? normalized : name, normalized));
        }
    }

    [RelayCommand]
    private void SetSearchCenterKind(string kind) => SearchCenterKindFilter = kind;

    [RelayCommand]
    private async Task RunSearchCenterSearchAsync()
    {
        _searchCenterCts?.Cancel();
        _searchCenterCts?.Dispose();
        var cts = new CancellationTokenSource();
        _searchCenterCts = cts;

        SearchCenterResults.Clear();
        await RefreshSearchIndexAsync().ConfigureAwait(true);
        if (!UseSearchIndex)
        {
            SearchCenterStatusText = "文件名索引已暂停";
            return;
        }
        if (!HasSearchIndexRoots)
        {
            SearchCenterStatusText = "先加入一个索引根";
            return;
        }
        if (!SearchIndexIsCurrent)
        {
            SearchCenterStatusText = "索引需要重建";
            return;
        }

        var query = SearchCenterQuery.Trim();
        var kinds = SearchCenterKinds();
        if (query.Length == 0 && kinds is null)
        {
            SearchCenterStatusText = "输入关键词或选择类型";
            return;
        }

        IsSearchCenterBusy = true;
        SearchCenterStatusText = "正在搜索本地索引…";
        var batch = new List<FileItemViewModel>(50);
        try
        {
            var spec = new SearchSpec(query, string.Empty, kinds, ShowHidden, MaxResults: 5000);
            await foreach (var item in _services.SearchIndex.SearchAsync(spec, cts.Token).ConfigureAwait(true))
            {
                batch.Add(new FileItemViewModel(item)
                {
                    IsFavorite = _services.SettingsStore.IsFavorite(item.FullPath),
                });
                if (batch.Count < 50)
                    continue;
                foreach (var result in batch)
                    SearchCenterResults.Add(result);
                batch.Clear();
            }

            foreach (var result in batch)
                SearchCenterResults.Add(result);
            SearchCenterStatusText = SearchCenterResults.Count == 0
                ? "没有匹配结果"
                : $"{SearchCenterResults.Count} 项 · 本地索引{(SearchIndexIsPartial ? " · 部分" : string.Empty)}";
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            SearchCenterStatusText = "搜索已取消";
        }
        finally
        {
            if (ReferenceEquals(_searchCenterCts, cts))
                IsSearchCenterBusy = false;
        }
    }

    private IReadOnlySet<FileKind>? SearchCenterKinds() => SearchCenterKindFilter switch
    {
        nameof(FileKind.Folder) => new HashSet<FileKind> { FileKind.Folder },
        nameof(FileKind.Image) => new HashSet<FileKind> { FileKind.Image },
        nameof(FileKind.Video) => new HashSet<FileKind> { FileKind.Video },
        nameof(FileKind.Audio) => new HashSet<FileKind> { FileKind.Audio },
        nameof(FileKind.Document) => new HashSet<FileKind> { FileKind.Document },
        nameof(FileKind.Archive) => new HashSet<FileKind> { FileKind.Archive },
        nameof(FileKind.Code) => new HashSet<FileKind> { FileKind.Code },
        _ => null,
    };

    public async Task OpenSearchCenterResultAsync(FileItemViewModel item)
    {
        if (item.IsFolder)
        {
            await OpenFolderAsync(item.FullPath).ConfigureAwait(true);
            return;
        }

        Page = ShellPage.Browser;
        IsDrawerOpen = false;
        IsPreviewPageOpen = false;
        await Browser.NavigateToFileLocationAsync(item.FullPath).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task AddCurrentFolderToSearchIndexAsync()
    {
        var path = Browser.CurrentPath;
        if (string.IsNullOrWhiteSpace(path) || FileLocator.IsStorageUri(path) || !Directory.Exists(path))
        {
            Notify("当前文件夹不能加入本地索引");
            return;
        }

        await AddSearchIndexRootAsync(path).ConfigureAwait(true);
    }

    /// <summary>加入本地索引根；已被父根覆盖时不重复加入，加入父根时收拢其子根。</summary>
    public async Task<bool> AddSearchIndexRootAsync(string path)
    {
        if (TryNormalizeSearchIndexRoot(path) is not { } normalized || !Directory.Exists(normalized))
        {
            Notify("只能加入现有的本地文件夹");
            return false;
        }

        var roots = new List<string>();
        foreach (var existingPath in _services.SettingsStore.Settings.SearchIndexRootPaths ?? [])
        {
            if (TryNormalizeSearchIndexRoot(existingPath) is { } existing &&
                !roots.Contains(existing, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
                roots.Add(existing);
        }

        if (roots.Any(existing => IsSameOrUnderSearchRoot(normalized, existing)))
        {
            Notify("该文件夹已在索引范围内");
            return false;
        }

        roots.RemoveAll(existing => IsSameOrUnderSearchRoot(existing, normalized));
        roots.Add(normalized);
        _services.SettingsStore.Settings.SearchIndexRootPaths = roots;
        await SaveAsync().ConfigureAwait(true);
        await RefreshSearchIndexAsync().ConfigureAwait(true);
        SearchCenterStatusText = "索引根已更新，重建后生效";
        Notify("已加入文件名索引");
        return true;
    }

    [RelayCommand]
    private async Task RemoveSearchIndexRootAsync(SearchIndexRootModel? root)
    {
        if (root is null)
            return;

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        _services.SettingsStore.Settings.SearchIndexRootPaths.RemoveAll(path =>
            TryNormalizeSearchIndexRoot(path) is { } normalized &&
            string.Equals(normalized, root.Path, comparison));
        await SaveAsync().ConfigureAwait(true);
        SearchCenterResults.Clear();
        await RefreshSearchIndexAsync().ConfigureAwait(true);
        SearchCenterStatusText = "索引根已更新，重建后生效";
        Notify($"已移除索引根：{root.Name}");
    }

    [RelayCommand]
    private async Task RebuildSearchIndexAsync()
    {
        if (!UseSearchIndex)
        {
            Notify("文件名索引已暂停");
            return;
        }
        if (!HasSearchIndexRoots)
        {
            Notify("先加入一个索引根");
            return;
        }

        var outcome = await RunOperationAsync("正在重建文件名索引…", async (progress, ct) =>
        {
            var stats = await _services.SearchIndex.RebuildAsync(progress, ct).ConfigureAwait(true);
            return new OperationOutcome(stats.EntryCount, [], []);
        }).ConfigureAwait(true);

        await RefreshSearchIndexAsync().ConfigureAwait(true);
        if (outcome.Success)
            SearchCenterStatusText = $"索引已更新 · {SearchIndexEntryCountText} 项";
    }

    [RelayCommand]
    private async Task ClearSearchIndexAsync()
    {
        if (!await ConfirmAsync(
                "清除文件名索引？",
                "只删除保存在本机应用数据目录中的索引快照，不会删除任何文件。索引根配置会保留。",
                "清除索引",
                destructive: true).ConfigureAwait(true))
            return;

        _searchCenterCts?.Cancel();
        await _services.SearchIndex.ClearAsync(CancellationToken.None).ConfigureAwait(true);
        SearchCenterResults.Clear();
        await RefreshSearchIndexAsync().ConfigureAwait(true);
        SearchCenterStatusText = "索引已清除";
        Notify("已清除文件名索引");
    }

    private static string? TryNormalizeSearchIndexRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || FileLocator.IsStorageUri(path))
            return null;

        try
        {
            if (!Path.IsPathFullyQualified(path))
                return null;
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full) ?? string.Empty;
            while (full.Length > root.Length &&
                   (full[^1] == Path.DirectorySeparatorChar || full[^1] == Path.AltDirectorySeparatorChar))
                full = full[..^1];
            return full;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static bool IsSameOrUnderSearchRoot(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) || root.EndsWith(Path.AltDirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return string.Equals(path, root, comparison) || path.StartsWith(prefix, comparison);
    }

    // ---------- 设置 ----------
    [ObservableProperty] private string _themeMode = "System";
    [ObservableProperty] private string _seedColor = "#006A6A";
    [ObservableProperty] private bool _showHidden;
    [ObservableProperty] private bool _watchFolderChanges = true;
    [ObservableProperty] private bool _foldersFirst = true;
    [ObservableProperty] private bool _showPreviewPane = true;
    [ObservableProperty] private bool _reduceMotion;
    [ObservableProperty] private bool _rememberFolderView;

    public string ThemeModeText => ThemeMode switch { "Light" => "浅色", "Dark" => "深色", _ => "跟随系统" };

    public event EventHandler? ThemeSettingsChanged;

    partial void OnThemeModeChanged(string value)
    {
        OnPropertyChanged(nameof(ThemeModeText));
        _services.SettingsStore.Settings.ThemeMode = value;
        PersistAndApplyTheme();
    }

    partial void OnSeedColorChanged(string value)
    {
        _services.SettingsStore.Settings.SeedColor = value;
        PersistAndApplyTheme();
    }

    partial void OnShowHiddenChanged(bool value)
    {
        _services.SettingsStore.Settings.ShowHidden = value;
        _ = SaveAsync();
        _ = Browser.RefreshCommand.ExecuteAsync(null);
    }

    partial void OnFoldersFirstChanged(bool value)
    {
        _services.SettingsStore.Settings.FoldersFirst = value;
        _ = SaveAsync();
        Browser.RefilterAndSort();
    }

    partial void OnShowPreviewPaneChanged(bool value)
    {
        _services.SettingsStore.Settings.ShowPreviewPane = value;
        _ = SaveAsync();
        RefreshPreviewVisibility();
    }

    partial void OnReduceMotionChanged(bool value)
    {
        _services.SettingsStore.Settings.ReduceMotion = value;
        _ = SaveAsync();
    }

    partial void OnRememberFolderViewChanged(bool value)
    {
        _services.SettingsStore.Settings.RememberFolderView = value;
        Browser.OnFolderViewMemorySettingChanged(value);
    }

    [RelayCommand]
    private async Task ClearFolderViewPreferencesAsync()
    {
        await Browser.ClearFolderViewPreferencesAsync().ConfigureAwait(true);
        Notify("已清除文件夹视图记忆");
    }

    partial void OnWatchFolderChangesChanged(bool value)
    {
        _services.SettingsStore.Settings.WatchFolderChanges = value;
        _ = SaveAsync();
        Browser.SetWatchEnabled(value);
    }

    [RelayCommand]
    private void SetThemeMode(string mode) => ThemeMode = mode;

    [RelayCommand]
    private void SetSeedColor(string seed) => SeedColor = seed;

    private void PersistAndApplyTheme()
    {
        _ = SaveAsync();
        ThemeSettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task SaveAsync()
    {
        try { await _services.SettingsStore.SaveSettingsAsync(CancellationToken.None).ConfigureAwait(true); }
        catch (IOException) { }
    }

    // ---------- IDialogs ----------
    [ObservableProperty] private bool _isDialogOpen;
    [ObservableProperty] private string _dialogTitle = string.Empty;
    [ObservableProperty] private string _dialogMessage = string.Empty;
    [ObservableProperty] private string _dialogInput = string.Empty;
    [ObservableProperty] private string? _dialogInputError;
    [ObservableProperty] private string _dialogConfirmLabel = "确定";
    [ObservableProperty] private bool _dialogDestructive;
    [ObservableProperty] private double _operationPercent;
    [ObservableProperty] private string _operationDetail = string.Empty;

    private DialogKind _dialogKind = DialogKind.None;
    private TaskCompletionSource<object?>? _dialogTcs;
    private Func<string, string?>? _dialogValidate;
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    public bool IsPromptDialog => IsDialogOpen && _dialogKind == DialogKind.Prompt;
    public bool IsProgressDialog => IsDialogOpen && _dialogKind == DialogKind.Progress;
    public bool IsMessageDialog => IsDialogOpen && _dialogKind is DialogKind.Confirm or DialogKind.Message;
    public bool IsConflictDialog => IsDialogOpen && _dialogKind == DialogKind.Conflict;
    public bool IsBatchRenameDialog => IsDialogOpen && _dialogKind == DialogKind.BatchRename;
    public bool ShowDialogCancel => _dialogKind is DialogKind.Prompt or DialogKind.Confirm or DialogKind.Conflict or DialogKind.BatchRename;
    public bool ShowDialogConfirm => _dialogKind is DialogKind.Prompt or DialogKind.Confirm or DialogKind.Message or DialogKind.BatchRename;

    private void NotifyDialogKinds()
    {
        OnPropertyChanged(nameof(IsPromptDialog));
        OnPropertyChanged(nameof(IsProgressDialog));
        OnPropertyChanged(nameof(IsMessageDialog));
        OnPropertyChanged(nameof(IsConflictDialog));
        OnPropertyChanged(nameof(IsBatchRenameDialog));
        OnPropertyChanged(nameof(ShowDialogCancel));
        OnPropertyChanged(nameof(ShowDialogConfirm));
    }

    public async Task<string?> PromptAsync(string title, string? initialText, string confirmLabel, Func<string, string?>? validate = null)
    {
        _dialogKind = DialogKind.Prompt;
        _dialogValidate = validate;
        DialogTitle = title;
        DialogInput = initialText ?? string.Empty;
        DialogInputError = null;
        DialogConfirmLabel = confirmLabel;
        DialogDestructive = false;
        var result = await ShowDialogAsync().ConfigureAwait(true);
        return result as string;
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmLabel, bool destructive = false)
    {
        _dialogKind = DialogKind.Confirm;
        _dialogValidate = null;
        DialogTitle = title;
        DialogMessage = message;
        DialogConfirmLabel = confirmLabel;
        DialogDestructive = destructive;
        var result = await ShowDialogAsync().ConfigureAwait(true);
        return result is true;
    }

    public async Task ShowPropertiesAsync(string title, string body)
    {
        _dialogKind = DialogKind.Message;
        _dialogValidate = null;
        DialogTitle = title;
        DialogMessage = body;
        DialogConfirmLabel = "关闭";
        DialogDestructive = false;
        await ShowDialogAsync().ConfigureAwait(true);
    }

    public async Task<ConflictPolicy?> AskConflictAsync(string fileName)
    {
        _dialogKind = DialogKind.Conflict;
        _dialogValidate = null;
        DialogTitle = "目标中已存在同名项目";
        DialogMessage = $"「{fileName}」在目标文件夹中已存在。选择处理方式（将应用于本批全部冲突项）：";
        DialogDestructive = false;
        var result = await ShowDialogAsync().ConfigureAwait(true);
        return result as ConflictPolicy?;
    }

    // ---------- 批量重命名对话框 ----------
    [ObservableProperty] private string _batchPattern = "{name}_{n}";
    [ObservableProperty] private string _batchFind = string.Empty;
    [ObservableProperty] private string _batchReplace = string.Empty;
    [ObservableProperty] private string _batchStartText = "1";
    [ObservableProperty] private string _batchStepText = "1";
    [ObservableProperty] private string _batchCaseMode = "Keep";
    [ObservableProperty] private bool _batchHasErrors;

    public ObservableCollection<BatchRenameRowModel> BatchRows { get; } = [];
    private IReadOnlyList<string> _batchNames = [];
    private IReadOnlyList<string> _batchSiblings = [];

    public async Task<IReadOnlyList<string>?> BatchRenameAsync(IReadOnlyList<string> names, IReadOnlyList<string> siblingNames)
    {
        _dialogKind = DialogKind.BatchRename;
        _dialogValidate = null;
        _batchNames = names;
        _batchSiblings = siblingNames;
        DialogTitle = $"批量重命名（{names.Count} 项）";
        DialogConfirmLabel = "重命名";
        DialogDestructive = false;
        BatchPattern = "{name}_{n}";
        BatchFind = string.Empty;
        BatchReplace = string.Empty;
        BatchStartText = "1";
        BatchStepText = "1";
        BatchCaseMode = "Keep";
        RecomputeBatchRename();
        var result = await ShowDialogAsync().ConfigureAwait(true);
        return result as IReadOnlyList<string>;
    }

    [RelayCommand]
    private void SetBatchCaseMode(string mode) => BatchCaseMode = mode;

    partial void OnBatchPatternChanged(string value) => RecomputeBatchRename();
    partial void OnBatchFindChanged(string value) => RecomputeBatchRename();
    partial void OnBatchReplaceChanged(string value) => RecomputeBatchRename();
    partial void OnBatchStartTextChanged(string value) => RecomputeBatchRename();
    partial void OnBatchStepTextChanged(string value) => RecomputeBatchRename();
    partial void OnBatchCaseModeChanged(string value) => RecomputeBatchRename();

    private void RecomputeBatchRename()
    {
        if (_batchNames.Count == 0) return;
        var start = int.TryParse(BatchStartText, out var s) ? s : 1;
        var step = int.TryParse(BatchStepText, out var st) ? st : 1;
        var options = new BatchRenameOptions(BatchPattern, BatchFind, BatchReplace, start, step, BatchCaseMode);
        var rows = BatchRenamePlanner.Plan(_batchNames, options, _batchSiblings);
        BatchRows.Clear();
        foreach (var row in rows)
            BatchRows.Add(new BatchRenameRowModel(row.Original, row.Proposed, row.Error));
        BatchHasErrors = rows.Any(r => r.Error is not null);
    }

    private async Task<object?> ShowDialogAsync()
    {
        _dialogTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        IsDialogOpen = true;
        NotifyDialogKinds();
        var result = await _dialogTcs.Task.ConfigureAwait(true);
        IsDialogOpen = false;
        _dialogKind = DialogKind.None;
        NotifyDialogKinds();
        return result;
    }

    [RelayCommand]
    private void DialogConfirm()
    {
        object? result = _dialogKind switch
        {
            DialogKind.Prompt => ValidatePrompt() ? DialogInput : _sentinel,
            DialogKind.Confirm or DialogKind.Message => true,
            DialogKind.BatchRename => BatchHasErrors ? _sentinel : BatchRows.Select(r => r.Proposed).ToArray(),
            _ => null,
        };
        if (ReferenceEquals(result, _sentinel)) return;
        _dialogTcs?.TrySetResult(result);
    }

    partial void OnDialogInputChanged(string value)
    {
        if (_dialogKind == DialogKind.Prompt)
            DialogInputError = _dialogValidate?.Invoke(value);
    }

    private bool ValidatePrompt()
    {
        DialogInputError = _dialogValidate?.Invoke(DialogInput);
        return DialogInputError is null;
    }

    private static readonly object _sentinel = new();

    [RelayCommand]
    private void DialogCancel() =>
        _dialogTcs?.TrySetResult(_dialogKind == DialogKind.Confirm ? false : null);

    [RelayCommand]
    private void ConflictChoice(string policy)
    {
        if (Enum.TryParse<ConflictPolicy>(policy, out var parsed))
            _dialogTcs?.TrySetResult(parsed);
    }

    // ---------- Snackbar ----------
    [ObservableProperty] private bool _isSnackbarVisible;
    [ObservableProperty] private string _snackbarText = string.Empty;
    private int _snackbarGeneration;

    public void Notify(string message)
    {
        SnackbarText = message;
        IsSnackbarVisible = true;
        var generation = ++_snackbarGeneration;
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3.2)).ConfigureAwait(false);
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_snackbarGeneration == generation)
                    IsSnackbarVisible = false;
            });
        });
    }

    [RelayCommand]
    private void DismissSnackbar() => IsSnackbarVisible = false;

    // ---------- 任务中心 ----------
    public async Task<OperationOutcome> RunOperationAsync(
        string title,
        Func<IProgress<OperationProgress>, CancellationToken, Task<OperationOutcome>> operation)
    {
        var task = new OperationTaskViewModel(title);
        task.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(OperationTaskViewModel.Status))
                RefreshTaskCenterSummary();
            if (e.PropertyName is nameof(OperationTaskViewModel.ProgressPercent))
                OperationPercent = task.ProgressPercent;
            if (e.PropertyName is nameof(OperationTaskViewModel.Detail))
                OperationDetail = task.Detail;
        };
        OperationTasks.Insert(0, task);
        TrimOperationHistory();
        OperationPercent = 0;
        OperationDetail = task.Detail;

        var acquired = false;
        OperationOutcome outcome;
        try
        {
            await _operationGate.WaitAsync(task.CancellationToken).ConfigureAwait(true);
            acquired = true;
            task.MarkRunning();
            var progress = new Progress<OperationProgress>(task.Report);
            outcome = await operation(progress, task.CancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            outcome = new OperationOutcome(0, [], [], Cancelled: true);
        }
        catch (Exception ex)
        {
            outcome = new OperationOutcome(0, [new OperationError(title, ex.Message)], []);
        }
        finally
        {
            if (acquired)
                _operationGate.Release();
        }

        task.Complete(outcome);
        RefreshTaskCenterSummary();
        if (outcome.Cancelled)
        {
            Notify("已取消");
        }
        else if (outcome.Errors.Count > 0)
        {
            IsTaskCenterOpen = true;
            Notify("部分操作未完成，详情见任务中心");
        }
        else
        {
            Notify("操作完成");
        }

        return outcome;
    }

    private void TrimOperationHistory()
    {
        const int maxTasks = 20;
        while (OperationTasks.Count > maxTasks)
        {
            var oldestFinished = OperationTasks.LastOrDefault(task => task.IsFinished);
            if (oldestFinished is null)
                return;
            OperationTasks.Remove(oldestFinished);
            oldestFinished.Dispose();
        }
    }
}
