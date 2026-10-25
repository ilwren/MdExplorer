using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Md3.Avalonia.Controls;
using Md3.Avalonia.Themes.Dynamic;
using Avalonia.VisualTree;

namespace MdExplorer.Desktop.Views;

public sealed class MainWindow : Window
{
    private readonly Grid _workspace = new();
    private readonly StackPanel _fileList = new() { Spacing = 2 };
    private readonly StackPanel _secondList = new() { Spacing = 2 };
    private readonly StackPanel _preview = new() { Spacing = 10 };
    private readonly TextBlock _pathLabel = new();
    private readonly TextBlock _countLabel = new();
    private readonly MdTextBox _search = new() { PlaceholderText = "搜索此文件夹", Variant = MdTextBoxVariant.Outlined, ShowClearButton = true };
    private Button? _themeButton;
    private IReadOnlyDictionary<string, Color> _themeRoles = new Dictionary<string, Color>();
    private MdThemeMode _themeMode = MdThemeMode.System;
    private readonly TextBlock _status = new();
    private readonly string _home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private string _currentPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private string _secondPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    private FileSystemInfo? _selected;
    private LayoutMode _mode = LayoutMode.Preview;
    private CancellationTokenSource? _loadCancellation;

    private enum LayoutMode { Preview, SideBySide, Stacked }

    public MainWindow()
    {
        Title = "MdExplorer · 本地文件";
        Width = 1450;
        Height = 900;
        MinWidth = 760;
        MinHeight = 540;
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
        nav.Children.Add(ActionButton("‹", () => Navigate(Directory.GetParent(_currentPath)?.FullName ?? _home), "返回"));
        nav.Children.Add(ActionButton("›", () => { }, "前进"));
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
        tools.Children.Add(ActionButton("＋ 新建", CreateFolder, "新建文件夹"));
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
        actions.Children.Add(ActionButton("名称　⌄", () => SetStatus("按名称排序")));
        actions.Children.Add(ActionButton("☷", () => SetStatus("列表视图")));
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

    private async Task LoadDirectoryAsync(string path)
    {
        _loadCancellation?.Cancel();
        var cts = _loadCancellation = new CancellationTokenSource();
        var query = _search.Text?.Trim() ?? "";
        try
        {
            var entries = await Task.Run(() => Directory.EnumerateFileSystemEntries(path)
                .Select(p => (FileSystemInfo)(Directory.Exists(p) ? new DirectoryInfo(p) : new FileInfo(p)))
                .Where(x => string.IsNullOrEmpty(query) || x.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x is DirectoryInfo).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                .Take(1000).ToList(), cts.Token);
            if (cts.IsCancellationRequested) return;
            _currentPath = path;
            _pathLabel.Text = path;
            _countLabel.Text = $"{entries.Count} 个项目 · 按名称排序";
            _fileList.Children.Clear();
            foreach (var entry in entries) _fileList.Children.Add(FileRow(entry));
            SetStatus($"{path}　·　{entries.Count} 个项目");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { SetStatus($"无法读取目录：{ex.Message}"); }
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
        var button = new MdButton { Content = grid, Variant = _selected?.FullName == entry.FullName ? MdButtonVariant.Tonal : MdButtonVariant.Text, Size = MdButtonSize.Small, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch };
        button.Click += (_, _) => SelectEntry(entry);
        button.DoubleTapped += (_, _) => { if (isDirectory) _ = LoadDirectoryAsync(entry.FullName); else SelectEntry(entry); };
        return button;
    }

    private void SelectEntry(FileSystemInfo entry)
    {
        _selected = entry;
        _preview.Children.Clear();
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
            try { previewBox.Content = new Image { Source = new Avalonia.Media.Imaging.Bitmap(entry.FullName), Stretch = Stretch.Uniform, MaxHeight = 250 }; }
            catch { previewBox.Content = new TextBlock { Text = "无法解码此图片" }; }
        }
        else previewBox.Content = new TextBlock { Text = "暂无内置预览\n可使用系统应用打开。", TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        _preview.Children.Add(previewBox);
        _preview.Children.Add(new TextBlock { Text = "文件信息", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 10, 0, 0) });
        AddInfo("类型", entry is DirectoryInfo ? "文件夹" : (string.IsNullOrEmpty(ext) ? "文件" : ext.TrimStart('.').ToUpperInvariant()));
        AddInfo("修改时间", entry.LastWriteTime.ToString("yyyy-MM-dd HH:mm"));
        AddInfo("位置", entry.FullName);
        if (entry is FileInfo file) AddInfo("大小", FormatSize(file.Length));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Margin = new Thickness(0, 5, 0, 0) };
        actions.Children.Add(ActionButton("打开文件", () => OpenEntry(entry)));
        actions.Children.Add(ActionButton("重命名", () => RenameEntry(entry)));
        actions.Children.Add(ActionButton("删除", () => DeleteEntry(entry)));
        _preview.Children.Add(actions);
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

    private async void CreateFolder()
    {
        var dialog = new MdTextBox { PlaceholderText = "文件夹名称", Width = 300, Variant = MdTextBoxVariant.Outlined };
        var ok = new MdButton { Content = "创建", Variant = MdButtonVariant.Filled, Size = MdButtonSize.Small, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var stack = new StackPanel { Spacing = 8, Margin = new Thickness(18) };
        stack.Children.Add(new TextBlock { Text = $"在此位置新建文件夹：{_currentPath}", TextWrapping = TextWrapping.Wrap }); stack.Children.Add(dialog); stack.Children.Add(ok);
        var window = new Window { Title = "新建文件夹", Width = 380, Height = 170, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = stack };
        ok.Click += (_, _) => window.Close(dialog.Text);
        var name = await window.ShowDialog<string?>(this);
        if (!string.IsNullOrWhiteSpace(name)) try { Directory.CreateDirectory(Path.Combine(_currentPath, name)); await LoadDirectoryAsync(_currentPath); } catch (Exception ex) { SetStatus(ex.Message); }
    }

    private async void RenameEntry(FileSystemInfo entry)
    {
        var input = new MdTextBox { Text = entry.Name, Width = 300, Variant = MdTextBoxVariant.Outlined };
        var ok = new MdButton { Content = "重命名", Variant = MdButtonVariant.Filled, Size = MdButtonSize.Small, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(18), Children = { new TextBlock { Text = "输入新名称" }, input, ok } };
        var dialog = new Window { Title = "重命名", Width = 370, Height = 160, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel };
        ok.Click += (_, _) => dialog.Close(input.Text);
        var newName = await dialog.ShowDialog<string?>(this);
        if (string.IsNullOrWhiteSpace(newName) || newName == entry.Name || newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return;
        try { var destination = Path.Combine(Path.GetDirectoryName(entry.FullName)!, newName); if (entry is DirectoryInfo) Directory.Move(entry.FullName, destination); else File.Move(entry.FullName, destination); await LoadDirectoryAsync(_currentPath); } catch (Exception ex) { SetStatus($"重命名失败：{ex.Message}"); }
    }

    private async void DeleteEntry(FileSystemInfo entry)
    {
        var confirm = new Window { Title = "确认删除", Width = 380, Height = 160, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var yes = new MdButton { Content = "永久删除", Variant = MdButtonVariant.Filled, Size = MdButtonSize.Small, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        confirm.Content = new StackPanel { Margin = new Thickness(18), Children = { new TextBlock { Text = $"确定永久删除“{entry.Name}”？此操作无法撤销。", TextWrapping = TextWrapping.Wrap }, yes } };
        yes.Click += (_, _) => confirm.Close(true);
        var accepted = await confirm.ShowDialog<bool>(this);
        if (!accepted) return;
        try { if (entry is DirectoryInfo) Directory.Delete(entry.FullName, true); else File.Delete(entry.FullName); _selected = null; _preview.Children.Clear(); await LoadDirectoryAsync(_currentPath); } catch (Exception ex) { SetStatus($"删除失败：{ex.Message}"); }
    }

    private void OpenEntry(FileSystemInfo entry)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(entry.FullName) { UseShellExecute = true }); }
        catch (Exception ex) { SetStatus($"无法打开文件：{ex.Message}"); }
    }

    private void Navigate(string path)
    {
        if (!Directory.Exists(path)) { SetStatus("目录不存在或无法访问"); return; }
        _search.Text = "";
        _ = LoadDirectoryAsync(path);
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
