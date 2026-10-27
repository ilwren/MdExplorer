using System.Collections.ObjectModel;
using System.Reflection;
using Avalonia.Controls;
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

public enum ShellPage { Home, Browser, Favorites, Trash, Settings }
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

    public ShellViewModel(AppHost.AppServices services)
    {
        _services = services;
        _launcher = () => services.Launcher;
        Preview = new PreviewViewModel(MdExplorer.UI.Preview.BuiltInPreviewProviders.Create(services.Archive, services.FileSystem), () => Launcher);
        _persistedRoots.AddRange(services.PersistedRoots);
        AuthorizedRoots.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasAuthorizedRoots));
        Browser = new BrowserViewModel(
            services.FileSystem, services.Operations, services.Trash,
            services.Archive, services.Search, services.SettingsStore,
            services.Inspector, services.FolderWatch, services.SystemClipboard,
            this, Preview, () => Launcher);
        Browser.FavoritesChanged += async (_, _) => await RefreshFavoritesAsync().ConfigureAwait(true);
        Browser.RecentsChanged += async (_, _) => await RefreshRecentsAsync().ConfigureAwait(true);
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
        TrashItems.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowTrashEmpty));
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
        ShellPage.Trash => "回收站",
        ShellPage.Settings => "设置",
        _ => Browser.IsSearchMode ? "搜索结果" : Browser.CurrentFolderName,
    };

    public bool HasFavorites => FavoriteItems.Count > 0;
    public bool HasRecentFolders => RecentFolders.Count > 0;
    public bool HasRecentFiles => RecentFiles.Count > 0;
    public bool HasRecents => HasRecentFolders || HasRecentFiles;
    /// <summary>可浏览平台的回收站为空时展示占位（Windows/Android 走平台说明分支）。</summary>
    public bool ShowTrashEmpty => Capabilities.CanBrowseTrash && TrashItems.Count == 0;

    public bool IsHome => Page == ShellPage.Home;
    public bool IsBrowser => Page == ShellPage.Browser;
    public bool IsFavoritesPage => Page == ShellPage.Favorites;
    public bool IsTrashPage => Page == ShellPage.Trash;
    public bool IsSettingsPage => Page == ShellPage.Settings;

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
        foreach (var root in _persistedRoots)
        {
            try
            {
                if (await provider.OpenFolderBookmarkAsync(root.Bookmark).ConfigureAwait(true) is { } folder)
                    AddAuthorizedRootModel(folder.Name, _services.StorageFileSystem.RegisterRoot(folder));
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException)
            {
                // 书签/授权失效则跳过（条目保留在持久化中，用户可重新授权覆盖）
            }
        }
    }

    [RelayCommand]
    private async Task PickAuthorizedFolderAsync()
    {
        var provider = _services.StorageFileSystem.StorageProvider;
        if (provider is null || !provider.CanPickFolder)
        {
            Notify("当前环境不支持文件夹选择");
            return;
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
            return;
        }
        string? firstUri = null;
        foreach (var folder in picked)
        {
            var uri = _services.StorageFileSystem.RegisterRoot(folder);
            firstUri ??= uri;
            AddAuthorizedRootModel(folder.Name, uri);
            try
            {
                // 书签持久化尽力而为：失败时该授权仅本次会话有效
                if (folder.CanBookmark && await folder.SaveBookmarkAsync().ConfigureAwait(true) is { } bookmark)
                {
                    _persistedRoots.RemoveAll(r => r.Name == folder.Name);
                    _persistedRoots.Add(new AuthorizedRoot(folder.Name, bookmark));
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException) { }
        }
        if (picked.Count > 0)
        {
            await SaveRootsAsync().ConfigureAwait(true);
            if (firstUri is not null)
                await OpenFolderAsync(firstUri).ConfigureAwait(true);
        }
    }

    /// <summary>移除授权入口与书签（不触碰实际文件；系统侧授权亦可在系统设置撤销）。</summary>
    public async Task RemoveAuthorizedRootAsync(AuthorizedRootModel root)
    {
        AuthorizedRoots.Remove(root);
        _persistedRoots.RemoveAll(r => r.Name == root.Name);
        await SaveRootsAsync().ConfigureAwait(true);
        Notify($"已移除授权：{root.Name}");
    }

    /// <summary>父文件夹定位串（双形态统一，供收藏跳转等跨界调用）。</summary>
    public string? BrowserParentOf(string path) => _services.FileSystem.GetParentPath(path);

    private void AddAuthorizedRootModel(string name, string uri)
    {
        if (AuthorizedRoots.All(r => r.Path != uri))
            AuthorizedRoots.Add(new AuthorizedRootModel(name, uri));
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
        if (Page == ShellPage.Browser && await Browser.TryGoBackAsync().ConfigureAwait(true)) return true;
        if (Page != ShellPage.Home) { GoHome(); return true; }
        return false;
    }

    // ---------- 回收站页 ----------
    private async Task RefreshTrashAsync()
    {
        TrashItems.Clear();
        if (!Capabilities.CanBrowseTrash) return;
        var items = await _services.Trash.ListAsync(CancellationToken.None).ConfigureAwait(true);
        foreach (var item in items)
            TrashItems.Add(new TrashItemModel(item.Id, item.OriginalName, item.OriginalPath,
                item.DeletedAt == default ? "" : Formatting.DateTime(item.DeletedAt, DateTimeOffset.Now),
                item.IsFolder));
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ConfirmAsync("删除失败", ex.Message, "知道了").ConfigureAwait(true);
        }
        await RefreshTrashAsync().ConfigureAwait(true);
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
