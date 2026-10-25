using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MdExplorer.Domain;
using MdExplorer.Platform.Abstractions;

namespace MdExplorer.UI.ViewModels;

/// <summary>
/// 预览状态机（v0.1）：图片位图 / 文本（含代码、数据、未知二进制 Hex 摘要）/ ZIP 条目 / 纯元数据卡。
/// 每次 Load 先递增代次并作废上一轮，快速切选不串内容。
/// </summary>
public sealed partial class PreviewViewModel(
    IArchiveService archive,
    Func<IFileLauncherService> launcherRefresh) : ObservableObject
{
    private const long TextLimit = 512 * 1024;
    private int _generation;

    [ObservableProperty] private bool _hasItem;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private Bitmap? _image;
    [ObservableProperty] private string? _textContent;
    [ObservableProperty] private string? _failureText;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _canOpen;

    public bool HasImage => Image is not null;
    public bool HasText => TextContent is not null;
    public bool HasArchive => ArchiveEntries.Count > 0;
    public bool HasFailure => FailureText is not null;

    public ObservableCollection<MetadataRowModel> MetadataRows { get; } = [];
    public ObservableCollection<ArchiveEntryRowModel> ArchiveEntries { get; } = [];

    public FileItem? CurrentItem { get; private set; }

    partial void OnImageChanged(Bitmap? value) => OnPropertyChanged(nameof(HasImage));
    partial void OnTextContentChanged(string? value) => OnPropertyChanged(nameof(HasText));
    partial void OnFailureTextChanged(string? value) => OnPropertyChanged(nameof(HasFailure));


    public async Task LoadAsync(FileItem? item)
    {
        var generation = ++_generation;
        CurrentItem = item;
        Image = null;
        TextContent = null;
        FailureText = null;
        ArchiveEntries.Clear();
        MetadataRows.Clear();
        OnPropertyChanged(nameof(HasArchive));

        if (item is null)
        {
            HasItem = false;
            CanOpen = false;
            Title = string.Empty;
            Subtitle = string.Empty;
            IsLoading = false;
            return;
        }

        HasItem = true;
        CanOpen = !item.IsFolder;
        Title = item.Name;
        Subtitle = FileKindClassifier.Describe(item);
        BuildMetadata(item);
        if (item.IsFolder)
        {
            IsLoading = false;
            return;
        }

        IsLoading = true;
        try
        {
            switch (item.Kind)
            {
                case FileKind.Image:
                    Image = await Task.Run(() =>
                    {
                        using var stream = File.OpenRead(item.FullPath);
                        return Bitmap.DecodeToWidth(stream, 1600);
                    }).ConfigureAwait(true);
                    break;

                case FileKind.Text or FileKind.Code or FileKind.Data:
                    TextContent = await ReadTextPreviewAsync(item).ConfigureAwait(true);
                    break;

                case FileKind.Archive when archive.CanOpen(item.FullPath):
                    var entries = await archive.ListEntriesAsync(item.FullPath, 500, CancellationToken.None).ConfigureAwait(true);
                    if (generation != _generation) return;
                    foreach (var entry in entries)
                        ArchiveEntries.Add(new ArchiveEntryRowModel(entry.FullName, entry.IsFolder ? "" : Formatting.Bytes(entry.Length)));
                    OnPropertyChanged(nameof(HasArchive));
                    break;

                case FileKind.Unknown:
                    TextContent = await ReadHexPreviewAsync(item).ConfigureAwait(true);
                    break;

                default:
                    // PDF/音视频/字体/可执行：元数据卡 + 系统打开（开发文档 §6.5；PDF 渲染与播放为后续里程碑）
                    FailureText = item.Kind switch
                    {
                        FileKind.Document when FileKindClassifier.IsPdf(item) => "PDF 内置预览将在后续版本提供，可先用系统应用打开。",
                        FileKind.Video or FileKind.Audio => "内置播放将在后续版本提供，可先用系统应用打开。",
                        _ => "此类型暂不支持内置预览，可先用系统应用打开。",
                    };
                    break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            if (generation == _generation)
                FailureText = $"预览失败：{ex.Message}";
        }
        finally
        {
            if (generation == _generation)
                IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        if (CurrentItem is null) return;
        // Android 上启动器在 MainView 挂载后替换，故每次取最新实例
        await launcherRefresh().OpenAsync(CurrentItem.FullPath, CancellationToken.None).ConfigureAwait(true);
    }

    private void BuildMetadata(FileItem item)
    {
        MetadataRows.Add(new MetadataRowModel("类型", FileKindClassifier.Describe(item)));
        if (!item.IsFolder)
            MetadataRows.Add(new MetadataRowModel("大小", $"{Formatting.Bytes(item.Length)}（{item.Length:N0} 字节）"));
        MetadataRows.Add(new MetadataRowModel("修改时间", Formatting.FullDateTime(item.LastWriteTime)));
        MetadataRows.Add(new MetadataRowModel("位置", Path.GetDirectoryName(item.FullPath) ?? item.FullPath));
        if (item.IsSymlink)
            MetadataRows.Add(new MetadataRowModel("链接", "符号链接"));
        if (item.IsHidden)
            MetadataRows.Add(new MetadataRowModel("可见性", "隐藏"));
    }

    private static async Task<string> ReadTextPreviewAsync(FileItem item)
    {
        await using var stream = new FileStream(item.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var buffer = new byte[Math.Min(TextLimit, Math.Max(0, stream.Length))];
        var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(true);
        var text = Encoding.UTF8.GetString(buffer, 0, read);
        if (stream.Length > TextLimit)
            text += $"\n\n…（仅预览前 {Formatting.Bytes(TextLimit)}，文件共 {Formatting.Bytes(item.Length)}）";
        return text;
    }

    private static async Task<string> ReadHexPreviewAsync(FileItem item)
    {
        await using var stream = new FileStream(item.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var buffer = new byte[Math.Min(512, Math.Max(0, stream.Length))];
        var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(true);
        var builder = new StringBuilder();
        for (int offset = 0; offset < read; offset += 16)
        {
            builder.Append(offset.ToString("X8")).Append("  ");
            for (int i = 0; i < 16; i++)
                builder.Append(offset + i < read ? buffer[offset + i].ToString("X2") + ' ' : "   ");
            builder.Append(" |");
            for (int i = 0; i < 16 && offset + i < read; i++)
            {
                var b = buffer[offset + i];
                builder.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
            }
            builder.Append("|\n");
        }
        builder.Append($"\n（十六进制预览 · 前 {read} 字节）");
        return builder.ToString();
    }
}
