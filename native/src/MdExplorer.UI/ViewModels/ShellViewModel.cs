using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MdExplorer.Application.Navigation;
using MdExplorer.Application.Operations;
using MdExplorer.Domain.Files;
using MdExplorer.Domain.Operations;
using MdExplorer.UI.Services;

namespace MdExplorer.UI.ViewModels;

/// 作业进度行（绑定 OperationHandle：Fraction/Items/Completion）。
public sealed partial class JobRow : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _stateText = "排队中";

    public OperationHandle Handle { get; }

    public JobRow(OperationHandle handle)
    {
        Handle = handle;
        Name = handle.Label;
        _ = WatchAsync();
    }

    private async Task WatchAsync()
    {
        while (!Handle.Completion.IsCompleted)
        {
            Progress = Handle.Fraction * 100;
            StateText = Handle.State switch
            {
                OperationState.Running => $"进行中 {Handle.Fraction:P0}",
                _ => "排队中",
            };
            try { await Task.Delay(300); }
            catch { break; }
        }
        Progress = Handle.State == OperationState.Done ? 100 : Handle.Fraction * 100;
        StateText = Handle.State switch
        {
            OperationState.Done => "完成",
            OperationState.Failed => "失败",
            OperationState.Cancelled => "已取消",
            OperationState.Skipped => "已跳过",
            _ => Handle.State.ToString(),
        };
    }

    [RelayCommand]
    private void Cancel() => Handle.Cancellation.Cancel();
}

/// 会话壳 ViewModel：标签页、导航目的地、写操作入口、主题、自适应断点（§4.4）。
public sealed partial class ShellViewModel : ObservableObject
{
    public UiServices Services { get; }

    public ObservableCollection<BrowserViewModel> Tabs { get; } = new();
    public ObservableCollection<JobRow> Jobs { get; } = new();
    public PreviewViewModel Preview { get; }
    public SearchCenterViewModel SearchCenter { get; }
    public SettingsViewModel Settings { get; }
    public HomeViewModel Home { get; }
    public ArchiveViewModel Archive { get; }

    [ObservableProperty] private BrowserViewModel? _activeTab;
    [ObservableProperty] private object? _currentPage;
    [ObservableProperty] private string _breakpoint = "Expanded";
    [ObservableProperty] private string _themeMode = "dark";
    [ObservableProperty] private string _navTitle = "主页";
    [ObservableProperty] private bool _isPreviewVisible;
    [ObservableProperty] private string _snackbarText = "";
    [ObservableProperty] private bool _isSnackbarOpen;

    public ShellViewModel(UiServices services)
    {
        Services = services;
        Preview = new PreviewViewModel(services);
        SearchCenter = new SearchCenterViewModel(services);
        Settings = new SettingsViewModel(services);
        Home = new HomeViewModel(services);
        Archive = new ArchiveViewModel(services) { CurrentContext = this };
        ThemeMode = services.Settings.Current.Theme == "light" ? "light" : "dark";
        Jobs.CollectionChanged += (_, _) => OnPropertyChanged(new PropertyChangedEventArgs(nameof(HasJobs)));
        OpenTab(new Destination.Home());
    }

    // ---------- 标签页（桌面 ≤10） ----------
    [RelayCommand]
    private void NewTab()
    {
        if (Tabs.Count >= 10) return;
        OpenTab(new Destination.Home());
    }

    public void OpenTab(Destination dest)
    {
        var tab = new BrowserViewModel(Services, dest);
        tab.SelectionChanged += (_, _) => OnSelectionChanged(tab);
        tab.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BrowserViewModel.Previewed) && tab == ActiveTab)
                _ = OnPreviewChangedAsync(tab);
        };
        Tabs.Add(tab);
        ActiveTab = tab;
        ShowFiles();
    }

    [RelayCommand]
    private void CloseTab(BrowserViewModel tab)
    {
        Tabs.Remove(tab);
        if (ActiveTab == tab)
            ActiveTab = Tabs.LastOrDefault();
        if (Tabs.Count == 0)
            OpenTab(new Destination.Home());
    }

    partial void OnActiveTabChanged(BrowserViewModel? value)
    {
        if (value is not null)
        {
            NavTitle = value.Title;
            _ = OnPreviewChangedAsync(value);
        }
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(HasActiveTab)));
    }

    public bool HasActiveTab => ActiveTab is not null;

    private void OnSelectionChanged(BrowserViewModel tab)
    {
        if (tab != ActiveTab) return;
        var selected = tab.SelectedItems.ToList();
        if (selected.Count == 1 && selected[0].Kind != FileKind.Folder)
            _ = Preview.ShowAsync(selected[0]);
        else if (selected.Count == 0)
            Preview.Clear();
    }

    private async Task OnPreviewChangedAsync(BrowserViewModel tab)
    {
        if (tab.Previewed is not null)
            await Preview.ShowAsync(tab.Previewed.Item);
        else
            Preview.Clear();
    }

    // ---------- 页面切换 ----------
    public void ShowFiles()
    {
        CurrentPage = ActiveTab;
        if (ActiveTab is not null) NavTitle = ActiveTab.Title;
    }

    [RelayCommand] private void OpenHome() => OpenTab(new Destination.Home());
    [RelayCommand] private void OpenRecent() => NavigateActive(new Destination.Recent());
    [RelayCommand] private void OpenFavorites() => NavigateActive(new Destination.Favorites());
    [RelayCommand] private void OpenTrash() => NavigateActive(new Destination.Trash());
    [RelayCommand] private void OpenCategory(string name) => NavigateActive(new Destination.Category(
        name switch
        {
            "image" => FileKind.Image,
            "video" => FileKind.Video,
            "audio" => FileKind.Audio,
            "document" => FileKind.Document,
            "archive" => FileKind.Archive,
            "code" => FileKind.Code,
            _ => FileKind.Unknown,
        }));

    [RelayCommand]
    private void OpenSearchCenter()
    {
        CurrentPage = SearchCenter;
        NavTitle = "搜索中心";
    }

    [RelayCommand]
    private void OpenInsights()
    {
        var vm = new InsightsViewModel(Services);
        CurrentPage = vm;
        NavTitle = "统计";
        _ = vm.LoadAsync();
    }

    [RelayCommand]
    private void OpenSettings()
    {
        CurrentPage = Settings;
        NavTitle = "设置";
    }

    [RelayCommand]
    private void OpenTasks()
    {
        CurrentPage = new TasksViewModel(Jobs);
        NavTitle = "任务";
    }

    [RelayCommand]
    private void OpenArchiveWorkbench()
    {
        CurrentPage = Archive;
        NavTitle = "压缩包工作台";
    }

    [RelayCommand]
    private void OpenTrashPage()
    {
        var vm = new TrashViewModel(Services);
        CurrentPage = vm;
        NavTitle = "回收站";
        _ = vm.LoadAsync();
    }

    private void NavigateActive(Destination dest)
    {
        if (ActiveTab is null) OpenTab(dest);
        else _ = ActiveTab.NavigateAsync(dest);
        ShowFiles();
    }

    // ---------- 文件操作（全部走 FileOperationQueue；§5.5 UI 永不直接写盘） ----------
    private void EnqueueAndTrack(FileOperationJob job)
    {
        var handle = Services.Queue.Enqueue(job);
        Jobs.Add(new JobRow(handle));
    }

    [RelayCommand]
    private void Copy()
    {
        var tab = ActiveTab;
        if (tab is null) return;
        var items = tab.SelectedItems.ToList();
        if (items.Count == 0) return;
        Services.Clipboard.Set(items.Select(i => i.Locator).ToList(), ClipboardOperation.Copy);
        Services.Notify.ShowTransient($"已复制 {items.Count} 项");
    }

    [RelayCommand]
    private void Cut()
    {
        var tab = ActiveTab;
        if (tab is null) return;
        var items = tab.SelectedItems.ToList();
        if (items.Count == 0) return;
        Services.Clipboard.Set(items.Select(i => i.Locator).ToList(), ClipboardOperation.Cut);
        Services.Notify.ShowTransient($"已剪切 {items.Count} 项");
    }

    [RelayCommand]
    private async Task PasteAsync()
    {
        var tab = ActiveTab;
        if (tab is null) return;
        var op = Services.Clipboard.Operation;
        var sources = Services.Clipboard.Items;
        if (op is null || sources.Count == 0)
        {
            Services.Notify.ShowTransient("剪贴板为空");
            return;
        }
        if (SessionFolder(tab) is not { } dest)
        {
            Services.Notify.ShowTransient("当前位置不可粘贴");
            return;
        }
        var kind = op == ClipboardOperation.Copy ? FileJobKind.Copy : FileJobKind.Move;
        EnqueueAndTrack(new FileOperationJob(kind, sources.ToList(), dest));
        if (op == ClipboardOperation.Cut)
            Services.Clipboard.Clear();
        Services.Notify.ShowTransient("已加入任务队列");
        await tab.RefreshCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task DeleteToTrashAsync()
    {
        var tab = ActiveTab;
        if (tab is null) return;
        var items = tab.SelectedItems.ToList();
        if (items.Count == 0) return;
        if (Services.Settings.Current.ConfirmDelete)
        {
            var ok = await Services.Dialogs.ConfirmAsync("移入回收站",
                $"将 {items.Count} 项移入回收站？", "移入回收站", destructive: true);
            if (!ok) return;
        }
        EnqueueAndTrack(new FileOperationJob(FileJobKind.DeleteToTrash,
            items.Select(i => i.Locator).ToList(), null));
        Services.Notify.ShowTransient("已移入回收站队列");
        await tab.RefreshCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task DeletePermanentlyAsync()
    {
        var tab = ActiveTab;
        if (tab is null) return;
        var items = tab.SelectedItems.ToList();
        if (items.Count == 0) return;
        var ok = await Services.Dialogs.ConfirmAsync("永久删除",
            $"将永久删除 {items.Count} 项，此操作不可恢复！", "永久删除", destructive: true);
        if (!ok) return;
        EnqueueAndTrack(new FileOperationJob(FileJobKind.DeletePermanently,
            items.Select(i => i.Locator).ToList(), null));
        await tab.RefreshCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task RenameAsync()
    {
        var tab = ActiveTab;
        if (tab is null) return;
        var items = tab.SelectedItems.ToList();
        if (items.Count != 1) return;
        var item = items[0];
        var newName = await Services.Dialogs.PromptTextAsync("重命名", item.Name, "输入新名称");
        if (string.IsNullOrWhiteSpace(newName) || newName == item.Name) return;
        EnqueueAndTrack(new FileOperationJob(FileJobKind.Rename,
            [item.Locator], null, NewName: newName));
        await tab.RefreshCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task NewFolderAsync()
    {
        var tab = ActiveTab;
        if (tab is null) return;
        if (SessionFolder(tab) is not { } dest) return;
        var name = await Services.Dialogs.PromptTextAsync("新建文件夹", "新建文件夹", "输入文件夹名称");
        if (string.IsNullOrWhiteSpace(name)) return;
        EnqueueAndTrack(new FileOperationJob(FileJobKind.CreateFolder, [], dest, NewName: name));
        await tab.RefreshCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task ShowPropertiesAsync()
    {
        var tab = ActiveTab;
        if (tab is null) return;
        var items = tab.SelectedItems.ToList();
        if (items.Count != 1) return;
        await Services.Dialogs.ShowPropertiesAsync(items[0]);
    }

    [RelayCommand]
    private async Task OpenEntryAsync(EntryViewModel entry)
    {
        if (ActiveTab is null) return;
        await ActiveTab.OpenAsync(entry);
    }

    private static FileLocator? SessionFolder(BrowserViewModel tab)
        => tab.Session.Current is Destination.Folder f ? f.Locator : null;

    // ---------- 主题与断点 ----------
    [RelayCommand]
    private void ToggleTheme()
    {
        ThemeMode = ThemeMode == "dark" ? "light" : "dark";
    }

    partial void OnThemeModeChanged(string value)
    {
        Services.Settings.Current.Theme = value;
        _ = Services.Settings.SaveAsync(CancellationToken.None);
    }

    public bool IsRailVisible => Breakpoint != "Compact";
    public bool IsBottomNavVisible => Breakpoint == "Compact";
    public bool HasJobs => Jobs.Count > 0;
    public bool IsCompact => Breakpoint == "Compact";

    partial void OnBreakpointChanged(string value)
    {
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsRailVisible)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsBottomNavVisible)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsCompact)));
    }

    partial void OnIsSnackbarOpenChanged(bool value)
    {
        if (value)
            _ = Task.Delay(3200).ContinueWith(_ => Dispatcher.UIThread.Post(() => IsSnackbarOpen = false));
    }

    /// 由 MainWindow 在 SizeChanged 时调用（P3：响应式=布局切换）。
    public void SetWidth(double width)
    {
        Breakpoint = width switch
        {
            < 600 => "Compact",
            < 840 => "Medium",
            _ => "Expanded",
        };
        IsPreviewVisible = width >= 1200;
    }
}
