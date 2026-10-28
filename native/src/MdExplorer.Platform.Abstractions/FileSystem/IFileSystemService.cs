using MdExplorer.Domain.Errors;
using MdExplorer.Domain.Files;

namespace MdExplorer.Platform.Abstractions.FileSystem;

public sealed record ListOptions(bool IncludeHidden = false, int? MaxCount = null);

/// 文件系统端口（§5.2）——最重要的接口。桌面实现走 System.IO；
/// Android 实现按定位符 scheme 分发（SAF / 应用专属目录），对上透明。
public interface IFileSystemService
{
    IAsyncEnumerable<FileItem> ListAsync(FileLocator folder, ListOptions options, CancellationToken ct);
    ValueTask<Result<FileItem>> StatAsync(FileLocator locator, CancellationToken ct);
    ValueTask<Result<Stream>> OpenReadAsync(FileLocator file, CancellationToken ct);
    ValueTask<Result<Stream>> OpenWriteAsync(FileLocator file, bool overwrite, CancellationToken ct);
    ValueTask<Result<FileLocator>> CreateFolderAsync(FileLocator parent, string name, CancellationToken ct);
    ValueTask<Result<FileLocator>> CreateFileAsync(FileLocator parent, string name, CancellationToken ct);
    ValueTask<Result<FileLocator>> RenameAsync(FileLocator source, string newName, CancellationToken ct);
    ValueTask<Result<FileLocator>> GetParentAsync(FileLocator locator, CancellationToken ct);
    ValueTask<bool> ExistsAsync(FileLocator locator, CancellationToken ct);
    ValueTask<Result> DeletePermanentlyAsync(FileLocator locator, CancellationToken ct);
    ValueTask<Result<FileLocator>> MoveAsync(FileLocator source, FileLocator destinationFolder, CancellationToken ct);
    ValueTask<Result<FileLocator>> CopyAsync(FileLocator source, FileLocator destinationFolder, CancellationToken ct);
}

/// 卷与已知目录：UI 永不自己推导「文档/下载」在哪（§5.2）。
public interface IVolumeService
{
    ValueTask<IReadOnlyList<StorageVolume>> GetVolumesAsync(CancellationToken ct);
    event EventHandler? VolumesChanged;
}

public interface IKnownFoldersService
{
    /// 返回 null 表示该平台无此概念或未授权。
    ValueTask<FileLocator?> GetAsync(KnownFolder folder, CancellationToken ct);
}

/// 父级/拼接/规范化/相对化，按平台大小写与分隔符规则（§5.2）。
public interface IPathService
{
    FileLocator? GetParent(FileLocator locator);
    FileLocator Combine(FileLocator folder, string name);
    string GetFileName(FileLocator locator);
    string? GetExtension(FileLocator locator);
    string Describe(FileLocator locator); // 展示用短路径
}

/// 扩展名 → MIME/Kind（§5.2）。
public interface IMimeTypeService
{
    string? GetMimeType(string? extension);
    FileKind GetKind(string name, bool isDirectory);
}
