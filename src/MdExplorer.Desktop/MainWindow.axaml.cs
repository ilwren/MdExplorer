using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;

namespace MdExplorer.Desktop;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private bool _previewOpen = true;
    private bool _isCompact;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainWindowViewModel();
        DataContext = _viewModel;
        _viewModel.PropertyChanged += ViewModelOnPropertyChanged;
        SizeChanged += OnWindowSizeChanged;
        KeyDown += OnWindowKeyDown;
        Opened += (_, _) => ApplyResponsiveLayout(Bounds.Width);
    }

    private void ViewModelOnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsDark) && Application.Current is { } app)
            app.RequestedThemeVariant = _viewModel.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        if (e.PropertyName == nameof(MainWindowViewModel.IsCompactNavigationOpen))
            ApplyResponsiveLayout(Bounds.Width);
    }

    private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs e) => ApplyResponsiveLayout(e.NewSize.Width);

    private void ApplyResponsiveLayout(double width)
    {
        if (MainLayout.ColumnDefinitions.Count < 3) return;
        _isCompact = width < 600;
        var isMedium = width >= 600 && width < 840;
        var isExpanded = width >= 840 && width < 1200;
        var navWidth = _isCompact
            ? (_viewModel.IsCompactNavigationOpen ? 250 : 0)
            : isMedium ? 76 : isExpanded ? 236 : 258;
        var showPreview = width >= 1080 && _previewOpen;

        MainLayout.ColumnDefinitions[0].Width = new GridLength(navWidth);
        MainLayout.ColumnDefinitions[2].Width = showPreview ? new GridLength(isExpanded ? 280 : 306) : new GridLength(0);
        NavigationPanel.IsVisible = !_isCompact || _viewModel.IsCompactNavigationOpen;
        NavExpanded.IsVisible = !isMedium;
        NavRail.IsVisible = isMedium;
        NewFolderExpanded.IsVisible = !isMedium;
        NewFolderRail.IsVisible = isMedium;
        BrowseLabel.IsVisible = !isMedium;
        FooterExpanded.IsVisible = !isMedium;
        FooterRail.IsVisible = isMedium;
        StorageBlock.IsVisible = !isMedium && !_isCompact;
        PreviewPanel.IsVisible = showPreview;
        TabBar.IsVisible = !_isCompact;
        if (RootLayout.RowDefinitions.Count > 1)
            RootLayout.RowDefinitions[1].Height = _isCompact ? new GridLength(0) : new GridLength(44);

        if (BrowserNavigationRow.ColumnDefinitions.Count >= 3)
            BrowserNavigationRow.ColumnDefinitions[2].Width = new GridLength(_isCompact ? 112 : isMedium ? 175 : 250);
        SearchBox.PlaceholderText = _isCompact ? "⌕ 搜索" : "⌕  搜索此文件夹…";
        FilterHelper.IsVisible = !_isCompact;
        if (width >= 840 && _viewModel.IsFloatingPreviewOpen)
            _viewModel.IsFloatingPreviewOpen = false;
        var availableHeight = Math.Max(280, RootLayout.Bounds.Height - 164);
        if (_viewModel.IsFloatingPreviewExpanded)
        {
            FloatingPreviewCard.MaxWidth = Math.Max(320, width - 32);
            FloatingPreviewCard.MaxHeight = availableHeight;
            FloatingPreviewCard.Width = Math.Max(320, width - 32);
            FloatingPreviewCard.Height = availableHeight;
        }
        else
        {
            FloatingPreviewCard.MaxWidth = 760;
            FloatingPreviewCard.MaxHeight = 900;
            FloatingPreviewCard.Width = Math.Max(320, Math.Min(560, width - 32));
            FloatingPreviewCard.Height = Math.Min(680, availableHeight);
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (control && e.Key == Key.L)
        {
            _viewModel.BeginPathEdit();
            PathEditor.Focus();
            PathEditor.SelectAll();
            e.Handled = true;
        }
        else if (control && e.Key == Key.F)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && e.Key == Key.Left)
        {
            _viewModel.NavigateBackCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && e.Key == Key.Right)
        {
            _viewModel.NavigateForwardCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && e.Key == Key.Up)
        {
            _viewModel.NavigateUpCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            _viewModel.RefreshCommand.Execute(null);
            e.Handled = true;
        }
        else if (control && e.Key == Key.D1) { _viewModel.SetView("grid"); e.Handled = true; }
        else if (control && e.Key == Key.D2) { _viewModel.SetView("list"); e.Handled = true; }
        else if (control && e.Key == Key.D3) { _viewModel.SetView("details"); e.Handled = true; }
        else if (e.Key == Key.Escape && _viewModel.IsPathEditing)
        {
            _viewModel.IsPathEditing = false;
            e.Handled = true;
        }
    }

    private void OnPathKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _viewModel.CommitPathEdit();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _viewModel.IsPathEditing = false;
            e.Handled = true;
        }
    }

    private void OnBreadcrumbClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path }) _viewModel.NavigateTo(path);
    }

    private void OnBreadcrumbAreaPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ReferenceEquals(e.Source, BreadcrumbFrame))
        {
            _viewModel.BeginPathEdit();
            PathEditor.Focus();
            PathEditor.SelectAll();
            e.Handled = true;
        }
    }

    private void OnBreadcrumbDropdownClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string path } button) return;
        try
        {
            var menu = new ContextMenu();
            var folders = Directory.Exists(path)
                ? Directory.EnumerateDirectories(path).OrderBy(p => Path.GetFileName(p), StringComparer.CurrentCultureIgnoreCase).ToArray()
                : Array.Empty<string>();
            if (folders.Length == 0)
                menu.Items.Add(new MenuItem { Header = "没有子文件夹", IsEnabled = false });
            else
                foreach (var folder in folders)
                {
                    var item = new MenuItem { Header = Path.GetFileName(folder), Tag = folder };
                    item.Click += (_, _) => _viewModel.NavigateTo(folder);
                    menu.Items.Add(item);
                }
            button.ContextMenu = menu;
            menu.Open(button);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _viewModel.StatusText = $"无法读取子文件夹：{ex.Message}";
        }
    }

    private void OnEntryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: FileEntry entry })
        {
            _viewModel.SelectedEntry = entry;
            if (entry.IsDirectory)
                _viewModel.IsFloatingPreviewOpen = false;
            else if (Bounds.Width < 840)
                _viewModel.IsFloatingPreviewOpen = true;
        }
    }

    private void OnEntryDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: FileEntry entry }) _viewModel.OpenEntry(entry);
    }

    private void OnNavigationClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: NavigationItem item })
            _viewModel.NavigateShortcutCommand.Execute(item);
    }

    private void OnCreateFolderClick(object? sender, RoutedEventArgs e) => _viewModel.CreateFolderCommand.Execute(null);
    private void OnGridViewClick(object? sender, RoutedEventArgs e) => _viewModel.SetView("grid");
    private void OnListViewClick(object? sender, RoutedEventArgs e) => _viewModel.SetView("list");
    private void OnDetailsViewClick(object? sender, RoutedEventArgs e) => _viewModel.SetView("details");
    private void OnClosePreviewClick(object? sender, RoutedEventArgs e) { _previewOpen = false; ApplyResponsiveLayout(Bounds.Width); }
    private void OnFloatCloseClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.IsFloatingPreviewOpen = false;
        _viewModel.IsFloatingPreviewExpanded = false;
    }
    private void OnFloatExpandClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.IsFloatingPreviewExpanded = !_viewModel.IsFloatingPreviewExpanded;
        ApplyResponsiveLayout(Bounds.Width);
    }
    private void OnFloatPreviewTabClick(object? sender, RoutedEventArgs e) => _viewModel.PreviewTab = "预览";
    private void OnFloatPropertiesTabClick(object? sender, RoutedEventArgs e) => _viewModel.PreviewTab = "属性";

    private void OnOpenSelectedClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedEntry is { } entry) _viewModel.OpenEntry(entry);
        else _viewModel.StatusText = "请先选择一个文件或文件夹";
    }

    private void OnShareClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.StatusText = _viewModel.SelectedEntry is null
            ? "请先选择一个文件"
            : "系统分享入口将在平台适配阶段接入";
    }

    private void OnVolumeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path }) _viewModel.NavigateTo(path);
    }

    private void OnTrashClick(object? sender, RoutedEventArgs e) => _viewModel.StatusText = "回收站视图将在文件操作阶段接入";
    private void OnSettingsClick(object? sender, RoutedEventArgs e) => _viewModel.StatusText = "设置页将在下一阶段接入";

    private void OnTabHomeClick(object? sender, RoutedEventArgs e) => NavigateTab("Home");
    private void OnTabPicturesClick(object? sender, RoutedEventArgs e) => NavigateTab("Pictures");
    private void OnTabDocumentsClick(object? sender, RoutedEventArgs e) => NavigateTab("Documents");

    private void NavigateTab(string key)
    {
        var item = _viewModel.NavigationItems.FirstOrDefault(n => n.Key == key);
        if (item is not null) _viewModel.NavigateShortcutCommand.Execute(item);
    }

    private void OnTypeImageClick(object? sender, RoutedEventArgs e) => _viewModel.SetTypeFilter("image");
    private void OnTypeVideoClick(object? sender, RoutedEventArgs e) => _viewModel.SetTypeFilter("video");
    private void OnTypeAudioClick(object? sender, RoutedEventArgs e) => _viewModel.SetTypeFilter("audio");
    private void OnTypeArchiveClick(object? sender, RoutedEventArgs e) => _viewModel.SetTypeFilter("archive");
    private void OnTypeDocumentClick(object? sender, RoutedEventArgs e) => _viewModel.SetTypeFilter("document");
    private void OnTypeAllClick(object? sender, RoutedEventArgs e) => _viewModel.SetTypeFilter("");
}
