using System.Collections.ObjectModel;

namespace MdExplorer.Desktop;

/// <summary>Display-only archive workbench. The archive is a synthetic read-only sample.</summary>
public sealed class ArchiveWorkbenchViewModel : ObservableObject
{
    private string _currentFolder = "/";
    private ArchiveEntry? _selectedEntry;
    private string _targetPath = "D:/解压/Project-assets";
    private bool _keepStructure = true;
    private bool _askBeforeOverwrite = true;
    private bool _openAfterDone;
    private string _statusText = "规划预览样例 · 压缩包尚未打开真实文件";
    private double _progress = 64;

    public ArchiveWorkbenchViewModel()
    {
        Entries = new ObservableCollection<ArchiveEntry>
        {
            new("README.md", "/", "文档", "24 KB", "8 KB", "▤"),
            new("metadata.json", "/", "数据", "12 KB", "4 KB", "{ }"),
            new("assets", "/", "文件夹", "—", "—", "▰"),
            new("docs", "/", "文件夹", "—", "—", "▰"),
            new("coastline-morning.jpg", "/assets", "图片", "3.1 MB", "2.4 MB", "▧"),
            new("wildflower-04.png", "/assets", "图片", "2.4 MB", "1.8 MB", "▧"),
            new("concept-notes.md", "/docs", "文档", "680 KB", "212 KB", "▤"),
            new("sample-data.csv", "/docs", "表格", "1.2 MB", "344 KB", "▤")
        };
        VisibleEntries = new ObservableCollection<ArchiveEntry>();
        UpdateVisibleEntries();
        SelectedEntry = VisibleEntries.FirstOrDefault();
    }

    public ObservableCollection<ArchiveEntry> Entries { get; }
    public ObservableCollection<ArchiveEntry> VisibleEntries { get; }
    public string CurrentFolder { get => _currentFolder; private set => SetField(ref _currentFolder, value); }
    public ArchiveEntry? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (SetField(ref _selectedEntry, value))
            {
                OnPropertyChanged(nameof(PreviewName));
                OnPropertyChanged(nameof(PreviewKind));
                OnPropertyChanged(nameof(PreviewGlyph));
                OnPropertyChanged(nameof(PreviewSize));
                OnPropertyChanged(nameof(PreviewPackedSize));
            }
        }
    }
    public string TargetPath { get => _targetPath; set => SetField(ref _targetPath, value); }
    public bool KeepStructure { get => _keepStructure; set => SetField(ref _keepStructure, value); }
    public bool AskBeforeOverwrite { get => _askBeforeOverwrite; set => SetField(ref _askBeforeOverwrite, value); }
    public bool OpenAfterDone { get => _openAfterDone; set => SetField(ref _openAfterDone, value); }
    public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }
    public double Progress { get => _progress; private set => SetField(ref _progress, value); }
    public string ProgressText => $"{Progress:0}%";
    public string ArchivePath => $"Project-assets.zip  ›  {CurrentFolder.TrimStart('/')}";
    public string PreviewName => SelectedEntry?.Name ?? "选择一个条目";
    public string PreviewKind => SelectedEntry?.Kind ?? "压缩包条目";
    public string PreviewGlyph => SelectedEntry?.Glyph ?? "▣";
    public string PreviewSize => SelectedEntry?.OriginalSize ?? "—";
    public string PreviewPackedSize => SelectedEntry?.PackedSize ?? "—";

    public void NavigateTree(string folder)
    {
        CurrentFolder = folder;
        UpdateVisibleEntries();
        SelectedEntry = VisibleEntries.FirstOrDefault();
    }

    public void SelectEntry(ArchiveEntry? entry)
    {
        if (entry is null) return;
        if (entry.Kind == "文件夹")
        {
            NavigateTree(CurrentFolder == "/" ? $"/{entry.Name}" : $"{CurrentFolder}/{entry.Name}");
            return;
        }
        SelectedEntry = entry;
    }

    public void ExtractAll()
    {
        StatusText = $"演示：已创建“解压全部”任务到 {TargetPath}；没有写入真实文件";
        Progress = 100;
        OnPropertyChanged(nameof(ProgressText));
    }

    public void ExtractSelected()
    {
        StatusText = SelectedEntry is null
            ? "请先选择一个条目"
            : $"演示：已将“{SelectedEntry.Name}”加入提取任务；没有写入真实文件";
    }

    private void UpdateVisibleEntries()
    {
        var entries = Entries.Where(entry => entry.ParentPath == CurrentFolder).ToArray();
        VisibleEntries.Clear();
        foreach (var entry in entries) VisibleEntries.Add(entry);
        OnPropertyChanged(nameof(ArchivePath));
    }
}

public sealed record ArchiveEntry(string Name, string ParentPath, string Kind, string OriginalSize, string PackedSize, string Glyph);
