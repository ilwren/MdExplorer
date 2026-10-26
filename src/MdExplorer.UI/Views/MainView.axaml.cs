using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using MdExplorer.Domain;
using MdExplorer.UI.ViewModels;

namespace MdExplorer.UI.Views;

public partial class MainView : UserControl
{
    private bool _suppressBottomNavEcho;

    public MainView()
    {
        InitializeComponent();
        // 右键菜单打开前确保命中项被选中（ListBox 默认不处理）
        FileList.AddHandler(PointerPressedEvent, OnListPointerPressed, RoutingStrategies.Tunnel);
        FileGrid.AddHandler(PointerPressedEvent, OnListPointerPressed, RoutingStrategies.Tunnel);
    }

    private ShellViewModel? Shell => DataContext as ShellViewModel;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevel.GetTopLevel(this) is { } topLevel)
        {
            topLevel.AddHandler(TopLevel.BackRequestedEvent, OnBackRequested, RoutingStrategies.Bubble);
            // Android 的打开方式走 Avalonia Launcher；桌面沿用进程实现
            if (Shell is { Capabilities.IsAndroid: true } shell)
                shell.UseLauncher(new Services.AvaloniaFileLauncher(() => TopLevel.GetTopLevel(this)));
        }
        if (Shell is { } vm)
            vm.PropertyChanged += OnShellPropertyChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (Shell is { } vm)
            vm.PropertyChanged -= OnShellPropertyChanged;
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
    private void OnListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.Properties.IsRightButtonPressed)
            return;
        if (ItemFromEvent(e) is { } item && Shell is { } shell &&
            !shell.Browser.SelectedItems.Contains(item))
        {
            shell.Browser.SelectedItems.Clear();
            shell.Browser.SelectedItems.Add(item);
        }
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
        return source.FindAncestorOfType<ListBoxItem>()?.DataContext as FileItemViewModel;
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
