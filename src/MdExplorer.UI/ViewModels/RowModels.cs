using System.Globalization;
using MdExplorer.Domain;

namespace MdExplorer.UI.ViewModels;

/// <summary>主页/抽屉中的卷条目（顶层类型便于 XAML 编译绑定引用）。</summary>
public sealed record VolumeItemModel(string Name, string Path, string UsageText, double UsedPercent, string? Glyph)
{
    public string UsedPercentText => $"{UsedPercent:0}%";
}

/// <summary>已授权文件夹（Android SAF）入口行模型。Path 为 content:// 定位串。</summary>
public sealed record AuthorizedRootModel(string Name, string Path);

/// <summary>文件名索引根行（仅本地文件夹）。</summary>
public sealed record SearchIndexRootModel(string Name, string Path);

/// <summary>回收站行。</summary>
public sealed record TrashItemModel(string Id, string Name, string OriginalPath, string DeletedText, bool IsFolder);

/// <summary>预览元数据行。</summary>
public sealed record MetadataRowModel(string Key, string Value);

/// <summary>压缩包条目行。</summary>
public sealed record ArchiveEntryRowModel(string Name, string SizeText);

/// <summary>存储洞察的类型占用行。</summary>
public sealed record StorageKindUsageModel(
    FileKind Kind,
    string Name,
    string? Glyph,
    long TotalBytes,
    long FileCount,
    double Percent)
{
    public string SizeText => Formatting.Bytes(TotalBytes);
    public string PercentText => Percent.ToString("0.#", CultureInfo.InvariantCulture) + "%";
    public string FileCountText => FileCount.ToString("N0", CultureInfo.InvariantCulture);
}

/// <summary>存储洞察的一级子文件夹占用行。</summary>
public sealed record StorageFolderUsageModel(
    string Name,
    string Path,
    long TotalBytes,
    long FileCount,
    long FolderCount,
    double Percent,
    bool IsRootFiles)
{
    public string SizeText => Formatting.Bytes(TotalBytes);
    public string PercentText => Percent.ToString("0.#", CultureInfo.InvariantCulture) + "%";
    public string CountText => $"{FileCount.ToString("N0", CultureInfo.InvariantCulture)} 个文件";
}

/// <summary>精确重复文件复核组；只展示与定位，不承载删除选择。</summary>
public sealed record StorageDuplicateGroupModel(
    long FileSize,
    string Sha256,
    IReadOnlyList<FileItemViewModel> Files)
{
    public int Count => Files.Count;
    public long TotalBytes => FileSize * Count;
    public long ReclaimableBytes => FileSize * Math.Max(0, Count - 1);
    public string EachSizeText => Formatting.Bytes(FileSize);
    public string ReclaimableText => Formatting.Bytes(ReclaimableBytes);
    public string ShortHash => Sha256.Length <= 12 ? Sha256 : Sha256[..12];
    public string SummaryText => $"{Count} 份 · 每份 {EachSizeText} · 可复核 {ReclaimableText}";
}

/// <summary>批量重命名预览行（Error 非空=整批阻断并标红）。</summary>
public sealed record BatchRenameRowModel(string Original, string Proposed, string? Error)
{
    public bool HasError => Error is not null;
}
