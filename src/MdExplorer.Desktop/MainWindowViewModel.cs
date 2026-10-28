using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace MdExplorer.Desktop;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly Stack<string> _backStack = new();
    private readonly Stack<string> _forwardStack = new();
    private string _currentPath;
    private string _searchQuery = "";
    private string _typeFilter = "";
    private string _pathInput = "";
    private string _statusText = "";
    private FileEntry? _selectedEntry;
    private bool _isPathEditing;
    private bool _isDark;
    private bool _isGridView = true;
    private bool _isListView;
    private bool _isDetailsView;
    private bool _showHidden;
    private bool _isCompactNavigationOpen;
    private bool _isFloatingPreviewOpen;
    private bool _isFloatingPreviewExpanded;
    private string _previewTab = "预览";
    private string _activeDestination = "主页";

    public MainWindowViewModel()
    {
        _currentPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(_currentPath) || !Directory.Exists(_currentPath))
            _currentPath = Environment.CurrentDirectory;

        NavigationItems = new ObservableCollection<NavigationItem>
        {
            new("⌂", "主页", "Home"),
            new("◷", "最近", "Recent"),
            new("☆", "收藏", "Starred"),
            new("▧", "图片", "Pictures"),
            new("▷", "视频", "Videos"),
            new("▤", "文档", "Documents"),
            new("⇩", "下载", "Downloads")
        };
        Volumes = new ObservableCollection<VolumeItem>();

        NavigateBackCommand = new RelayCommand(_ => NavigateBack(), _ => _backStack.Count > 0);
        NavigateForwardCommand = new RelayCommand(_ => NavigateForward(), _ => _forwardStack.Count > 0);
        NavigateUpCommand = new RelayCommand(_ => NavigateUp());
        RefreshCommand = new RelayCommand(_ => LoadDirectory(_currentPath));
        CreateFolderCommand = new RelayCommand(_ => CreateFolder());
        ToggleThemeCommand = new RelayCommand(_ => IsDark = !IsDark);
        ToggleHiddenCommand = new RelayCommand(_ => { ShowHidden = !ShowHidden; LoadDirectory(_currentPath); });
        TogglePathEditCommand = new RelayCommand(_ => BeginPathEdit());
        GoToPathCommand = new RelayCommand(_ => CommitPathEdit());
        ToggleNavigationCommand = new RelayCommand(_ => IsCompactNavigationOpen = !IsCompactNavigationOpen);
        NewTabCommand = new RelayCommand(_ => StatusText = "已创建一个概念标签页（标签会话即将接入）");
        OpenTasksCommand = new RelayCommand(_ => StatusText = "当前没有进行中的文件任务");
        OpenEntryCommand = new RelayCommand(parameter =>
        {
            if (parameter is FileEntry entry) OpenEntry(entry);
        });
        SelectEntryCommand = new RelayCommand(parameter =>
        {
            if (parameter is FileEntry entry) SelectedEntry = entry;
        });
        NavigateShortcutCommand = new RelayCommand(parameter =>
        {
            if (parameter is NavigationItem item) NavigateShortcut(item);
        });
        NavigateSegmentCommand = new RelayCommand(parameter =>
        {
            if (parameter is PathSegment segment) NavigateTo(segment.FullPath);
        });

        LoadDirectory(_currentPath);
        LoadVolumes();
    }

    public ObservableCollection<FileEntry> Entries { get; } = new();
    public ObservableCollection<PathSegment> Breadcrumbs { get; } = new();
    public ObservableCollection<NavigationItem> NavigationItems { get; }
    public ObservableCollection<VolumeItem> Volumes { get; }

    public string CurrentPath { get => _currentPath; private set => SetField(ref _currentPath, value); }
    public string SearchQuery
    {
        get => _searchQuery;
        set { if (SetField(ref _searchQuery, value)) { FilterEntries(); UpdateStatus(); } }
    }
    public string TypeFilter
    {
        get => _typeFilter;
        private set { if (SetField(ref _typeFilter, value)) FilterEntries(); }
    }
    public string PathInput { get => _pathInput; set => SetField(ref _pathInput, value); }
    public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }
    public FileEntry? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (_selectedEntry == value) return;
            if (_selectedEntry is not null) _selectedEntry.IsSelected = false;
            SetField(ref _selectedEntry, value);
            if (_selectedEntry is not null) _selectedEntry.IsSelected = true;
            PreviewTab = "预览";
            OnPropertyChanged(nameof(PreviewTitle));
            OnPropertyChanged(nameof(PreviewSubtitle));
            OnPropertyChanged(nameof(PreviewGlyph));
            OnPropertyChanged(nameof(PreviewSize));
            OnPropertyChanged(nameof(PreviewModified));
        }
    }
    public bool IsPathEditing
    {
        get => _isPathEditing;
        set
        {
            if (SetField(ref _isPathEditing, value)) OnPropertyChanged(nameof(IsBreadcrumbVisible));
        }
    }
    public bool IsBreadcrumbVisible => !IsPathEditing;
    public bool IsDark { get => _isDark; set => SetField(ref _isDark, value); }
    public bool IsGridView { get => _isGridView; private set => SetField(ref _isGridView, value); }
    public bool IsListView { get => _isListView; private set => SetField(ref _isListView, value); }
    public bool IsDetailsView { get => _isDetailsView; private set => SetField(ref _isDetailsView, value); }
    public bool ShowHidden { get => _showHidden; private set => SetField(ref _showHidden, value); }
    public bool IsCompactNavigationOpen { get => _isCompactNavigationOpen; set => SetField(ref _isCompactNavigationOpen, value); }
    public bool IsFloatingPreviewOpen { get => _isFloatingPreviewOpen; set => SetField(ref _isFloatingPreviewOpen, value); }
    public bool IsFloatingPreviewExpanded { get => _isFloatingPreviewExpanded; set => SetField(ref _isFloatingPreviewExpanded, value); }
    public string PreviewTab
    {
        get => _previewTab;
        set
        {
            if (SetField(ref _previewTab, value))
            {
                OnPropertyChanged(nameof(IsPreviewTab));
                OnPropertyChanged(nameof(IsPropertiesTab));
            }
        }
    }
    public bool IsPreviewTab => PreviewTab == "预览";
    public bool IsPropertiesTab => PreviewTab == "属性";
    public string ActiveDestination { get => _activeDestination; private set => SetField(ref _activeDestination, value); }
    public bool CanGoBack => _backStack.Count > 0;
    public bool CanGoForward => _forwardStack.Count > 0;
    public string CountText => SearchQuery.Length > 0
        ? $"{Entries.Count} 个匹配项"
        : $"{Entries.Count} 个项目";
    public bool IsEmpty => Entries.Count == 0;

    public string PreviewTitle => SelectedEntry?.Name ?? "选择一个文件以预览";
    public string PreviewSubtitle => SelectedEntry is null ? "预览与属性" : SelectedEntry.TypeLabel;
    public string PreviewGlyph => SelectedEntry?.Glyph ?? "▧";
    public string PreviewSize => SelectedEntry?.SizeText ?? "—";
    public string PreviewModified => SelectedEntry?.ModifiedText ?? "—";

    public ICommand NavigateBackCommand { get; }
    public ICommand NavigateForwardCommand { get; }
    public ICommand NavigateUpCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand CreateFolderCommand { get; }
    public ICommand ToggleThemeCommand { get; }
    public ICommand ToggleHiddenCommand { get; }
    public ICommand TogglePathEditCommand { get; }
    public ICommand GoToPathCommand { get; }
    public ICommand ToggleNavigationCommand { get; }
    public ICommand NewTabCommand { get; }
    public ICommand OpenTasksCommand { get; }
    public ICommand OpenEntryCommand { get; }
    public ICommand SelectEntryCommand { get; }
    public ICommand NavigateShortcutCommand { get; }
    public ICommand NavigateSegmentCommand { get; }

    public void SetView(string view)
    {
        IsGridView = view == "grid";
        IsListView = view == "list";
        IsDetailsView = view == "details";
    }

    public void SetTypeFilter(string filter)
    {
        TypeFilter = filter;
    }

    public void NavigateTo(string path, bool addHistory = true)
    {
        try
        {
            path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            if (path == "~") path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (File.Exists(path))
            {
                var filePath = Path.GetFullPath(path);
                var parentPath = Path.GetDirectoryName(filePath);
                if (parentPath is not null)
                {
                    NavigateTo(parentPath, addHistory);
                    SelectedEntry = Entries.FirstOrDefault(item => string.Equals(item.FullPath, filePath, StringComparison.Ordinal));
                    IsPathEditing = false;
                }
                return;
            }
            if (!Directory.Exists(path))
            {
                StatusText = $"找不到文件夹：{path}";
                return;
            }

            var fullPath = Path.GetFullPath(path);
            if (string.Equals(fullPath, _currentPath, StringComparison.Ordinal))
            {
                IsPathEditing = false;
                return;
            }
            if (addHistory)
            {
                _backStack.Push(_currentPath);
                _forwardStack.Clear();
            }
            CurrentPath = fullPath;
            ActiveDestination = "此文件夹";
            SelectedEntry = null;
            IsFloatingPreviewOpen = false;
            SearchQuery = "";
            TypeFilter = "";
            IsPathEditing = false;
            IsCompactNavigationOpen = false;
            RefreshHistoryCommands();
            BuildBreadcrumbs();
            LoadDirectory(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            StatusText = $"无法打开此位置：{ex.Message}";
        }
    }

    public void BeginPathEdit()
    {
        PathInput = CurrentPath;
        IsPathEditing = true;
    }

    public void CommitPathEdit()
    {
        if (string.IsNullOrWhiteSpace(PathInput))
        {
            IsPathEditing = false;
            return;
        }
        NavigateTo(PathInput);
    }

    public void OpenEntry(FileEntry entry)
    {
        if (entry.IsDirectory)
        {
            NavigateTo(entry.FullPath);
            return;
        }
        SelectedEntry = entry;
        try
        {
            Process.Start(new ProcessStartInfo(entry.FullPath) { UseShellExecute = true });
            StatusText = $"已交由系统应用打开：{entry.Name}";
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            StatusText = $"无法打开文件：{ex.Message}";
        }
    }

    private void NavigateShortcut(NavigationItem item)
    {
        ActiveDestination = item.Title;
        if (item.Key == "Recent")
        {
            SearchQuery = "";
            LoadDirectory(_currentPath);
            StatusText = "最近项目暂以当前文件夹内容展示";
            return;
        }
        if (item.Key == "Starred")
        {
            Entries.Clear();
            SelectedEntry = null;
            UpdateStatus();
            StatusText = "收藏夹尚无内容";
            return;
        }
        var folder = item.Key switch
        {
            "Home" => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Pictures" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "Videos" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            "Documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Downloads" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            _ => _currentPath
        };
        if (Directory.Exists(folder)) NavigateTo(folder);
        else StatusText = $"此位置不存在：{folder}";
    }

    private void NavigateBack()
    {
        if (_backStack.Count == 0) return;
        _forwardStack.Push(_currentPath);
        var target = _backStack.Pop();
        RefreshHistoryCommands();
        NavigateTo(target, addHistory: false);
    }

    private void NavigateForward()
    {
        if (_forwardStack.Count == 0) return;
        _backStack.Push(_currentPath);
        var target = _forwardStack.Pop();
        RefreshHistoryCommands();
        NavigateTo(target, addHistory: false);
    }

    private void NavigateUp()
    {
        var parent = Directory.GetParent(_currentPath)?.FullName;
        if (parent is not null) NavigateTo(parent);
    }

    private void LoadDirectory(string path)
    {
        try
        {
            var entries = new DirectoryInfo(path).EnumerateFileSystemInfos()
                .Where(item => ShowHidden || !item.Name.StartsWith(".", StringComparison.Ordinal))
                .Select(CreateEntry)
                .OrderByDescending(item => item.IsDirectory)
                .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            _allEntries.Clear();
            _allEntries.AddRange(entries);
            Entries.Clear();
            SelectedEntry = null;
            BuildBreadcrumbs();
            FilterEntries();
            UpdateStatus();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _allEntries.Clear();
            Entries.Clear();
            OnPropertyChanged(nameof(CountText));
            OnPropertyChanged(nameof(IsEmpty));
            StatusText = $"无法读取此文件夹：{ex.Message}";
        }
    }

    private static FileEntry CreateEntry(FileSystemInfo info)
    {
        var isDirectory = info is DirectoryInfo;
        long size = 0;
        if (info is FileInfo file)
        {
            try { size = file.Length; } catch (IOException) { }
        }
        return new FileEntry(info.Name, info.FullName, isDirectory, size, info.LastWriteTime);
    }

    private void FilterEntries()
    {
        // Keep filesystem enumeration separate from the visible list so clearing any filter restores all entries.
        var filtered = _allEntries.Where(item =>
            (string.IsNullOrWhiteSpace(SearchQuery) || item.Name.Contains(SearchQuery, StringComparison.CurrentCultureIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(TypeFilter) || MatchesType(item, TypeFilter)))
            .ToList();
        Entries.Clear();
        foreach (var item in filtered) Entries.Add(item);
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private readonly List<FileEntry> _allEntries = new();

    private static bool MatchesType(FileEntry item, string type)
    {
        if (item.IsDirectory) return false;
        var extension = Path.GetExtension(item.Name).ToLowerInvariant();
        return type switch
        {
            "image" => extension is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or ".tif" or ".tiff" or ".svg",
            "video" => extension is ".mp4" or ".mkv" or ".mov" or ".avi" or ".webm" or ".m4v",
            "audio" => extension is ".mp3" or ".wav" or ".flac" or ".aac" or ".m4a" or ".ogg" or ".wma",
            "archive" => extension is ".zip" or ".7z" or ".tar" or ".gz" or ".rar" or ".bz2" or ".xz",
            "document" => extension is ".pdf" or ".doc" or ".docx" or ".txt" or ".md" or ".rtf" or ".csv" or ".xls" or ".xlsx" or ".ppt" or ".pptx",
            _ => true
        };
    }

    private void UpdateStatus()
    {
        OnPropertyChanged(nameof(CountText));
        if (string.IsNullOrWhiteSpace(SearchQuery)) StatusText = $"{Entries.Count} 个项目";
    }

    private void BuildBreadcrumbs()
    {
        Breadcrumbs.Clear();
        var root = Path.GetPathRoot(_currentPath) ?? _currentPath;
        var relative = _currentPath[root.Length..];
        Breadcrumbs.Add(new PathSegment(root.TrimEnd(Path.DirectorySeparatorChar) is { Length: > 0 } label ? label : root, root));
        var current = root;
        foreach (var part in relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            Breadcrumbs.Add(new PathSegment(part, current));
        }
    }

    private void LoadVolumes()
    {
        Volumes.Clear();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady) continue;
                var total = drive.TotalSize;
                var free = drive.AvailableFreeSpace;
                Volumes.Add(new VolumeItem(drive.Name, FormatBytes(free) + " 可用", total <= 0 ? 0 : (double)(total - free) / total));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private void CreateFolder()
    {
        var baseName = "新建文件夹";
        var candidate = Path.Combine(_currentPath, baseName);
        var index = 2;
        while (Directory.Exists(candidate)) candidate = Path.Combine(_currentPath, $"{baseName} ({index++})");
        try
        {
            Directory.CreateDirectory(candidate);
            LoadDirectory(_currentPath);
            SelectedEntry = Entries.FirstOrDefault(item => item.FullPath == candidate);
            StatusText = $"已创建：{Path.GetFileName(candidate)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = $"创建文件夹失败：{ex.Message}";
        }
    }

    private void RefreshHistoryCommands()
    {
        (NavigateBackCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (NavigateForwardCommand as RelayCommand)?.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size.ToString("0.#", CultureInfo.InvariantCulture)} {units[unit]}";
    }
}

public sealed class FileEntry : ObservableObject
{
    private bool _isSelected;
    public FileEntry(string name, string fullPath, bool isDirectory, long size, DateTime modified)
    {
        Name = name;
        FullPath = fullPath;
        IsDirectory = isDirectory;
        Size = size;
        Modified = modified;
        var extension = Path.GetExtension(name).TrimStart('.').ToUpperInvariant();
        Extension = isDirectory ? "文件夹" : string.IsNullOrEmpty(extension) ? "文件" : extension + " 文件";
        Glyph = isDirectory ? "▰" : Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" => "▧",
            ".pdf" => "▤",
            ".zip" or ".7z" or ".tar" or ".gz" => "▣",
            ".mp4" or ".mkv" or ".mov" => "▷",
            ".cs" or ".js" or ".json" or ".md" or ".txt" => "{ }",
            _ => "▤"
        };
        SizeText = isDirectory ? "文件夹" : FormatBytes(size);
        ModifiedText = modified.ToString("yyyy/M/d HH:mm", CultureInfo.CurrentCulture);
    }
    public string Name { get; }
    public string FullPath { get; }
    public bool IsDirectory { get; }
    public long Size { get; }
    public DateTime Modified { get; }
    public string Extension { get; }
    public string Glyph { get; }
    public string SizeText { get; }
    public string ModifiedText { get; }
    public string TypeLabel => IsDirectory ? "文件夹" : Extension;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetField(ref _isSelected, value)) OnPropertyChanged(nameof(SelectionMark));
        }
    }
    public string SelectionMark => IsSelected ? "✓" : "";
    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size.ToString("0.#", CultureInfo.InvariantCulture)} {units[unit]}";
    }
}

public sealed record PathSegment(string Label, string FullPath);
public sealed record NavigationItem(string Glyph, string Title, string Key);
public sealed record VolumeItem(string Name, string FreeText, double UsedRatio);

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => execute(parameter);
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
