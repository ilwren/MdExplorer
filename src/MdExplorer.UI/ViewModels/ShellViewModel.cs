using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Md3.Avalonia.Controls;
using MdExplorer.Domain;
using MdExplorer.Platform.Abstractions;
using MdExplorer.UI.Services;

namespace MdExplorer.UI.ViewModels;

public enum ShellPage { Home, Browser, Favorites, Trash, Settings }
public enum SizeClass { Compact, Medium, Expanded, Large, ExtraLarge }
internal enum DialogKind { None, Prompt, Confirm, Conflict, Message, Progress }

/// <summary>根视图模型：页面切换、断点状态、主页数据、设置、对话框与操作进度（实现 IDialogs）。</summary>
public sealed partial class ShellViewModel : ObservableObject, IDialogs
{
    private readonly AppHost.AppServices _services;
    private Func<IFileLauncherService> _launcher;
    private IFileLauncherService Launcher => _launcher();

    public BrowserViewModel Browser { get; }
    public PreviewViewModel Preview { get; }
    public PlatformCapabilities Capabilities => _services.Capabilities;
    public ISettingsStore SettingsStore => _services.SettingsStore;

    public ShellViewModel(AppHost.AppServices services)
    {
        _services = services;
        _launcher = () => services.Launcher;
        Preview = new PreviewViewModel(services.Archive, () => Launcher);
        Browser = new BrowserViewModel(
            services.FileSystem, services.Operations, services.Trash,
            services.Archive, services.Search, services.SettingsStore,
            this, Preview);
        Browser.FavoritesChanged += async (_, _) => await RefreshFavoritesAsync().ConfigureAwait(true);
        Browser.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(BrowserViewModel.CurrentFolderName))
                OnPropertyChanged(nameof(PageTitle));
            if (e.PropertyName is nameof(BrowserViewModel.IsSearchMode))
                OnPropertyChanged(nameof(PageTitle));
        };
        FavoriteItems.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasFavorites));
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
    }

    /// <summary>Android 挂载后将打开方式切换为 Avalonia Launcher 实现。</summary>
    public void UseLauncher(IFileLauncherService launcher) => _launcher = () => launcher;

    // ---------- 页面与断点 ----------
    [ObservableProperty] private ShellPage _page = ShellPage.Home;
    [ObservableProperty] private SizeClass _sizeClass = SizeClass.Large;
    [ObservableProperty] private bool _isDrawerOpen;
    [ObservableProperty] private bool _isPreviewPageOpen;

    public string DataDirectoryText => _services.DataDirectory;

    public string PageTitle => Page switch
    {
        ShellPage.Home => "文件",
        ShellPage.Favorites => "收藏",
        ShellPage.Trash => "回收站",
        ShellPage.Settings => "设置",
        _ => Browser.IsSearchMode ? "搜索结果" : Browser.CurrentFolderName,
    };

    public bool HasFavorites => FavoriteItems.Count > 0;

    public bool IsHome => Page == ShellPage.Home;
    public bool IsBrowser => Page == ShellPage.Browser;
    public bool IsFavoritesPage => Page == ShellPage.Favorites;
    public bool IsTrashPage => Page == ShellPage.Trash;
    public bool IsSettingsPage => Page == ShellPage.Settings;

    public bool IsCompact => SizeClass == SizeClass.Compact;
    public bool IsMedium => SizeClass == SizeClass.Medium;
    public bool IsExpandedOrAbove => SizeClass >= SizeClass.Expanded;
    public bool IsCompactOrMedium => SizeClass <= SizeClass.Medium;
    public bool ShowBottomNav => IsCompact;
    public bool ShowRail => IsMedium;
    public bool ShowDrawerPane => IsExpandedOrAbove;
    public bool ShowPreviewPaneNow => IsExpandedOrAbove && SettingsStore.Settings.ShowPreviewPane && Preview.HasItem;
    public bool ShowPreviewPaneEffective => IsBrowser && ShowPreviewPaneNow;
    public bool ShowPreviewPageNow => IsPreviewPageOpen && IsCompactOrMedium;

    partial void OnPageChanged(ShellPage value)
    {
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
                     nameof(IsCompact), nameof(IsMedium), nameof(IsExpandedOrAbove), nameof(IsCompactOrMedium),
                     nameof(ShowBottomNav), nameof(ShowRail), nameof(ShowDrawerPane),
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
    public ObservableCollection<TrashItemModel> TrashItems { get; } = [];


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
        await Browser.NavigateToAsync(_services.StartFolder, pushHistory: false).ConfigureAwait(true);
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
        FavoriteItems.Clear();
        foreach (var path in _services.SettingsStore.Favorites)
        {
            var item = await _services.FileSystem.StatAsync(path, CancellationToken.None).ConfigureAwait(true);
            if (item is not null)
                FavoriteItems.Add(new FileItemViewModel(item) { IsFavorite = true });
        }
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

    [RelayCommand]
    private void ToggleDrawer() => IsDrawerOpen = !IsDrawerOpen;

    [RelayCommand]
    private void ClosePreviewPage() => IsPreviewPageOpen = false;

    /// <summary>文件被点开时由视图调用：小断点进预览页，大断点预览窗格随选集变化即可。</summary>
    public void OnFileOpened(FileItemViewModel item)
    {
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
        IsDialogOpen || IsDrawerOpen || IsPreviewPageOpen ||
        Page != ShellPage.Home ||
        Browser.CanConsumeBack;

    /// <summary>顶级返回处理（Android 返回键/桌面 Alt+← 的兜底序）。返回是否已消费。</summary>
    public async Task<bool> TryGoBackAsync()
    {
        if (IsDialogOpen) return true; // 模态开着时吞掉返回，由对话框按钮收尾
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
    [ObservableProperty] private bool _foldersFirst = true;
    [ObservableProperty] private bool _showPreviewPane = true;

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
    private CancellationTokenSource? _operationCts;

    public bool IsPromptDialog => IsDialogOpen && _dialogKind == DialogKind.Prompt;
    public bool IsProgressDialog => IsDialogOpen && _dialogKind == DialogKind.Progress;
    public bool IsMessageDialog => IsDialogOpen && _dialogKind is DialogKind.Confirm or DialogKind.Message;
    public bool IsConflictDialog => IsDialogOpen && _dialogKind == DialogKind.Conflict;
    public bool ShowDialogCancel => _dialogKind is DialogKind.Prompt or DialogKind.Confirm or DialogKind.Conflict;
    public bool ShowDialogConfirm => _dialogKind is DialogKind.Prompt or DialogKind.Confirm or DialogKind.Message;

    private void NotifyDialogKinds()
    {
        OnPropertyChanged(nameof(IsPromptDialog));
        OnPropertyChanged(nameof(IsProgressDialog));
        OnPropertyChanged(nameof(IsMessageDialog));
        OnPropertyChanged(nameof(IsConflictDialog));
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
    private void DialogCancel()
    {
        if (_dialogKind == DialogKind.Progress)
            _operationCts?.Cancel();
        else
            _dialogTcs?.TrySetResult(_dialogKind == DialogKind.Confirm ? false : null);
    }

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

    // ---------- 操作进度 ----------
    public async Task<OperationOutcome> RunOperationAsync(
        string title,
        Func<IProgress<OperationProgress>, CancellationToken, Task<OperationOutcome>> operation)
    {
        _dialogKind = DialogKind.Progress;
        DialogTitle = title;
        OperationPercent = 0;
        OperationDetail = string.Empty;
        IsDialogOpen = true;
        NotifyDialogKinds();
        _operationCts = new CancellationTokenSource();
        var progress = new Progress<OperationProgress>(p =>
        {
            OperationPercent = p.Fraction < 0 ? 0 : p.Fraction * 100;
            OperationDetail = p.ItemsTotal > 0 ? $"{p.CurrentItemName}（{p.ItemsDone}/{p.ItemsTotal}）" : p.CurrentItemName;
        });

        OperationOutcome outcome;
        try
        {
            outcome = await operation(progress, _operationCts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            outcome = new OperationOutcome(0, [], [], Cancelled: true);
        }
        finally
        {
            IsDialogOpen = false;
            _dialogKind = DialogKind.None;
            NotifyDialogKinds();
            _operationCts?.Dispose();
            _operationCts = null;
        }

        if (outcome.Cancelled)
        {
            Notify("已取消");
        }
        else if (outcome.Errors.Count > 0)
        {
            var details = string.Join("\n", outcome.Errors.Take(8).Select(e => $"· {e.ItemName}：{e.Message}"));
            if (outcome.Errors.Count > 8)
                details += $"\n…等共 {outcome.Errors.Count} 项失败";
            await ConfirmAsync("部分操作未完成", details, "知道了").ConfigureAwait(true);
        }
        else
        {
            Notify("操作完成");
        }

        return outcome;
    }
}
