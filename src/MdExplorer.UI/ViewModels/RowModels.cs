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

/// <summary>批量重命名预览行（Error 非空=整批阻断并标红）。</summary>
public sealed record BatchRenameRowModel(string Original, string Proposed, string? Error)
{
    public bool HasError => Error is not null;
}
