using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using MdExplorer.Application;
using MdExplorer.Domain;
using MdExplorer.UI.ViewModels;

namespace MdExplorer.UI.Views;

public partial class MainView : UserControl
{
    // 应用内拖放专用格式：可承载 storage URI；标准 File 格式同时提供给外部应用。
    private static readonly DataFormat<string> DragPathsFormat =
        DataFormat.CreateStringApplicationFormat("x-mdexplorer-paths");

    private bool _suppressBottomNavEcho;
    private WindowBase? _clipboardWindow;
    private DragCandidate? _dragCandidate;
    private bool _dragInProgress;

    private sealed record DragCandidate(
        PointerPressedEventArgs Trigger,
        IReadOnlyList<FileItemViewModel> Items,
        Point Origin);

    public MainView()
    {
        InitializeComponent();
        // 右键菜单打开前确保命中项被选中（ListBox 默认不处理）
        FileList.AddHandler(PointerPressedEvent, OnListPointerPressed, RoutingStrategies.Tunnel);
        FileGrid.AddHandler(PointerPressedEvent, OnListPointerPressed, RoutingStrategies.Tunnel);
        FileDetails.AddHandler(PointerPressedEvent, OnListPointerPressed, RoutingStrategies.Tunnel);

        // 拖拽源：三种视图共用阈值判定，避免普通点击被误判为拖动。
        foreach (var control in new Control[] { FileList, FileGrid, FileDetails })
        {
            control.AddHandler(PointerPressedEvent, OnDragPointerPressed, RoutingStrategies.Tunnel);
            control.AddHandler(PointerMovedEvent, OnDragPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
            control.AddHandler(PointerReleasedEvent, OnDragPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
            control.AddHandler(PointerCaptureLostEvent, OnDragPointerCaptureLost, RoutingStrategies.Tunnel, handledEventsToo: true);
        }

        // 投放目标：文件区（含条目与空白）与面包屑。
        foreach (var target in new Control[] { FileDropArea, BreadcrumbBar })
        {
            DragDrop.SetAllowDrop(target, true);
            target.AddHandler(DragDrop.DragEnterEvent, OnFileDragOver);
            target.AddHandler(DragDrop.DragOverEvent, OnFileDragOver);
            target.AddHandler(DragDrop.DragLeaveEvent, OnFileDragLeave);
            target.AddHandler(DragDrop.DropEvent, OnFileDrop);
        }
    }

    private ShellViewModel? Shell => DataContext as ShellViewModel;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevel.GetTopLevel(this) is { } topLevel)
        {
            topLevel.AddHandler(TopLevel.BackRequestedEvent, OnBackRequested, RoutingStrategies.Bubble);
            Shell?.UseSystemClipboard(() => TopLevel.GetTopLevel(this));
            if (topLevel is WindowBase window)
            {
                _clipboardWindow = window;
                window.Activated += OnTopLevelActivated;
            }
            // Android 的打开方式走 Avalonia Launcher；桌面沿用进程实现
            if (Shell is { Capabilities.IsAndroid: true } shell)
            {
                shell.UseLauncher(new Services.AvaloniaFileLauncher(() => TopLevel.GetTopLevel(this)));
                // SAF：注入 StorageProvider 并恢复书签授权文件夹
                _ = shell.AttachStorageAsync(topLevel.StorageProvider);
            }
        }
        if (Shell is { } vm)
        {
            vm.PropertyChanged += OnShellPropertyChanged;
            // DataGrid.SelectedItems 为只读 IList（不可绑定整集），选集与 VM 手动双向同步
            vm.Browser.SelectedItems.CollectionChanged += OnBrowserSelectionChanged;
            vm.Browser.PropertyChanged += OnBrowserPropertyChanged;
            _ = vm.Browser.RefreshExternalClipboardAsync();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_clipboardWindow is not null)
        {
            _clipboardWindow.Activated -= OnTopLevelActivated;
            _clipboardWindow = null;
        }
        if (Shell is { } vm)
        {
            vm.PropertyChanged -= OnShellPropertyChanged;
            vm.Browser.SelectedItems.CollectionChanged -= OnBrowserSelectionChanged;
            vm.Browser.PropertyChanged -= OnBrowserPropertyChanged;
        }
        if (TopLevel.GetTopLevel(this) is { } topLevel)
            topLevel.RemoveHandler(TopLevel.BackRequestedEvent, OnBackRequested);
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Shell?.SetViewportSize(e.NewSize.Width, e.NewSize.Height);
    }

    private void OnBackRequested(object? sender, RoutedEventArgs e)
    {
        if (Shell is not { } shell)
            return;
        if (shell.WouldHandleBack)
        {
            e.Handled = true;
            _ = shell.TryGoBackAsync();
        }
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ShellViewModel.Page) || Shell is not { } shell)
            return;
        var index = shell.Page switch
        {
            ShellPage.Home => 0,
            ShellPage.Favorites => 1,
            ShellPage.Trash => 2,
            ShellPage.Settings => 3,
            _ => -1,
        };
        _suppressBottomNavEcho = true;
        BottomNav.SelectedIndex = index;
        _suppressBottomNavEcho = false;

        if (shell.IsDialogOpen)
        {
            // 对话框出现时把焦点给输入框（若有）
        }
    }

    // ---------- 底部导航 ----------
    private void OnBottomNavChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressBottomNavEcho || Shell is not { } shell)
            return;
        switch (BottomNav.SelectedIndex)
        {
            case 0: shell.GoHomeCommand.Execute(null); break;
            case 1: shell.GoFavoritesCommand.Execute(null); break;
            case 2: shell.GoTrashCommand.Execute(null); break;
            case 3: shell.GoSettingsCommand.Execute(null); break;
        }
        // 浏览器页没有对应底栏项：清零选中态
        if (shell.Page == ShellPage.Browser)
        {
            _suppressBottomNavEcho = true;
            BottomNav.SelectedIndex = -1;
            _suppressBottomNavEcho = false;
        }
    }

    // ---------- 列表交互 ----------
    private async void OnListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Properties.IsMiddleButtonPressed && ItemFromEvent(e) is { IsFolder: true } folder && Browser is { } browser)
        {
            await browser.OpenItemInNewTabAsync(folder);
            e.Handled = true;
            return;
        }
        if (!e.Properties.IsRightButtonPressed)
            return;
        if (ItemFromEvent(e) is { } item && Shell is { } shell &&
            !shell.Browser.SelectedItems.Contains(item))
        {
            shell.Browser.SelectedItems.Clear();
            shell.Browser.SelectedItems.Add(item);
        }
    }

    private async void OnTopLevelActivated(object? sender, EventArgs e)
    {
        if (Shell is { } shell)
            await shell.Browser.RefreshExternalClipboardAsync();
    }

    // ---------- 文件拖放 ----------

    private void OnDragPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_dragInProgress || Shell is not { } shell || shell.Browser.IsSelectionMode)
            return;
        if (e.Pointer.Type != PointerType.Mouse)
            return;
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed || point.Properties.IsRightButtonPressed)
            return;
        if (e.Source is Control source && source.FindAncestorOfType<Button>() is not null)
            return;
        if (ItemFromEvent(e) is not { } item)
            return;
        // Tunnel 阶段先拍选集快照：控件在随后的按下处理中可能把多选收敛为单选。
        IReadOnlyList<FileItemViewModel> draggedItems = shell.Browser.SelectedItems.Contains(item)
            ? shell.Browser.SelectedItems.ToList()
            : [item];
        _dragCandidate = new DragCandidate(e, draggedItems, e.GetPosition(this));
    }

    private async void OnDragPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragInProgress || _dragCandidate is not { } candidate)
            return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _dragCandidate = null;
            return;
        }

        var delta = e.GetPosition(this) - candidate.Origin;
        if (Math.Abs(delta.X) < 6 && Math.Abs(delta.Y) < 6)
            return;

        _dragCandidate = null;
        _dragInProgress = true;
        e.Handled = true;
        try
        {
            await StartItemDragAsync(candidate.Trigger, candidate.Items);
        }
        finally
        {
            _dragInProgress = false;
            HideDropHint();
        }
    }

    private void OnDragPointerReleased(object? sender, PointerReleasedEventArgs e) => _dragCandidate = null;

    private void OnDragPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => _dragCandidate = null;

    private async Task StartItemDragAsync(
        PointerPressedEventArgs trigger, IReadOnlyList<FileItemViewModel> draggedItems)
    {
        if (Shell is not { } shell || draggedItems.Count == 0)
            return;

        // 恢复按下前快照，确保拖的是用户看到的多选集合。
        if (!shell.Browser.SelectedItems.SequenceEqual(draggedItems))
        {
            shell.Browser.SelectedItems.Clear();
            foreach (var item in draggedItems)
                shell.Browser.SelectedItems.Add(item);
        }

        var paths = draggedItems.Select(i => i.FullPath).ToList();
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(DragPathsFormat, string.Join('\n', paths)));

        if (TopLevel.GetTopLevel(this) is { } topLevel)
        {
            foreach (var item in draggedItems.Where(i => !i.Model.Locator.IsStorage))
            {
                IStorageItem? storageItem = item.IsFolder
                    ? await topLevel.StorageProvider.TryGetFolderFromPathAsync(item.FullPath).ConfigureAwait(true)
                    : await topLevel.StorageProvider.TryGetFileFromPathAsync(item.FullPath).ConfigureAwait(true);
                if (storageItem is not null)
                    data.Add(DataTransferItem.Create(DataFormat.File, storageItem));
            }
        }

        await DragDrop.DoDragDropAsync(trigger, data, DragDropEffects.Copy | DragDropEffects.Move).ConfigureAwait(true);
    }

    private void OnFileDragOver(object? sender, DragEventArgs e)
    {
        if (Shell is not { } shell || shell.Browser.CurrentPath is null)
        {
            RejectDrop(e);
            return;
        }
        if (!e.DataTransfer.Contains(DragPathsFormat) && !e.DataTransfer.Contains(DataFormat.File))
        {
            RejectDrop(e);
            return;
        }
        if (ResolveDropDestination(e) is not { } destination)
        {
            RejectDrop(e);
            return;
        }

        var sources = ExtractDraggedPaths(e.DataTransfer);
        if (sources.Count == 0 || !shell.Browser.CanTransferItems(sources, destination))
        {
            RejectDrop(e);
            return;
        }

        var isMove = FileTransferRules.ShouldMove(
            e.DataTransfer.Contains(DragPathsFormat),
            (e.KeyModifiers & KeyModifiers.Control) != 0,
            (e.KeyModifiers & KeyModifiers.Shift) != 0);
        e.DragEffects = isMove ? DragDropEffects.Move : DragDropEffects.Copy;
        DropHintText.Text = isMove
            ? $"释放以移动到「{DropDestinationLabel(e)}」"
            : $"释放以复制到「{DropDestinationLabel(e)}」";
        DropHint.IsVisible = true;
        e.Handled = true;
    }

    private void OnFileDragLeave(object? sender, DragEventArgs e) => HideDropHint();

    private async void OnFileDrop(object? sender, DragEventArgs e)
    {
        HideDropHint();
        if (Shell is not { } shell || ResolveDropDestination(e) is not { } destination)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        var sources = ExtractDraggedPaths(e.DataTransfer);
        if (sources.Count == 0 || !shell.Browser.CanTransferItems(sources, destination))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        var isMove = FileTransferRules.ShouldMove(
            e.DataTransfer.Contains(DragPathsFormat),
            (e.KeyModifiers & KeyModifiers.Control) != 0,
            (e.KeyModifiers & KeyModifiers.Shift) != 0);
        e.DragEffects = isMove ? DragDropEffects.Move : DragDropEffects.Copy;
        e.Handled = true;
        await shell.Browser.TransferItemsAsync(sources, destination, isMove);
    }

    private void RejectDrop(DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.None;
        HideDropHint();
        e.Handled = true;
    }

    private void HideDropHint() => DropHint.IsVisible = false;

    private string? ResolveDropDestination(DragEventArgs e)
    {
        if (Shell is not { } shell)
            return null;
        if (ItemFromEvent(e) is { IsFolder: true } folder)
            return folder.FullPath;
        if (BreadcrumbFromEvent(e) is { } segment)
            return segment.FullPath;
        // 搜索结果的条目来自不同父目录：空格区域不猜当前目录，只允许明确投到某个文件夹条目。
        return shell.Browser.IsSearchMode ? null : shell.Browser.CurrentPath;
    }

    private string DropDestinationLabel(DragEventArgs e)
    {
        if (ItemFromEvent(e) is { IsFolder: true } folder)
            return folder.Name;
        if (BreadcrumbFromEvent(e) is { } segment)
            return segment.Name;
        return Shell?.Browser.CurrentFolderName ?? "此处";
    }

    private static BreadcrumbSegment? BreadcrumbFromEvent(RoutedEventArgs e)
    {
        if (e.Source is not Visual source)
            return null;
        foreach (var visual in source.GetSelfAndVisualAncestors())
        {
            if (visual is Control { DataContext: BreadcrumbSegment segment })
                return segment;
        }
        return null;
    }

    private static IReadOnlyList<string> ExtractDraggedPaths(IDataTransfer dataTransfer)
    {
        if (dataTransfer.TryGetValue(DragPathsFormat) is { Length: > 0 } payload)
        {
            // 不用 TrimEntries：合法文件名可以以空格开头或结尾。
            return payload
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(path => path.TrimEnd('\r'))
                .Where(path => path.Length > 0)
                .ToArray();
        }

        var files = dataTransfer.TryGetFiles();
        if (files is null)
            return [];
        return files
            .Select(item => item.TryGetLocalPath())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private async void OnItemTapped(object? sender, TappedEventArgs e)
    {
        if (Shell is not { } shell || ItemFromEvent(e) is not { } item)
            return;
        if (e.Source is Control source && source.FindAncestorOfType<Button>() is not null)
            return; // ⋮ 按钮自己处理
        if (shell.Browser.IsSelectionMode)
        {
            shell.Browser.ToggleChecked(item);
        }
        else if (shell.IsCompactOrMedium)
        {
            if (item.IsFolder)
            {
                await shell.Browser.OpenItemAsync(item);
            }
            else
            {
                shell.Browser.SelectedItems.Clear();
                shell.Browser.SelectedItems.Add(item);
                shell.OnFileOpened(item);
            }
        }
    }

    private async void OnItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (Shell is not { } shell || ItemFromEvent(e) is not { } item)
            return;
        if (item.IsFolder)
        {
            if (!BrowserViewModel.PathsEqual(shell.Browser.CurrentPath, item.FullPath))
                await shell.Browser.OpenItemAsync(item);
        }
        else if (!shell.IsCompactOrMedium)
        {
            shell.Browser.SelectedItems.Clear();
            shell.Browser.SelectedItems.Add(item);
            await shell.Browser.OpenItemExternalAsync(item);
        }
    }

    private async void OnFavoriteItemTapped(object? sender, TappedEventArgs e)
    {
        if (Shell is not { } shell || ItemFromEvent(e) is not { } item)
            return;
        if (item.IsFolder)
            await shell.OpenFolderAsync(item.FullPath);
        else if (Path.GetDirectoryName(item.FullPath) is { } parent)
            await shell.OpenFolderAsync(parent);
    }

    private void OnItemMoreClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control button || Shell is not { } shell)
            return;
        if (button.DataContext is FileItemViewModel item && !shell.Browser.SelectedItems.Contains(item))
        {
            shell.Browser.SelectedItems.Clear();
            shell.Browser.SelectedItems.Add(item);
        }
        if (Resources["ItemMenu"] is ContextMenu menu)
            menu.Open(button);
        e.Handled = true;
    }

    private static FileItemViewModel? ItemFromEvent(RoutedEventArgs e)
    {
        if (e.Source is not Control source)
            return null;
        if (source.DataContext is FileItemViewModel direct)
            return direct;
        return source.FindAncestorOfType<ListBoxItem>()?.DataContext as FileItemViewModel
               ?? source.FindAncestorOfType<DataGridRow>()?.DataContext as FileItemViewModel;
    }

    // ---------- 标签页 ----------
    private void OnTabTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Control source && source.FindAncestorOfType<Button>() is not null)
            return; // 关闭按钮自己处理
        if (sender is Control { DataContext: BrowserTabViewModel tab })
            Browser?.ActivateTabCommand.Execute(tab);
    }

    private void OnTabPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed)
            return;
        if (sender is Control { DataContext: BrowserTabViewModel tab })
        {
            Browser?.CloseTabCommand.Execute(tab);
            e.Handled = true;
        }
    }

    private void OnTabCloseClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: BrowserTabViewModel tab })
            Browser?.CloseTabCommand.Execute(tab);
    }

    // ---------- 面包屑/排序/搜索 ----------
    private async void OnBreadcrumbClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: BreadcrumbSegment segment } && Shell is { } shell)
            await shell.Browser.NavigateBreadcrumbAsync(segment);
    }

    private bool _ascending = true;
    private void OnSortByName(object? sender, RoutedEventArgs e) => SetSort(SortField.Name);
    private void OnSortByDate(object? sender, RoutedEventArgs e) => SetSort(SortField.LastWriteTime);
    private void OnSortBySize(object? sender, RoutedEventArgs e) => SetSort(SortField.Size);
    private void OnSortByKind(object? sender, RoutedEventArgs e) => SetSort(SortField.Kind);

    private void SetSort(SortField field) =>
        Shell?.Browser.SetSort(field, _ascending ? SortDirection.Ascending : SortDirection.Descending);

    private void OnToggleSortDirection(object? sender, RoutedEventArgs e)
    {
        _ascending = !_ascending;
        if (Shell is { } shell)
            shell.Browser.SetSort(Enum.TryParse<SortField>(shell.SettingsStore.Settings.SortField, out var f) ? f : SortField.Name,
                _ascending ? SortDirection.Ascending : SortDirection.Descending);
    }

    // ---------- 详细信息视图选集同步（DataGrid.SelectedItems 只读） ----------
    private bool _syncingDetailsSelection;

    private void OnDetailsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingDetailsSelection || Shell is not { } shell) return;
        _syncingDetailsSelection = true;
        try
        {
            var target = shell.Browser.SelectedItems;
            target.Clear();
            foreach (var item in FileDetails.SelectedItems)
                if (item is FileItemViewModel vm)
                    target.Add(vm);
        }
        finally { _syncingDetailsSelection = false; }
    }

    private void OnBrowserSelectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        MirrorSelectionToDetails();

    private void OnBrowserPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BrowserViewModel.IsDetailsView))
            MirrorSelectionToDetails();
    }

    private void MirrorSelectionToDetails()
    {
        if (_syncingDetailsSelection || Shell is not { } shell || !shell.Browser.IsDetailsView) return;
        _syncingDetailsSelection = true;
        try
        {
            FileDetails.SelectedItems.Clear();
            foreach (var vm in shell.Browser.SelectedItems)
                FileDetails.SelectedItems.Add(vm);
        }
        finally { _syncingDetailsSelection = false; }
    }

    /// <summary>详细信息点列头：排序统一收回 VM 管线（自然序/文件夹在前），控件自排被 Handled 拦截。</summary>
    private void OnDetailsSorting(object? sender, DataGridColumnEventArgs e)
    {
        var field = e.Column.SortMemberPath switch
        {
            "Name" => SortField.Name,
            "Size" => SortField.Size,
            "Kind" => SortField.Kind,
            "LastWriteTime" => SortField.LastWriteTime,
            _ => (SortField?)null,
        };
        if (field is { } f && Shell is { } shell)
            shell.Browser.ApplyColumnSort(f);
        e.Handled = true;
    }

    private async void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Shell is not { } shell)
            return;
        var query = BrowserSearch.Text?.Trim() ?? string.Empty;
        if (query.Length > 0)
            await shell.Browser.SearchRecursiveAsync(query);
    }

    private async void OnRecursiveSearchClick(object? sender, RoutedEventArgs e)
    {
        if (Shell is not { } shell)
            return;
        var query = BrowserSearch.Text?.Trim() ?? string.Empty;
        if (query.Length > 0)
            await shell.Browser.SearchRecursiveAsync(query);
    }

    // ---------- 菜单与工具栏 ----------
    private BrowserViewModel? Browser => Shell?.Browser;

    private async void OnMenuOpen(object? sender, RoutedEventArgs e)
    {
        if (Browser?.SelectedItems.FirstOrDefault() is not { } item || Shell is not { } shell)
            return;
        if (item.IsFolder)
            await Browser.OpenItemAsync(item);
        else
            shell.OnFileOpened(item);
    }

    private async void OnMenuOpenInNewTab(object? sender, RoutedEventArgs e)
    {
        if (Browser is { } browser)
            await browser.OpenItemInNewTabAsync(browser.SelectedItems.FirstOrDefault());
    }

    private async void OnMenuOpenExternal(object? sender, RoutedEventArgs e)
    {
        if (Browser is { } browser)
            await browser.OpenItemExternalAsync(browser.SelectedItems.FirstOrDefault());
    }

    private void OnMenuCopy(object? sender, RoutedEventArgs e) => Browser?.CopySelectionCommand.Execute(null);
    private void OnMenuCut(object? sender, RoutedEventArgs e) => Browser?.CutSelectionCommand.Execute(null);
    private void OnMenuPaste(object? sender, RoutedEventArgs e) => Browser?.PasteCommand.Execute(null);
    private void OnMenuRename(object? sender, RoutedEventArgs e) => Browser?.RenameCommand.Execute(Browser.SelectedItems.FirstOrDefault());
    private void OnToolbarRename(object? sender, RoutedEventArgs e) => Browser?.RenameCommand.Execute(Browser.SelectedItems.FirstOrDefault());
    private void OnMenuFavorite(object? sender, RoutedEventArgs e) => Browser?.ToggleFavoriteCommand.Execute(Browser.SelectedItems.FirstOrDefault());
    private void OnMenuCompress(object? sender, RoutedEventArgs e) => Browser?.CompressSelectionCommand.Execute(null);
    private void OnMenuExtract(object? sender, RoutedEventArgs e) => Browser?.ExtractSelectionCommand.Execute(Browser.SelectedItems.FirstOrDefault());
    private void OnMenuSelectionMode(object? sender, RoutedEventArgs e) => Browser?.EnterSelectionModeCommand.Execute(null);
    private void OnMenuBatchRename(object? sender, RoutedEventArgs e) => Browser?.BatchRenameSelectionCommand.Execute(null);
    private void OnMenuChecksums(object? sender, RoutedEventArgs e) => Browser?.ComputeChecksumsCommand.Execute(Browser.SelectedItems.FirstOrDefault());
    private void OnMenuProperties(object? sender, RoutedEventArgs e) => Browser?.ShowPropertiesCommand.Execute(Browser.SelectedItems.FirstOrDefault());
    private void OnMenuDelete(object? sender, RoutedEventArgs e) => Browser?.DeleteSelectionCommand.Execute(null);

    private void OnScrimTapped(object? sender, TappedEventArgs e)
    {
        if (Shell is { } shell)
            shell.IsDrawerOpen = false;
    }

    private void OnClosePreviewPane(object? sender, RoutedEventArgs e)
    {
        if (Shell is { } shell)
            shell.ShowPreviewPane = false;
    }

    // ---------- 键盘 ----------
    private async void OnRootKeyDown(object? sender, KeyEventArgs e)
    {
        if (Shell is not { } shell)
            return;
        if (e.Source is TextBox)
        {
            if (e.Key == Key.Escape)
                Focus();
            return;
        }

        var ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        switch (e.Key)
        {
            case Key.Escape:
                await shell.TryGoBackAsync();
                e.Handled = true;
                break;
            case Key.Left when (e.KeyModifiers & KeyModifiers.Alt) != 0:
                if (shell.Page == ShellPage.Browser)
                    shell.Browser.GoBackCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Right when (e.KeyModifiers & KeyModifiers.Alt) != 0:
                if (shell.Page == ShellPage.Browser)
                    shell.Browser.GoForwardCommand.Execute(null);
                e.Handled = true;
                break;
        }

        if (shell.Page != ShellPage.Browser)
            return;

        switch (e.Key)
        {
            case Key.Delete:
                shell.Browser.DeleteSelectionCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.F2:
                shell.Browser.RenameCommand.Execute(shell.Browser.SelectedItems.FirstOrDefault());
                e.Handled = true;
                break;
            case Key.F5:
                shell.Browser.RefreshCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.C when ctrl:
                shell.Browser.CopySelectionCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.X when ctrl:
                shell.Browser.CutSelectionCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.V when ctrl:
                shell.Browser.PasteCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.A when ctrl:
                shell.Browser.SelectAllCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.T when ctrl:
                shell.Browser.NewTabCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.W when ctrl:
                shell.Browser.CloseActiveTabCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Tab when ctrl:
                if ((e.KeyModifiers & KeyModifiers.Shift) != 0)
                    shell.Browser.PreviousTabCommand.Execute(null);
                else
                    shell.Browser.NextTabCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Enter:
                if (shell.Browser.SelectedItems.FirstOrDefault() is { } item)
                {
                    if (item.IsFolder)
                        await shell.Browser.OpenItemAsync(item);
                    else
                        await shell.Browser.OpenItemExternalAsync(item);
                    e.Handled = true;
                }
                break;
        }
    }
}
