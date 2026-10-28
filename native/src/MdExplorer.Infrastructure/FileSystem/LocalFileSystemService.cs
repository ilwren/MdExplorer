using System.Runtime.CompilerServices;
using MdExplorer.Domain.Errors;
using MdExplorer.Domain.Files;
using MdExplorer.Platform.Abstractions.FileSystem;

namespace MdExplorer.Infrastructure.FileSystem;

/// 桌面/通用实现：System.IO 目录枚举与文件读写（§4.1「三端行为一致」的逻辑）。
/// 不做 IsWindows() 判断；平台差异经端口进出。
public sealed class LocalFileSystemService : IFileSystemService
{
    private readonly IMimeTypeService _mime;

    public LocalFileSystemService(IMimeTypeService mime) => _mime = mime;

    private static string P(FileLocator locator) => locator.ToLocalPath();

    public async IAsyncEnumerable<FileItem> ListAsync(
        FileLocator folder, ListOptions options, [EnumeratorCancellation] CancellationToken ct)
    {
        IEnumerable<FileSystemInfo> entries;
        try
        {
            entries = new DirectoryInfo(P(folder)).EnumerateFileSystemInfos();
        }
        catch
        {
            yield break;
        }

        foreach (var info in entries)
        {
            ct.ThrowIfCancellationRequested();
            FileItem item;
            try
            {
                if (!options.IncludeHidden && (info.Attributes & FileAttributes.Hidden) != 0)
                    continue;
                item = Describe(info);
            }
            catch
            {
                continue;
            }
            yield return item;
            await Task.Yield();
        }
    }

    public ValueTask<Result<FileItem>> StatAsync(FileLocator locator, CancellationToken ct)
    {
        try
        {
            var path = P(locator);
            var isDir = Directory.Exists(path);
            if (!isDir && !File.Exists(path))
                return ValueTask.FromResult(Result<FileItem>.Fail(FileErrorKind.NotFound, "条目不存在", locator));
            var item = isDir
                ? Describe(new DirectoryInfo(path))
                : Describe(new FileInfo(path));
            return ValueTask.FromResult(Result<FileItem>.Ok(item));
        }
        catch (Exception ex)
        {
            return ValueTask.FromResult(Result<FileItem>.Fail(Map(ex), ex.Message, locator, ex));
        }
    }

    public ValueTask<Result<Stream>> OpenReadAsync(FileLocator file, CancellationToken ct)
    {
        try
        {
            Stream s = File.OpenRead(P(file));
            return ValueTask.FromResult(Result<Stream>.Ok(s));
        }
        catch (Exception ex)
        {
            return ValueTask.FromResult(Result<Stream>.Fail(Map(ex), ex.Message, file, ex));
        }
    }

    public ValueTask<Result<Stream>> OpenWriteAsync(FileLocator file, bool overwrite, CancellationToken ct)
    {
        try
        {
            Stream s = new FileStream(P(file), overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write);
            return ValueTask.FromResult(Result<Stream>.Ok(s));
        }
        catch (Exception ex)
        {
            return ValueTask.FromResult(Result<Stream>.Fail(Map(ex), ex.Message, file, ex));
        }
    }

    public ValueTask<Result<FileLocator>> CreateFolderAsync(FileLocator parent, string name, CancellationToken ct)
    {
        try
        {
            var full = Path.Combine(P(parent), name);
            Directory.CreateDirectory(full);
            return ValueTask.FromResult(Result<FileLocator>.Ok(FileLocator.FromPath(full)));
        }
        catch (Exception ex)
        {
            return ValueTask.FromResult(Result<FileLocator>.Fail(Map(ex), ex.Message, parent, ex));
        }
    }

    public ValueTask<Result<FileLocator>> CreateFileAsync(FileLocator parent, string name, CancellationToken ct)
    {
        try
        {
            var full = Path.Combine(P(parent), name);
            using (File.Create(full))
            {
            }
            return ValueTask.FromResult(Result<FileLocator>.Ok(FileLocator.FromPath(full)));
        }
        catch (Exception ex)
        {
            return ValueTask.FromResult(Result<FileLocator>.Fail(Map(ex), ex.Message, parent, ex));
        }
    }

    public ValueTask<Result<FileLocator>> RenameAsync(FileLocator source, string newName, CancellationToken ct)
    {
        try
        {
            var src = P(source);
            var dest = Path.Combine(Path.GetDirectoryName(src) ?? string.Empty, newName);
            if (File.Exists(dest) || Directory.Exists(dest))
                return ValueTask.FromResult(Result<FileLocator>.Fail(FileErrorKind.NameConflict, "目标名称已存在", source));
            if (File.Exists(src)) File.Move(src, dest);
            else Directory.Move(src, dest);
            return ValueTask.FromResult(Result<FileLocator>.Ok(FileLocator.FromPath(dest)));
        }
        catch (Exception ex)
        {
            return ValueTask.FromResult(Result<FileLocator>.Fail(Map(ex), ex.Message, source, ex));
        }
    }

    public ValueTask<Result<FileLocator>> GetParentAsync(FileLocator locator, CancellationToken ct)
    {
        try
        {
            var parent = Path.GetDirectoryName(P(locator));
            return string.IsNullOrEmpty(parent)
                ? ValueTask.FromResult(Result<FileLocator>.Fail(FileErrorKind.NotFound, "没有父级", locator))
                : ValueTask.FromResult(Result<FileLocator>.Ok(FileLocator.FromPath(parent)));
        }
        catch (Exception ex)
        {
            return ValueTask.FromResult(Result<FileLocator>.Fail(Map(ex), ex.Message, locator, ex));
        }
    }

    public ValueTask<bool> ExistsAsync(FileLocator locator, CancellationToken ct)
    {
        try
        {
            var p = P(locator);
            return ValueTask.FromResult(File.Exists(p) || Directory.Exists(p));
        }
        catch
        {
            return ValueTask.FromResult(false);
        }
    }

    public ValueTask<Result> DeletePermanentlyAsync(FileLocator locator, CancellationToken ct)
    {
        try
        {
            var p = P(locator);
            if (File.Exists(p)) File.Delete(p);
            else if (Directory.Exists(p)) Directory.Delete(p, recursive: true);
            return ValueTask.FromResult(Result.Ok());
        }
        catch (Exception ex)
        {
            return ValueTask.FromResult(Result.Fail(Map(ex), ex.Message, locator));
        }
    }

    public ValueTask<Result<FileLocator>> MoveAsync(FileLocator source, FileLocator destinationFolder, CancellationToken ct)
    {
        try
        {
            var src = P(source);
            var dest = Path.Combine(P(destinationFolder), Path.GetFileName(src));
            if (File.Exists(src)) File.Move(src, dest, overwrite: true);
            else Directory.Move(src, dest);
            return ValueTask.FromResult(Result<FileLocator>.Ok(FileLocator.FromPath(dest)));
        }
        catch (Exception ex)
        {
            return ValueTask.FromResult(Result<FileLocator>.Fail(Map(ex), ex.Message, source, ex));
        }
    }

    public ValueTask<Result<FileLocator>> CopyAsync(FileLocator source, FileLocator destinationFolder, CancellationToken ct)
    {
        try
        {
            var src = P(source);
            var dest = Path.Combine(P(destinationFolder), Path.GetFileName(src));
            if (File.Exists(src))
            {
                File.Copy(src, dest, overwrite: true);
            }
            else if (Directory.Exists(src))
            {
                CopyDirectory(src, dest);
            }
            return ValueTask.FromResult(Result<FileLocator>.Ok(FileLocator.FromPath(dest)));
        }
        catch (Exception ex)
        {
            return ValueTask.FromResult(Result<FileLocator>.Fail(Map(ex), ex.Message, source, ex));
        }
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.EnumerateDirectories(source))
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }

    private FileItem Describe(FileSystemInfo info)
    {
        var isDir = (info.Attributes & FileAttributes.Directory) != 0;
        long? length = isDir ? null : ((FileInfo)info).Length;
        return new FileItem(
            FileLocator.FromPath(info.FullName),
            info.Name,
            _mime.GetKind(info.Name, isDir),
            length,
            info.LastWriteTimeUtc,
            (info.Attributes & FileAttributes.Hidden) != 0,
            (info.Attributes & FileAttributes.ReparsePoint) != 0,
            (info.Attributes & FileAttributes.ReadOnly) != 0,
            isDir ? null : Path.GetExtension(info.Name).ToLowerInvariant(),
            isDir ? null : _mime.GetMimeType(info.Extension));
    }

    private static FileErrorKind Map(Exception ex) => ex switch
    {
        UnauthorizedAccessException => FileErrorKind.AccessDenied,
        DirectoryNotFoundException or FileNotFoundException => FileErrorKind.NotFound,
        PathTooLongException => FileErrorKind.PathTooLong,
        IOException => FileErrorKind.IoError,
        _ => FileErrorKind.Unknown,
    };
}
