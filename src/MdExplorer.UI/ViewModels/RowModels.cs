namespace MdExplorer.UI.ViewModels;

/// <summary>主页/抽屉中的卷条目（顶层类型便于 XAML 编译绑定引用）。</summary>
public sealed record VolumeItemModel(string Name, string Path, string UsageText, double UsedPercent, string? Glyph);

/// <summary>回收站行。</summary>
public sealed record TrashItemModel(string Id, string Name, string OriginalPath, string DeletedText, bool IsFolder);

/// <summary>预览元数据行。</summary>
public sealed record MetadataRowModel(string Key, string Value);

/// <summary>压缩包条目行。</summary>
public sealed record ArchiveEntryRowModel(string Name, string SizeText);
