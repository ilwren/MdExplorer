using CommunityToolkit.Mvvm.ComponentModel;
using MdExplorer.Domain.Files;
using MdExplorer.Platform.Abstractions.Preview;
using MdExplorer.UI.Services;

namespace MdExplorer.UI.ViewModels;

/// 预览面板 ViewModel（§5.4 PreviewContent 联合 → 视图状态）。
public sealed partial class PreviewViewModel : ObservableObject
{
    private readonly UiServices _svc;

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private bool _isText;
    [ObservableProperty] private bool _isMarkdown;
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private bool _isImage;
    [ObservableProperty] private string? _imagePath;
    [ObservableProperty] private bool _isPdf;
    [ObservableProperty] private bool _isMedia;
    [ObservableProperty] private string? _mediaPath;
    [ObservableProperty] private string _mediaTitle = "";
    [ObservableProperty] private bool _isArchive;
    [ObservableProperty] private bool _isMetadata;
    [ObservableProperty] private string _metadataSummary = "";
    [ObservableProperty] private bool _isEmpty = true;

    public System.Collections.ObjectModel.ObservableCollection<ArchiveEntryRow> ArchiveEntries { get; } = new();

    public PreviewViewModel(UiServices svc) => _svc = svc;

    public async Task ShowAsync(FileItem item, CancellationToken ct = default)
    {
        Title = item.Name;
        Subtitle = $"{item.Kind} · {item.LastWriteTime.LocalDateTime:yyyy-MM-dd HH:mm}" +
                   (item.Length is { } l ? $" · {Formatters.Size(l)}" : "");
        IsEmpty = false;
        try
        {
            var content = await _svc.Preview.LoadAsync(item, new PreviewOptions(), ct);
            Reset();
            switch (content)
            {
                case TextContent t:
                    IsText = true;
                    IsMarkdown = t.IsMarkdown;
                    Text = t.Text;
                    break;
                case ImageContent i:
                    IsImage = true;
                    ImagePath = i.SourcePath;
                    break;
                case PdfContent p:
                    IsPdf = true;
                    MediaPath = p.SourcePath;
                    MediaTitle = item.Name;
                    break;
                case VideoContent v:
                    IsMedia = true;
                    MediaPath = v.SourcePath;
                    MediaTitle = item.Name;
                    break;
                case AudioContent a:
                    IsMedia = true;
                    MediaPath = a.SourcePath;
                    MediaTitle = a.Title ?? item.Name;
                    break;
                case ArchiveContent a:
                    IsArchive = true;
                    ArchiveEntries.Clear();
                    foreach (var e in a.Entries.Take(500))
                        ArchiveEntries.Add(new ArchiveEntryRow(
                            e.Name,
                            e.IsDirectory ? "文件夹" : e.Length is { } l2 ? Formatters.Size(l2) : "",
                            e.IsDirectory ? "folder" : "file"));
                    break;
                case MetadataOnlyContent m:
                    IsMetadata = true;
                    MetadataSummary = m.Summary;
                    break;
                default:
                    IsMetadata = true;
                    MetadataSummary = "暂不支持此格式的内嵌预览";
                    break;
            }
        }
        catch (Exception)
        {
            Reset();
            IsMetadata = true;
            MetadataSummary = "预览加载失败";
        }
    }

    private void Reset()
    {
        IsText = IsMarkdown = IsImage = IsPdf = IsMedia = IsArchive = IsMetadata = false;
        Text = "";
        ImagePath = MediaPath = null;
        MediaTitle = MetadataSummary = "";
        ArchiveEntries.Clear();
    }

    public void Clear()
    {
        Reset();
        IsEmpty = true;
        Title = Subtitle = "";
    }
}

public sealed record ArchiveEntryRow(string Name, string SizeLabel, string IconName);
