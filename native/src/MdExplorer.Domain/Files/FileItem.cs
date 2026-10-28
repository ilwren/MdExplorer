namespace MdExplorer.Domain.Files;

public enum FileKind
{
    Folder,
    Image,
    Video,
    Audio,
    Document,
    Archive,
    Code,
    Text,
    Executable,
    Unknown,
}

/// 条目实体（§5.1）。文件夹 Length 为 null（不递归算大小）。
public sealed record FileItem(
    FileLocator Locator,
    string Name,
    FileKind Kind,
    long? Length,
    DateTimeOffset LastWriteTime,
    bool IsHidden,
    bool IsSymlink,
    bool IsReadOnly,
    string? Extension,
    string? MimeType);

public sealed record StorageVolume(
    string Id,
    string DisplayName,
    FileLocator Root,
    long? TotalBytes,
    long? FreeBytes,
    bool IsRemovable,
    bool IsPrimary);

public enum KnownFolder
{
    Home,
    Documents,
    Downloads,
    Pictures,
    Music,
    Videos,
    Desktop,
    AppData,
    Cache,
}

public enum ClipboardOperation
{
    Copy,
    Cut,
}
