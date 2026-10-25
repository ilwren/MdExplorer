using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Md3.Avalonia.Controls;
using Md3.Avalonia.Themes.Dynamic;
using Avalonia.VisualTree;
using MdExplorer.Desktop;

namespace MdExplorer.Desktop.Views;

public sealed class MainWindow : Window
{
    private readonly Grid _workspace = new();
    private readonly StackPanel _fileList = new() { Spacing = 2 };
    private readonly StackPanel _secondList = new() { Spacing = 2 };
    private readonly StackPanel _preview = new() { Spacing = 10 };
    private Avalonia.Media.Imaging.Bitmap? _currentPreviewBitmap;
    private readonly TextBlock _pathLabel = new();
    private readonly TextBlock _countLabel = new();
    private readonly MdTextBox _search = new() { PlaceholderText = "搜索此文件夹", Variant = MdTextBoxVariant.Outlined, ShowClearButton = true };
    private Button? _themeButton;
    private IReadOnlyDictionary<string, Color> _themeRoles = new Dictionary<string, Color>();
    private MdThemeMode _themeMode = MdThemeMode.System;
    private readonly HashSet<string> _selectedPaths = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private List<FileSystemInfo> _visibleEntries = new();
    private string? _selectionAnchor;
    private bool _toggleSelectionOnClick;
    private bool _rangeSelectionOnClick;
    private List<string> _clipboardPaths = new();
    private bool _cutPending;
    private Button? _copyButton;
    private Button? _cutButton;
    private Button? _pasteButton;
    private Button? _sortButton;
    private Button? _sortDirectionButton;
    private Button? _hiddenButton;
    private Button? _backButton;
    private Button? _forwardButton;
    private readonly Stack<string> _backHistory = new();
    private readonly Stack<string> _forwardHistory = new();
    private Button? _searchScopeButton;
    private SortMode _sortMode = SortMode.Name;
    private bool _sortDescending;
    private bool _showHidden;
    private bool _searchSubfolders;
    private Border? _operationPanel;
    private MdLinearProgressIndicator? _operationProgress;
    private TextBlock? _operationText;
    private CancellationTokenSource? _operationCancellation;
    private readonly TextBlock _status = new();
    private readonly string _home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private string _currentPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private string _secondPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    private FileSystemInfo? _selected;
    private LayoutMode _mode = LayoutMode.Preview;
    private CancellationTokenSource? _loadCancellation;

    private enum LayoutMode { Preview, SideBySide, Stacked }
    private enum SortMode { Name, Modified, Size, Type }

    public MainWindow()
    {
        Title = "MdExplorer · 本地文件";
        Width = 1450;
        Height = 900;
        MinWidth = 760;
        MinHeight = 540;
        KeyDown += OnKeyDown;
        Closing += (_, _) => _operationCancellation?.Cancel();
        _themeMode = MdThemeManager.Current.ThemeMode;
        _themeRoles = MdThemeManager.Apply(Application.Current!, MdThemeManager.Current, Application.Current!.ActualThemeVariant == ThemeVariant.Dark);
        Background = Brush("#F7F6F9");
        Application.Current.ActualThemeVariantChanged += (_, _) =>
        {
            _themeRoles = MdThemeManager.Apply(Application.Current, MdThemeManager.Current, Application.Current.ActualThemeVariant == ThemeVariant.Dark);
            RefreshThemeColors();
        };
        BuildShell();
        RefreshThemeColors();
        _ = LoadDirectoryAsync(_currentPath);
    }

    private void BuildShell()
    {
        var root = new DockPanel();
        Content = root;
        var header = BuildHeader();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        var statusBar = BuildStatusBar();
        DockPanel.SetDock(statusBar, Dock.Bottom);
        root.Children.Add(statusBar);
        _operationPanel = BuildOperationPanel();
        DockPanel.SetDock(_operationPanel, Dock.Bottom);
        root.Children.Add(_operationPanel);
        _workspace.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(205)));
        _workspace.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        _workspace.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(340)));
        var sidebar = BuildSidebar();
        Grid.SetColumn(sidebar, 0);
        _workspace.Children.Add(sidebar);
        var main = BuildMainPane();
        Grid.SetColumn(main, 1);
        _workspace.Children.Add(main);
        var details = BuildDetailsPane();
        Grid.SetColumn(details, 2);
        _workspace.Children.Add(details);
        root.Children.Add(_workspace);
    }

    private Control BuildHeader()
    {
        var bar = new Grid { Height = 62, Background = Brush("#FAF9FB"), ColumnSpacing = 14 };
        bar.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(185)));
        bar.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(92)));
        bar.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        bar.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(290)));
        bar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0) };
        brand.Children.Add(new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(9), Background = Brush("#6750A4"), Child = new TextBlock { Text = "M", Foreground = Brushes.White, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
        brand.Children.Add(new TextBlock { Text = "MdExplorer", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        AddAt(bar, brand, 0);
        var nav = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _backButton = ActionButton("‹", NavigateBack, "后退");
        _forwardButton = ActionButton("›", NavigateForward, "前进");
        _backButton.IsEnabled = false; _forwardButton.IsEnabled = false;
        nav.Children.Add(_backButton);
        nav.Children.Add(_forwardButton);
        AddAt(bar, nav, 1);
        var pathRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        pathRow.Children.Add(new TextBlock { Text = "⌂", FontSize = 17, VerticalAlignment = VerticalAlignment.Center });
        _pathLabel.Text = _currentPath;
        _pathLabel.TextTrimming = TextTrimming.CharacterEllipsis;
        _pathLabel.VerticalAlignment = VerticalAlignment.Center;
        pathRow.Children.Add(_pathLabel);
        AddAt(bar, pathRow, 2);
        _search.PlaceholderText = "搜索此文件夹";
        _search.Height = 36;
        _search.VerticalAlignment = VerticalAlignment.Center;
        _search.Margin = new Thickness(0, 0, 0, 0);
        _search.TextChanged += (_, _) => _ = LoadDirectoryAsync(_currentPath);
        AddAt(bar, _search, 3);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 14, 0) };
        tools.Children.Add(ActionButton("＋ 文件夹", CreateFolder, "新建文件夹"));
        tools.Children.Add(ActionButton("＋ 文本", CreateTextFile, "新建空白文本文件"));
        _searchScopeButton = ActionButton(SearchScopeLabel(), ToggleSearchScope, "切换搜索范围：当前目录 / 包含子目录");
        tools.Children.Add(_searchScopeButton);
        tools.Children.Add(ActionButton("⇄", CycleLayout, "切换布局"));
        _themeButton = ActionButton(ThemeLabel(), CycleTheme, "切换浅色 / 深色 / 跟随系统主题");
        tools.Children.Add(_themeButton);
        AddAt(bar, tools, 4);
        return new Border { BorderBrush = Brush("#EAE7EE"), BorderThickness = new Thickness(0, 0, 0, 1), Child = bar };
    }

    private Control BuildSidebar()
    {
        var panel = new DockPanel { Background = Brush("#FAF9FB"), LastChildFill = true };
        var footer = new TextBlock { Text = "●  本地文件 · 用户目录", FontSize = 11, Foreground = Brush("#77737F"), Margin = new Thickness(18, 14) };
        DockPanel.SetDock(footer, Dock.Bottom);
        panel.Children.Add(footer);
        var links = new StackPanel { Margin = new Thickness(12, 22) };
        links.Children.Add(Section("工作区"));
        links.Children.Add(SideButton("▣　文件", () => Navigate(_home), true));
        links.Children.Add(SideButton("◷　最近", () => SetStatus("最近访问列表将在后续版本提供")));
        links.Children.Add(SideButton("★　收藏", () => SetStatus("收藏夹将在后续版本提供")));
        links.Children.Add(Section("位置"));
        links.Children.Add(SideButton("▣　主目录", () => Navigate(_home)));
        links.Children.Add(SideButton("▱　文档", () => Navigate(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments))));
        links.Children.Add(SideButton("▱　下载", () => Navigate(Path.Combine(_home, "Downloads"))));
        links.Children.Add(SideButton("▱　图片", () => Navigate(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures))));
        panel.Children.Add(links);
        return new Border { BorderBrush = Brush("#EAE7EE"), BorderThickness = new Thickness(0, 0, 1, 0), Child = panel };
    }

    private Control BuildMainPane()
    {
        var pane = new DockPanel { Margin = new Thickness(24, 20, 20, 0) };
        var heading = new Grid { Margin = new Thickness(2, 0, 2, 16) };
        heading.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        heading.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        var labels = new StackPanel { Spacing = 4 };
        labels.Children.Add(new TextBlock { Text = "文件", FontSize = 21, FontWeight = FontWeight.SemiBold });
        _countLabel.Text = "加载中…";
        _countLabel.FontSize = 12;
        _countLabel.Foreground = Brush("#77737F");
        labels.Children.Add(_countLabel);
        AddAt(heading, labels, 0);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        _sortButton = ActionButton(SortLabel(), CycleSort, "循环切换排序字段");
        _sortDirectionButton = ActionButton(SortDirectionLabel(), ToggleSortDirection, "升序 / 降序");
        _hiddenButton = ActionButton(HiddenLabel(), ToggleHidden, "显示或隐藏隐藏文件");
        actions.Children.Add(_sortButton);
        actions.Children.Add(_sortDirectionButton);
        actions.Children.Add(_hiddenButton);
        actions.Children.Add(ActionButton("☷", () => SetStatus("当前为列表视图")));
        _copyButton = ActionButton("复制", () => SetClipboard(cut: false), "复制所选项目");
        _cutButton = ActionButton("剪切", () => SetClipboard(cut: true), "剪切所选项目");
        _pasteButton = ActionButton("粘贴", PasteClipboard, "粘贴到当前文件夹");
        _copyButton.IsEnabled = _selectedPaths.Count > 0;
        _cutButton.IsEnabled = _selectedPaths.Count > 0;
        _pasteButton.IsEnabled = _clipboardPaths.Count > 0;
        actions.Children.Add(_copyButton);
        actions.Children.Add(_cutButton);
        actions.Children.Add(_pasteButton);
        AddAt(heading, actions, 1);
        DockPanel.SetDock(heading, Dock.Top);
        pane.Children.Add(heading);
        var columns = new Grid { Height = 32 };
        columns.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        columns.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(120)));
        columns.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(90)));
        columns.Children.Add(new TextBlock { Text = "名称", FontSize = 11, Foreground = Brush("#85808B"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0) });
        var date = new TextBlock { Text = "修改日期", FontSize = 11, Foreground = Brush("#85808B"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(date, 1); columns.Children.Add(date);
        var size = new TextBlock { Text = "大小", FontSize = 11, Foreground = Brush("#85808B"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 12, 0) };
        Grid.SetColumn(size, 2); columns.Children.Add(size);
        DockPanel.SetDock(columns, Dock.Top);
        pane.Children.Add(columns);
        var scroll = new ScrollViewer { Content = _fileList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        pane.Children.Add(scroll);
        return pane;
    }

    private Control BuildDetailsPane()
    {
        var pane = new DockPanel { Background = Brush("#FCFBFD"), Margin = new Thickness(0) };
        var title = new TextBlock { Text = "预览与文件信息", FontSize = 15, FontWeight = FontWeight.SemiBold, Margin = new Thickness(20, 20, 20, 12) };
        DockPanel.SetDock(title, Dock.Top);
        pane.Children.Add(title);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _preview };
        pane.Children.Add(scroll);
        return new Border { BorderBrush = Brush("#EAE7EE"), BorderThickness = new Thickness(1, 0, 0, 0), Child = pane };
    }

    private Control BuildStatusBar()
    {
        var bar = new Grid { Height = 34, Background = Brush("#FAF9FB"), ColumnSpacing = 10 };
        bar.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        bar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        bar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        _status.Text = "就绪";
        _status.FontSize = 11;
        _status.Foreground = Brush("#77737F");
        _status.VerticalAlignment = VerticalAlignment.Center;
        _status.Margin = new Thickness(16, 0);
        AddAt(bar, _status, 0);
        AddAt(bar, new TextBlock { Text = "桌面 · 本地文件", FontSize = 11, Foreground = Brush("#77737F"), VerticalAlignment = VerticalAlignment.Center }, 1);
        AddAt(bar, ActionButton(LayoutLabel(), CycleLayout, "切换布局"), 2);
        return new Border { BorderBrush = Brush("#EAE7EE"), BorderThickness = new Thickness(0, 1, 0, 0), Child = bar };
    }

    private Border BuildOperationPanel()
    {
        var row = new Grid { ColumnSpacing = 14, RowSpacing = 4, Margin = new Thickness(16, 5) };
        row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        row.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        row.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        _operationText = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        AddAt(row, _operationText, 0);
        _operationProgress = new MdLinearProgressIndicator { Minimum = 0, Maximum = 1, Value = 0, Height = 5, IsIndeterminate = false };
        Grid.SetRow(_operationProgress, 1); Grid.SetColumnSpan(_operationProgress, 2); row.Children.Add(_operationProgress);
        var cancel = ActionButton("取消", () => _operationCancellation?.Cancel());
        Grid.SetColumn(cancel, 1); row.Children.Add(cancel);
        var panel = new Border { Background = Brush("#FAF9FB"), BorderBrush = Brush("#EAE7EE"), BorderThickness = new Thickness(0, 1, 0, 0), IsVisible = false, Child = row };
        _operationPanel = panel;
        return panel;
    }

    private (CancellationTokenSource Source, IProgress<OperationProgress> Progress) StartOperation(string title, int total)
    {
        _operationCancellation = new CancellationTokenSource();
        _operationPanel!.IsVisible = true;
        _operationText!.Text = title;
        _operationProgress!.Maximum = Math.Max(1, total);
        _operationProgress.Value = 0;
        _operationProgress.IsIndeterminate = total <= 0;
        var progress = new Progress<OperationProgress>(value =>
        {
            if (_operationCancellation is null) return;
            _operationText.Text = value.Message;
            _operationProgress.Maximum = Math.Max(1, value.Total);
            _operationProgress.Value = value.Completed;
        });
        return (_operationCancellation, progress);
    }

    private void StopOperation()
    {
        _operationPanel!.IsVisible = false;
        _operationCancellation?.Dispose();
        _operationCancellation = null;
    }

    private sealed record OperationProgress(int Completed, int Total, string Message);

    private async Task LoadDirectoryAsync(string path)
    {
        if (!PathEquals(path, _currentPath))
        {
            _selectedPaths.Clear(); _selectionAnchor = null; _selected = null; ClearPreview();
            if (_copyButton is not null) _copyButton.IsEnabled = false;
            if (_cutButton is not null) _cutButton.IsEnabled = false;
        }
        _loadCancellation?.Cancel();
        var cts = _loadCancellation = new CancellationTokenSource();
        var query = _search.Text?.Trim() ?? "";
        var showHidden = _showHidden;
        var searchSubfolders = _searchSubfolders;
        var sortMode = _sortMode;
        var sortDescending = _sortDescending;
        try
        {
            var entries = await Task.Run(() =>
            {
                List<FileSystemInfo> all;
                if (searchSubfolders && !string.IsNullOrWhiteSpace(query))
                    all = SearchSubtree(path, query, showHidden, cts.Token);
                else
                    all = Directory.EnumerateFileSystemEntries(path)
                        .Select(p => (FileSystemInfo)(Directory.Exists(p) ? new DirectoryInfo(p) : new FileInfo(p)))
                        .Where(x => showHidden || !IsHidden(x))
                        .Where(x => string.IsNullOrEmpty(query) || x.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                        .Take(1000).ToList();
                return SortEntries(all, sortMode, sortDescending);
            }, cts.Token);
            if (cts.IsCancellationRequested) return;
            _currentPath = path;
            _pathLabel.Text = path;
            _countLabel.Text = string.IsNullOrWhiteSpace(query)
                ? $"{entries.Count} 个项目 · {SortLabel()}"
                : $"搜索结果 {entries.Count} 项 · {(searchSubfolders ? "包含子目录" : "当前目录")}";
            _visibleEntries = entries;
            _selectedPaths.RemoveWhere(path => !_visibleEntries.Any(entry => PathEquals(entry.FullName, path)));
            _selected = _selectedPaths.Count == 0 ? null : _visibleEntries.LastOrDefault(entry => _selectedPaths.Contains(entry.FullName));
            if (_selected is null)
            {
                ClearPreview();
                if (_copyButton is not null) _copyButton.IsEnabled = false;
                if (_cutButton is not null) _cutButton.IsEnabled = false;
            }
            _fileList.Children.Clear();
            foreach (var entry in entries) _fileList.Children.Add(FileRow(entry));
            SetStatus($"{path}　·　{entries.Count} 个项目");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { SetStatus($"无法读取目录：{ex.Message}"); }
    }

    private static List<FileSystemInfo> SearchSubtree(string root, string query, bool showHidden, CancellationToken cancellationToken)
    {
        var found = new List<FileSystemInfo>();
        var pending = new Stack<(string Path, int Depth)>();
        var visited = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        pending.Push((root, 0));
        while (pending.Count > 0 && found.Count < 1000)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (directory, depth) = pending.Pop();
            string canonical;
            try { canonical = Path.GetFullPath(directory); }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { continue; }
            if (!visited.Add(canonical)) continue;
            try
            {
                foreach (var child in Directory.EnumerateFileSystemEntries(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var attributes = File.GetAttributes(child);
                        if (!showHidden && ((attributes & FileAttributes.Hidden) != 0 || Path.GetFileName(child).StartsWith(".", StringComparison.Ordinal))) continue;
                        var isDirectory = (attributes & FileAttributes.Directory) != 0;
                        FileSystemInfo item = isDirectory ? new DirectoryInfo(child) : new FileInfo(child);
                        if (item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) found.Add(item);
                        if (isDirectory && depth < 31 && (attributes & FileAttributes.ReparsePoint) == 0) pending.Push((child, depth + 1));
                        if (found.Count >= 1000) break;
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return found;
    }

    private static List<FileSystemInfo> SortEntries(List<FileSystemInfo> entries, SortMode mode, bool descending)
    {
        static IOrderedEnumerable<FileSystemInfo> SortGroup(IEnumerable<FileSystemInfo> group, SortMode current, bool reverse) => current switch
        {
            SortMode.Modified => reverse ? group.OrderByDescending(x => x.LastWriteTimeUtc).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase) : group.OrderBy(x => x.LastWriteTimeUtc).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase),
            SortMode.Size => reverse ? group.OrderByDescending(x => x is FileInfo f ? f.Length : -1L).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase) : group.OrderBy(x => x is FileInfo f ? f.Length : -1L).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase),
            SortMode.Type => reverse ? group.OrderByDescending(x => x is DirectoryInfo ? "" : x.Extension, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase) : group.OrderBy(x => x is DirectoryInfo ? "" : x.Extension, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => reverse ? group.OrderByDescending(x => x.Name, StringComparer.CurrentCultureIgnoreCase) : group.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
        };
        var folders = entries.Where(x => x is DirectoryInfo);
        var files = entries.Where(x => x is not DirectoryInfo);
        return SortGroup(folders, mode, descending).Concat(SortGroup(files, mode, descending)).ToList();
    }

    private static bool IsHidden(FileSystemInfo entry)
    {
        if (entry.Name.StartsWith(".", StringComparison.Ordinal)) return true;
        try { return (File.GetAttributes(entry.FullName) & FileAttributes.Hidden) != 0; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private string SortLabel() => _sortMode switch { SortMode.Name => "名称排序", SortMode.Modified => "修改时间", SortMode.Size => "大小排序", _ => "类型排序" };
    private string SortDirectionLabel() => _sortDescending ? "↓" : "↑";
    private void ToggleSortDirection()
    {
        _sortDescending = !_sortDescending;
        if (_sortDirectionButton is not null) _sortDirectionButton.Content = SortDirectionLabel();
        _ = LoadDirectoryAsync(_currentPath);
    }
    private string SearchScopeLabel() => _searchSubfolders ? "搜索：含子目录" : "搜索：当前目录";
    private void ToggleSearchScope()
    {
        _searchSubfolders = !_searchSubfolders;
        if (_searchScopeButton is not null) _searchScopeButton.Content = SearchScopeLabel();
        _ = LoadDirectoryAsync(_currentPath);
    }
    private string HiddenLabel() => _showHidden ? "隐藏项：开" : "隐藏项：关";

    private void CycleSort()
    {
        _sortMode = _sortMode switch { SortMode.Name => SortMode.Modified, SortMode.Modified => SortMode.Size, SortMode.Size => SortMode.Type, _ => SortMode.Name };
        if (_sortButton is not null) _sortButton.Content = SortLabel();
        _ = LoadDirectoryAsync(_currentPath);
    }

    private void ToggleHidden()
    {
        _showHidden = !_showHidden;
        if (_hiddenButton is not null) _hiddenButton.Content = HiddenLabel();
        _ = LoadDirectoryAsync(_currentPath);
    }

    private Control FileRow(FileSystemInfo entry)
    {
        var isDirectory = entry is DirectoryInfo;
        var grid = new Grid { Height = 46, ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(120)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(90)));
        var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0) };
        name.Children.Add(new Border { Width = 25, Height = 25, CornerRadius = new CornerRadius(6), Background = Brush(isDirectory ? "#F6E2A2" : "#E8DEF8"), Child = new TextBlock { Text = isDirectory ? "▰" : IconFor(entry.Extension), FontSize = 10, Foreground = Brush("#6750A4"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
        name.Children.Add(new TextBlock { Text = entry.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        grid.Children.Add(name);
        var modified = new TextBlock { Text = entry.LastWriteTime.ToString("g"), FontSize = 11, Foreground = Brush("#77737F"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(modified, 1); grid.Children.Add(modified);
        var sizeText = isDirectory ? "文件夹" : FormatSize(((FileInfo)entry).Length);
        var size = new TextBlock { Text = sizeText, FontSize = 11, Foreground = Brush("#77737F"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 12, 0) };
        Grid.SetColumn(size, 2); grid.Children.Add(size);
        var button = new MdButton { Content = grid, Variant = _selectedPaths.Contains(entry.FullName) ? MdButtonVariant.Tonal : MdButtonVariant.Text, Size = MdButtonSize.Small, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch };
        button.PointerPressed += (_, e) =>
        {
            _toggleSelectionOnClick = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
            _rangeSelectionOnClick = !_toggleSelectionOnClick && e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        };
        button.Click += (_, _) =>
        {
            SelectEntry(entry, _toggleSelectionOnClick, _rangeSelectionOnClick);
            _toggleSelectionOnClick = false; _rangeSelectionOnClick = false;
        };
        button.DoubleTapped += (_, _) => { if (isDirectory) Navigate(entry.FullName); else OpenEntry(entry); };
        return button;
    }

    private void SelectEntry(FileSystemInfo entry, bool toggle = false, bool range = false)
    {
        if (range && _selectionAnchor is not null)
        {
            var anchor = _visibleEntries.FindIndex(x => PathEquals(x.FullName, _selectionAnchor));
            var current = _visibleEntries.FindIndex(x => PathEquals(x.FullName, entry.FullName));
            if (anchor >= 0 && current >= 0)
            {
                _selectedPaths.Clear();
                for (var i = Math.Min(anchor, current); i <= Math.Max(anchor, current); i++) _selectedPaths.Add(_visibleEntries[i].FullName);
            }
            else { _selectedPaths.Clear(); _selectedPaths.Add(entry.FullName); _selectionAnchor = entry.FullName; }
        }
        else if (toggle)
        {
            if (!_selectedPaths.Add(entry.FullName)) _selectedPaths.Remove(entry.FullName);
            if (_selectionAnchor is null || !_selectedPaths.Contains(_selectionAnchor)) _selectionAnchor = entry.FullName;
        }
        else
        {
            _selectedPaths.Clear(); _selectedPaths.Add(entry.FullName); _selectionAnchor = entry.FullName;
        }
        _selected = _selectedPaths.Contains(entry.FullName) ? entry : _visibleEntries.LastOrDefault(x => _selectedPaths.Contains(x.FullName));
        if (_copyButton is not null) _copyButton.IsEnabled = _selectedPaths.Count > 0;
        if (_cutButton is not null) _cutButton.IsEnabled = _selectedPaths.Count > 0;
        if (_selected is null)
        {
            ClearPreview();
            SetStatus("未选择项目");
            _fileList.Children.Clear(); foreach (var item in _visibleEntries) _fileList.Children.Add(FileRow(item));
            return;
        }
        entry = _selected;
        SetStatus(_selectedPaths.Count > 1 ? $"已选择 {_selectedPaths.Count} 项" : entry.Name);
        ClearPreview();
        _preview.Margin = new Thickness(20, 4, 20, 20);
        var ext = entry.Extension.ToLowerInvariant();
        _preview.Children.Add(new TextBlock { Text = entry.Name, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        _preview.Children.Add(new TextBlock { Text = entry is DirectoryInfo ? "文件夹" : $"{(string.IsNullOrEmpty(ext) ? "文件" : ext.TrimStart('.').ToUpperInvariant())} 文件 · {FormatSize(((FileInfo)entry).Length)}", FontSize = 11, Foreground = Brush("#77737F") });
        var previewBox = new MdCard { MinHeight = 190, Padding = new Thickness(18), Variant = MdCardVariant.Outlined, IsInteractive = false };
        if (entry is DirectoryInfo)
            previewBox.Content = new TextBlock { Text = "文件夹\n\n双击打开此位置", TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        else if (new[] { ".txt", ".md", ".log", ".csv", ".json", ".xml" }.Contains(ext))
            previewBox.Content = new TextBlock { Text = ReadTextPreview(entry.FullName), TextWrapping = TextWrapping.Wrap, FontSize = 12, MaxHeight = 260 };
        else if (new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" }.Contains(ext))
        {
            try
            {
                _currentPreviewBitmap = new Avalonia.Media.Imaging.Bitmap(entry.FullName);
                previewBox.Content = new Image { Source = _currentPreviewBitmap, Stretch = Stretch.Uniform, MaxHeight = 250 };
            }
            catch { previewBox.Content = new TextBlock { Text = "无法解码此图片" }; }
        }
        else previewBox.Content = new TextBlock { Text = "暂无内置预览\n可使用系统应用打开。", TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        _preview.Children.Add(previewBox);
        _preview.Children.Add(new TextBlock { Text = "文件信息", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 10, 0, 0) });
        AddInfo("类型", entry is DirectoryInfo ? "文件夹" : (string.IsNullOrEmpty(ext) ? "文件" : ext.TrimStart('.').ToUpperInvariant()));
        AddInfo("创建时间", entry.CreationTime.ToString("yyyy-MM-dd HH:mm"));
        AddInfo("修改时间", entry.LastWriteTime.ToString("yyyy-MM-dd HH:mm"));
        AddInfo("属性", entry.Attributes.ToString());
        if (_currentPreviewBitmap is { } image) AddInfo("图像尺寸", $"{image.PixelSize.Width} × {image.PixelSize.Height} px");
        AddInfo("位置", entry.FullName);
        if (entry is FileInfo file) AddInfo("大小", FormatSize(file.Length));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Margin = new Thickness(0, 5, 0, 0) };
        actions.Children.Add(ActionButton("打开文件", () => OpenEntry(entry)));
        actions.Children.Add(ActionButton("重命名", () => RenameEntry(entry)));
        actions.Children.Add(ActionButton("删除", DeleteSelectedEntries));
        _preview.Children.Add(actions);
        _fileList.Children.Clear(); foreach (var item in _visibleEntries) _fileList.Children.Add(FileRow(item));
    }

    private void ClearPreview()
    {
        _preview.Children.Clear();
        _currentPreviewBitmap?.Dispose();
        _currentPreviewBitmap = null;
    }

    private void AddInfo(string label, string value)
    {
        var row = new Grid { ColumnDefinitions = { new ColumnDefinition(new GridLength(85)), new ColumnDefinition(new GridLength(1, GridUnitType.Star)) }, Margin = new Thickness(0, 5) };
        row.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = Brush("#85808B") });
        var valueText = new TextBlock { Text = value, FontSize = 11, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(valueText, 1); row.Children.Add(valueText); _preview.Children.Add(row);
    }

    private void CycleLayout()
    {
        _mode = _mode switch { LayoutMode.Preview => LayoutMode.SideBySide, LayoutMode.SideBySide => LayoutMode.Stacked, _ => LayoutMode.Preview };
        _workspace.ColumnDefinitions.Clear();
        _workspace.RowDefinitions.Clear();
        _workspace.Children.Clear();
        _workspace.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(205)));
        _workspace.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        var sidebar = BuildSidebar(); Grid.SetColumn(sidebar, 0); _workspace.Children.Add(sidebar);
        var main = BuildMainPane(); Grid.SetColumn(main, 1); _workspace.Children.Add(main);
        if (_mode == LayoutMode.Preview)
        {
            _workspace.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(340)));
            var details = BuildDetailsPane(); Grid.SetColumn(details, 2); _workspace.Children.Add(details);
            if (_selected is not null) SelectEntry(_selected);
        }
        else
        {
            var second = BuildSecondPane();
            if (_mode == LayoutMode.SideBySide)
            {
                _workspace.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
                _workspace.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
                Grid.SetColumn(second, 2);
            }
            else
            {
                _workspace.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
                _workspace.RowDefinitions = new RowDefinitions("*,*");
                Grid.SetRow(main, 0); Grid.SetColumn(main, 1);
                Grid.SetRow(second, 1); Grid.SetColumn(second, 1);
                Grid.SetColumnSpan(sidebar, 1);
                Grid.SetRowSpan(sidebar, 2);
            }
            _workspace.Children.Add(second);
        }
        SetStatus(_mode switch { LayoutMode.Preview => "预览与信息面板", LayoutMode.SideBySide => "左右双栏 · 第二栏可浏览独立位置", _ => "上下双栏 · 第二栏可浏览独立位置" });
    }

    private Control BuildSecondPane()
    {
        var box = new DockPanel { Margin = new Thickness(18, 12) };
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(4, 4, 4, 12) };
        heading.Children.Add(new TextBlock { Text = _mode == LayoutMode.SideBySide ? "第二面板" : "第二面板 · 下载", FontWeight = FontWeight.SemiBold });
        heading.Children.Add(ActionButton("选择位置…", async () => await ChooseSecondLocation()));
        DockPanel.SetDock(heading, Dock.Top); box.Children.Add(heading);
        _secondList.Children.Clear();
        try
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(_secondPath).Take(200))
            {
                var info = (FileSystemInfo)(Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path));
                _secondList.Children.Add(ActionButton($"{(info is DirectoryInfo ? "▰" : "▤")}　{info.Name}", () => { if (info is DirectoryInfo) { _secondPath = info.FullName; CycleLayoutRefresh(); } else SetStatus(info.FullName); }));
            }
        }
        catch (Exception ex) { _secondList.Children.Add(new TextBlock { Text = ex.Message }); }
        box.Children.Add(new ScrollViewer { Content = _secondList });
        return new Border { BorderBrush = Brush("#EAE7EE"), BorderThickness = new Thickness(1, 0, 0, 0), Child = box };
    }

    private void CycleLayoutRefresh()
    {
        var old = _mode;
        _mode = old == LayoutMode.SideBySide ? LayoutMode.Preview : LayoutMode.Preview;
        CycleLayout();
    }

    private async Task ChooseSecondLocation()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "选择第二面板位置", AllowMultiple = false });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path) { _secondPath = path; CycleLayoutRefresh(); }
    }

    private List<FileSystemInfo> GetTopLevelSelection()
    {
        var selected = _visibleEntries.Where(x => _selectedPaths.Contains(x.FullName)).ToList();
        return selected.Where(candidate => !selected.Any(parent =>
        {
            if (parent is not DirectoryInfo || PathEquals(parent.FullName, candidate.FullName)) return false;
            var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent.FullName)) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(candidate.FullName).StartsWith(prefix, PathComparison);
        })).ToList();
    }

    private void SelectAllVisible()
    {
        if (_visibleEntries.Count == 0) return;
        _selectionAnchor = _visibleEntries[0].FullName;
        SelectEntry(_visibleEntries[^1], range: true);
    }

    private void ClearSelection()
    {
        _selectedPaths.Clear(); _selectionAnchor = null; _selected = null;
        ClearPreview();
        if (_copyButton is not null) _copyButton.IsEnabled = false;
        if (_cutButton is not null) _cutButton.IsEnabled = false;
        _fileList.Children.Clear(); foreach (var item in _visibleEntries) _fileList.Children.Add(FileRow(item));
        SetStatus("未选择项目");
    }

    private async void EditLocation()
    {
        var address = new MdTextBox { Text = _currentPath, Width = 440, Variant = MdTextBoxVariant.Outlined };
        var open = new MdButton { Content = "打开位置", Variant = MdButtonVariant.Filled, Size = MdButtonSize.Small, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var content = new StackPanel { Margin = new Thickness(18), Spacing = 8, Children = { new TextBlock { Text = "输入本地目录路径" }, address, open } };
        var dialog = new Window { Title = "转到位置", Width = 510, Height = 170, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = content };
        open.Click += (_, _) => dialog.Close(address.Text);
        var path = await dialog.ShowDialog<string?>(this);
        if (!string.IsNullOrWhiteSpace(path))
        {
            try { Navigate(Path.GetFullPath(path)); }
            catch (Exception ex) { SetStatus($"路径无效：{ex.Message}"); }
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox) return;
        var shortcut = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (shortcut && e.Key == Key.A) { SelectAllVisible(); e.Handled = true; }
        else if (shortcut && e.Key == Key.L) { EditLocation(); e.Handled = true; }
        else if (shortcut && e.Key == Key.N && e.KeyModifiers.HasFlag(KeyModifiers.Shift)) { CreateFolder(); e.Handled = true; }
        else if (shortcut && e.Key == Key.N) { CreateTextFile(); e.Handled = true; }
        else if (e.Key == Key.Escape) { ClearSelection(); e.Handled = true; }
        else if (e.Key == Key.Enter && _selected is not null) { OpenEntry(_selected); e.Handled = true; }
        else if (e.Key == Key.Back) { Navigate(Directory.GetParent(_currentPath)?.FullName ?? _home); e.Handled = true; }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && e.Key == Key.Left) { NavigateBack(); e.Handled = true; }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && e.Key == Key.Right) { NavigateForward(); e.Handled = true; }
        else if (shortcut && e.Key == Key.C) { SetClipboard(cut: false); e.Handled = true; }
        else if (shortcut && e.Key == Key.X) { SetClipboard(cut: true); e.Handled = true; }
        else if (shortcut && e.Key == Key.V) { PasteClipboard(); e.Handled = true; }
        else if (e.Key == Key.F2 && _selected is not null && _selectedPaths.Count == 1) { RenameEntry(_selected); e.Handled = true; }
        else if (e.Key == Key.Delete && _selectedPaths.Count > 0) { DeleteSelectedEntries(); e.Handled = true; }
    }

    private void SetClipboard(bool cut)
    {
        if (_selectedPaths.Count == 0) { SetStatus("请先选择文件或文件夹"); return; }
        _clipboardPaths = GetTopLevelSelection().Select(x => x.FullName).ToList();
        _cutPending = cut;
        if (_pasteButton is not null) _pasteButton.IsEnabled = true;
        SetStatus($"{(cut ? "已剪切" : "已复制")} {_clipboardPaths.Count} 项 · 粘贴到目标文件夹");
    }

    private async void PasteClipboard()
    {
        if (_operationCancellation is not null) { SetStatus("请先完成或取消当前操作"); return; }
        if (_clipboardPaths.Count == 0) { SetStatus("剪贴板中没有项目"); return; }
        var sources = _clipboardPaths.ToList();
        var isCut = _cutPending;
        var destinationFolder = Path.GetFullPath(_currentPath);
        var completed = new List<string>();
        var operation = StartOperation($"正在{(isCut ? "移动" : "复制")} {sources.Count} 项…", sources.Count);
        var token = operation.Source.Token;
        var finalMessage = string.Empty;
        try
        {
            await Task.Run(() =>
            {
                foreach (var rawSource in sources)
                {
                    token.ThrowIfCancellationRequested();
                    var source = Path.GetFullPath(rawSource);
                    if (!File.Exists(source) && !Directory.Exists(source)) throw new FileNotFoundException("源项目不存在", source);
                    if (isCut && PathEquals(Path.GetDirectoryName(source)!, destinationFolder)) { completed.Add(source); operation.Progress.Report(new(completed.Count, sources.Count, $"跳过已在目标目录的项目 · {completed.Count}/{sources.Count}")); continue; }
                    if (Directory.Exists(source) && destinationFolder.StartsWith(source + Path.DirectorySeparatorChar, PathComparison))
                        throw new InvalidOperationException($"不能将文件夹“{Path.GetFileName(source)}”复制到自身内部");
                    var destination = UniqueDestination(destinationFolder, Path.GetFileName(source));
                    try
                    {
                        if (isCut)
                        {
                            try { MovePath(source, destination); }
                            catch (IOException) { CopyPath(source, destination, token); DeletePath(source, token); }
                        }
                        else CopyPath(source, destination, token);
                        completed.Add(source);
                        operation.Progress.Report(new(completed.Count, sources.Count, $"{(isCut ? "正在移动" : "正在复制")} {Path.GetFileName(source)} · {completed.Count}/{sources.Count}"));
                    }
                    catch
                    {
                        try { if (File.Exists(destination)) File.Delete(destination); else if (Directory.Exists(destination)) DeletePath(destination); } catch { /* Keep the original operation error. */ }
                        throw;
                    }
                }
            }, token);
            finalMessage = $"已{(isCut ? "移动" : "复制")} {completed.Count} 项到当前文件夹";
        }
        catch (OperationCanceledException) { finalMessage = $"操作已取消，已完成 {completed.Count} 项"; }
        catch (Exception ex) { finalMessage = $"已完成 {completed.Count} 项；后续操作失败：{ex.Message}"; }
        finally
        {
            if (isCut) FinishCut(completed);
            StopOperation();
        }
        await LoadDirectoryAsync(_currentPath);
        SetStatus(finalMessage);
    }

    private void FinishCut(IReadOnlyCollection<string> completed)
    {
        _clipboardPaths.RemoveAll(path => completed.Any(done => PathEquals(path, done)));
        if (_clipboardPaths.Count == 0) _cutPending = false;
        _selectedPaths.RemoveWhere(path => completed.Any(done => PathEquals(path, done)));
        _selected = _visibleEntries.LastOrDefault(x => _selectedPaths.Contains(x.FullName));
        if (_selected is null) ClearPreview();
        if (_pasteButton is not null) _pasteButton.IsEnabled = _clipboardPaths.Count > 0;
        if (_copyButton is not null) _copyButton.IsEnabled = _selectedPaths.Count > 0;
        if (_cutButton is not null) _cutButton.IsEnabled = _selectedPaths.Count > 0;
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static bool PathEquals(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), PathComparison);

    private static string UniqueDestination(string folder, string name)
    {
        var original = Path.Combine(folder, name);
        if (!File.Exists(original) && !Directory.Exists(original)) return original;
        var isDirectory = Directory.Exists(original);
        var stem = isDirectory ? name : Path.GetFileNameWithoutExtension(name);
        var extension = isDirectory ? string.Empty : Path.GetExtension(name);
        for (var i = 1; ; i++)
        {
            var copyName = $"{stem} - Copy {i}{extension}";
            var candidate = Path.Combine(folder, copyName);
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
    }

    private static void MovePath(string source, string destination)
    {
        if (Directory.Exists(source)) Directory.Move(source, destination);
        else File.Move(source, destination);
    }

    private static void CopyPath(string source, string destination, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var attributes = File.GetAttributes(source);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new NotSupportedException("首版不复制符号链接/重解析点，以避免越出授权目录。");
        if ((attributes & FileAttributes.Directory) == 0)
        {
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.SequentialScan);
            using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.SequentialScan);
            var buffer = new byte[128 * 1024];
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                output.Write(buffer, 0, read);
            }
            return;
        }
        Directory.CreateDirectory(destination);
        foreach (var child in Directory.EnumerateFileSystemEntries(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            CopyPath(child, Path.Combine(destination, Path.GetFileName(child)), cancellationToken);
        }
    }

    private static void DeletePath(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Directory.Exists(path))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) == 0)
                foreach (var child in Directory.EnumerateFileSystemEntries(path)) DeletePath(child, cancellationToken);
            Directory.Delete(path, recursive: false);
        }
        else File.Delete(path);
    }

    private async void CreateTextFile()
    {
        var input = new MdTextBox { PlaceholderText = "例如：notes.txt", Width = 320, Variant = MdTextBoxVariant.Outlined };
        var create = new MdButton { Content = "创建文件", Variant = MdButtonVariant.Filled, Size = MdButtonSize.Small, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var panel = new StackPanel { Margin = new Thickness(18), Spacing = 8, Children = { new TextBlock { Text = $"在 {_currentPath} 中创建空白文本文件" }, input, create } };
        var dialog = new Window { Title = "新建文本文件", Width = 410, Height = 175, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel };
        create.Click += (_, _) => dialog.Close(input.Text);
        var name = await dialog.ShowDialog<string?>(this);
        if (!IsValidLeafName(name)) { if (!string.IsNullOrWhiteSpace(name)) SetStatus("文件名无效"); return; }
        try
        {
            var filePath = Path.Combine(_currentPath, name!);
            await using (var stream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            _search.Text = "";
            await LoadDirectoryAsync(_currentPath);
            var created = _visibleEntries.FirstOrDefault(x => PathEquals(x.FullName, filePath));
            if (created is not null) SelectEntry(created);
        }
        catch (Exception ex) { SetStatus($"创建文件失败：{ex.Message}"); }
    }

    private static bool IsValidLeafName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..") return false;
        const string crossPlatformInvalid = "<>:\"/\\|?*";
        return !name.Any(ch => char.IsControl(ch) || crossPlatformInvalid.Contains(ch) || Path.GetInvalidFileNameChars().Contains(ch));
    }

    private async void CreateFolder()
    {
        var dialog = new MdTextBox { PlaceholderText = "文件夹名称", Width = 300, Variant = MdTextBoxVariant.Outlined };
        var ok = new MdButton { Content = "创建", Variant = MdButtonVariant.Filled, Size = MdButtonSize.Small, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var stack = new StackPanel { Spacing = 8, Margin = new Thickness(18) };
        stack.Children.Add(new TextBlock { Text = $"在此位置新建文件夹：{_currentPath}", TextWrapping = TextWrapping.Wrap }); stack.Children.Add(dialog); stack.Children.Add(ok);
        var window = new Window { Title = "新建文件夹", Width = 380, Height = 170, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = stack };
        ok.Click += (_, _) => window.Close(dialog.Text);
        var name = await window.ShowDialog<string?>(this);
        if (!IsValidLeafName(name)) { if (!string.IsNullOrWhiteSpace(name)) SetStatus("文件夹名称无效"); return; }
        try
        {
            var path = Path.Combine(_currentPath, name!);
            if (Directory.Exists(path) || File.Exists(path)) { SetStatus("该名称已存在"); return; }
            Directory.CreateDirectory(path);
            await LoadDirectoryAsync(_currentPath);
        }
        catch (Exception ex) { SetStatus($"创建文件夹失败：{ex.Message}"); }
    }

    private async void RenameEntry(FileSystemInfo entry)
    {
        var input = new MdTextBox { Text = entry.Name, Width = 300, Variant = MdTextBoxVariant.Outlined };
        var ok = new MdButton { Content = "重命名", Variant = MdButtonVariant.Filled, Size = MdButtonSize.Small, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(18), Children = { new TextBlock { Text = "输入新名称" }, input, ok } };
        var dialog = new Window { Title = "重命名", Width = 370, Height = 160, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel };
        ok.Click += (_, _) => dialog.Close(input.Text);
        var newName = await dialog.ShowDialog<string?>(this);
        if (!IsValidLeafName(newName) || newName == entry.Name) return;
        try { var destination = Path.Combine(Path.GetDirectoryName(entry.FullName)!, newName); if (entry is DirectoryInfo) Directory.Move(entry.FullName, destination); else File.Move(entry.FullName, destination); await LoadDirectoryAsync(_currentPath); } catch (Exception ex) { SetStatus($"重命名失败：{ex.Message}"); }
    }

    private async void DeleteSelectedEntries()
    {
        if (_operationCancellation is not null) { SetStatus("请先完成或取消当前操作"); return; }
        var entries = GetTopLevelSelection();
        if (entries.Count == 0) { SetStatus("请先选择要删除的项目"); return; }
        var title = entries.Count == 1 ? $"确定永久删除“{entries[0].Name}”？此操作无法撤销。" : $"确定永久删除选中的 {entries.Count} 项？此操作无法撤销。";
        var confirm = new Window { Title = "确认永久删除", Width = 400, Height = 180, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var yes = new MdButton { Content = "永久删除", Variant = MdButtonVariant.Filled, Size = MdButtonSize.Small, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        confirm.Content = new StackPanel { Margin = new Thickness(18), Children = { new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap }, yes } };
        yes.Click += (_, _) => confirm.Close(true);
        if (!await confirm.ShowDialog<bool>(this)) return;
        var deleted = new List<string>();
        var operation = StartOperation($"正在永久删除 {entries.Count} 项…", entries.Count);
        var finalMessage = string.Empty;
        try
        {
            await Task.Run(() =>
            {
                foreach (var entry in entries)
                {
                    operation.Source.Token.ThrowIfCancellationRequested();
                    DeletePath(entry.FullName, operation.Source.Token);
                    deleted.Add(entry.FullName);
                    operation.Progress.Report(new(deleted.Count, entries.Count, $"正在删除 {entry.Name} · {deleted.Count}/{entries.Count}"));
                }
            }, operation.Source.Token);
            finalMessage = $"已永久删除 {deleted.Count} 项";
        }
        catch (OperationCanceledException) { finalMessage = $"删除已取消，已完成 {deleted.Count} 项"; }
        catch (Exception ex) { finalMessage = $"已删除 {deleted.Count} 项；后续删除失败：{ex.Message}"; }
        finally { StopOperation(); }
        _selectedPaths.RemoveWhere(path => deleted.Any(done => PathEquals(path, done)));
        _selectionAnchor = null;
        _selected = _visibleEntries.LastOrDefault(x => _selectedPaths.Contains(x.FullName));
        if (_selected is null) ClearPreview();
        if (_copyButton is not null) _copyButton.IsEnabled = _selectedPaths.Count > 0;
        if (_cutButton is not null) _cutButton.IsEnabled = _selectedPaths.Count > 0;
        await LoadDirectoryAsync(_currentPath);
        SetStatus(finalMessage);
    }

    private void OpenEntry(FileSystemInfo entry)
    {
        if (entry is DirectoryInfo) { Navigate(entry.FullName); return; }
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(entry.FullName) { UseShellExecute = true }); }
        catch (Exception ex) { SetStatus($"无法打开文件：{ex.Message}"); }
    }

    private void Navigate(string path)
    {
        if (!Directory.Exists(path)) { SetStatus("目录不存在或无法访问"); return; }
        if (PathEquals(path, _currentPath)) return;
        _backHistory.Push(_currentPath);
        _forwardHistory.Clear();
        UpdateNavigationButtons();
        _search.Text = "";
        _ = LoadDirectoryAsync(path);
    }

    private void NavigateBack()
    {
        if (_backHistory.Count == 0) return;
        _forwardHistory.Push(_currentPath);
        var path = _backHistory.Pop();
        UpdateNavigationButtons();
        _search.Text = "";
        _ = LoadDirectoryAsync(path);
    }

    private void NavigateForward()
    {
        if (_forwardHistory.Count == 0) return;
        _backHistory.Push(_currentPath);
        var path = _forwardHistory.Pop();
        UpdateNavigationButtons();
        _search.Text = "";
        _ = LoadDirectoryAsync(path);
    }

    private void UpdateNavigationButtons()
    {
        if (_backButton is not null) _backButton.IsEnabled = _backHistory.Count > 0;
        if (_forwardButton is not null) _forwardButton.IsEnabled = _forwardHistory.Count > 0;
    }

    private string ReadTextPreview(string path)
    {
        try { using var reader = new StreamReader(path); var buffer = new char[5000]; var count = reader.ReadBlock(buffer, 0, buffer.Length); return new string(buffer, 0, count); }
        catch (Exception ex) { return $"无法读取预览：{ex.Message}"; }
    }

    private static string IconFor(string ext) => ext.ToLowerInvariant() switch { ".pdf" => "PDF", ".png" or ".jpg" or ".jpeg" => "IMG", ".txt" => "TXT", ".md" => "MD", _ => "FILE" };
    private static string FormatSize(long bytes) => bytes < 1024 ? $"{bytes} B" : bytes < 1024 * 1024 ? $"{bytes / 1024d:0.#} KB" : $"{bytes / (1024d * 1024):0.#} MB";
    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
    private static void AddAt(Grid grid, Control control, int column) { Grid.SetColumn(control, column); grid.Children.Add(control); }
    private static TextBlock Section(string text) => new() { Text = text, FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Brush("#817C87"), Margin = new Thickness(10, 20, 0, 7) };
    private Button SideButton(string text, Action action, bool active = false) { var b = new MdButton { Content = text, Variant = active ? MdButtonVariant.Tonal : MdButtonVariant.Text, Size = MdButtonSize.Small, HorizontalContentAlignment = HorizontalAlignment.Left, HorizontalAlignment = HorizontalAlignment.Stretch, Height = 40, Margin = new Thickness(0, 1), Padding = new Thickness(12, 0) }; b.Click += (_, _) => action(); return b; }
    private static Button ActionButton(string text, Action action, string? tooltip = null) { var b = new MdButton { Content = text, Variant = MdButtonVariant.Tonal, Size = MdButtonSize.Small, MinHeight = 32, FontSize = 12 }; if (tooltip is not null) ToolTip.SetTip(b, tooltip); b.Click += (_, _) => action(); return b; }
    private void CycleTheme()
    {
        _themeMode = _themeMode switch { MdThemeMode.System => MdThemeMode.Light, MdThemeMode.Light => MdThemeMode.Dark, _ => MdThemeMode.System };
        ThemePreferenceStore.Save(_themeMode);
        var app = Application.Current!;
        app.RequestedThemeVariant = _themeMode switch { MdThemeMode.Light => ThemeVariant.Light, MdThemeMode.Dark => ThemeVariant.Dark, _ => ThemeVariant.Default };
        var dark = _themeMode == MdThemeMode.Dark || (_themeMode == MdThemeMode.System && app.ActualThemeVariant == ThemeVariant.Dark);
        var options = MdThemeManager.Current with { ThemeMode = _themeMode };
        _themeRoles = MdThemeManager.Apply(app, options, dark);
        if (_themeButton is not null) _themeButton.Content = ThemeLabel();
        RefreshThemeColors();
    }

    private string ThemeLabel() => _themeMode switch { MdThemeMode.System => "◐ 系统", MdThemeMode.Light => "☀ 浅色", _ => "☾ 深色" };

    private void RefreshThemeColors()
    {
        Background = RoleBrush("Surface");
        RefreshThemeColors(this);
    }

    private void RefreshThemeColors(Visual visual)
    {
        if (visual is Border border)
        {
            border.Background = Remap(border.Background);
            border.BorderBrush = Remap(border.BorderBrush);
        }
        if (visual is Panel panel) panel.Background = Remap(panel.Background);
        if (visual is TemplatedControl control)
        {
            control.Background = Remap(control.Background);
            control.Foreground = Remap(control.Foreground);
            if (control is MdButton) { /* Md3 control templates consume dynamic token resources directly. */ }
        }
        if (visual is TextBlock text) text.Foreground = Remap(text.Foreground);
        foreach (var child in visual.GetVisualChildren()) RefreshThemeColors(child);
    }

    private IBrush? Remap(IBrush? brush)
    {
        if (brush is not SolidColorBrush solid) return brush;
        var role = solid.Color.ToString().ToUpperInvariant() switch
        {
            "#FFF7F6F9" => "Surface",
            "#FFFAF9FB" => "SurfaceContainer",
            "#FFFCFBFD" => "SurfaceContainerLow",
            "#FFEAE7EE" or "#FFEBE7EF" => "OutlineVariant",
            "#FF77737F" or "#FF85808B" or "#FF817C87" => "OnSurfaceVariant",
            "#FFEEE8F7" => "SecondaryContainer",
            "#FF493481" => "OnSecondaryContainer",
            "#FF55515C" => "OnSurface",
            "#FFF4F2F6" => "SurfaceContainerHigh",
            "#FF6750A4" => "Primary",
            "#FFE8DEF8" => "PrimaryContainer",
            _ => null
        };
        return role is null ? brush : RoleBrush(role);
    }

    private IBrush RoleBrush(string role) => _themeRoles.TryGetValue(role, out var color) ? new SolidColorBrush(color) : Brushes.Transparent;

    private string LayoutLabel() => _mode switch { LayoutMode.Preview => "预览布局", LayoutMode.SideBySide => "左右双栏", _ => "上下双栏" };
    private void SetStatus(string text) => _status.Text = text;
}
