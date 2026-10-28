using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using MdExplorer.Domain.Errors;
using MdExplorer.Domain.Files;
using MdExplorer.Platform.Abstractions.FileSystem;
using MdExplorer.Platform.Abstractions.Shell;

namespace MdExplorer.Infrastructure.Shell;

/// 三端行为一致的应用私有回收站（§4.1）：移动到 AppData/trash + manifest.json。
/// 平台层可通过 ITrashService 替换为系统回收站实现（Windows Shell / Linux XDG）。
public sealed class AppTrashService : ITrashService
{
    private readonly IFileSystemService _files;
    private readonly IMimeTypeService _mime;
    private readonly string _trashDir;
    private readonly string _manifestPath;

    public AppTrashService(IFileSystemService files, IMimeTypeService mime, string dataDirectory)
    {
        _files = files;
        _mime = mime;
        _trashDir = Path.Combine(dataDirectory, "trash");
        _manifestPath = Path.Combine(_trashDir, "manifest.json");
        Directory.CreateDirectory(_trashDir);
    }

    public bool IsSupported => true;

    private List<ManifestEntry> Load()
    {
        try
        {
            return JsonSerializer.Deserialize(
                       File.ReadAllText(_manifestPath), TrashJsonContext.Default.ListManifestEntry) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void Save(List<ManifestEntry> entries)
        => File.WriteAllText(_manifestPath,
            JsonSerializer.Serialize(entries, TrashJsonContext.Default.ListManifestEntry));

    public async ValueTask<Result> MoveToTrashAsync(FileLocator item, CancellationToken ct)
    {
        var path = item.ToLocalPath();
        if (!File.Exists(path) && !Directory.Exists(path))
            return Result.Fail(FileErrorKind.NotFound, "条目不存在", item);
        var id = Guid.NewGuid().ToString("N");
        var name = Path.GetFileName(path);
        var isDir = Directory.Exists(path);
        var size = isDir ? 0L : new FileInfo(path).Length;
        var stored = Path.Combine(_trashDir, id + "__" + name);
        try
        {
            if (isDir) Directory.Move(path, stored);
            else File.Move(path, stored, overwrite: true);
            var entries = Load();
            entries.Add(new ManifestEntry(id, path, name, isDir, size, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
            Save(entries);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail(FileErrorKind.IoError, ex.Message, item, ex);
        }
    }

    public async IAsyncEnumerable<TrashedItem> ListTrashAsync([EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var entry in Load().OrderByDescending(e => e.DeletedAt))
        {
            ct.ThrowIfCancellationRequested();
            var stored = Path.Combine(_trashDir, entry.Id + "__" + entry.Name);
            var item = new FileItem(
                FileLocator.FromPath(stored),
                entry.Name,
                entry.IsDir ? FileKind.Folder : _mime.GetKind(entry.Name, false),
                entry.IsDir ? null : entry.Size,
                DateTimeOffset.FromUnixTimeSeconds(entry.DeletedAt),
                false, false, false,
                entry.IsDir ? null : Path.GetExtension(entry.Name),
                null);
            yield return new TrashedItem(entry.Id, item, DateTimeOffset.FromUnixTimeSeconds(entry.DeletedAt), entry.OriginalPath);
            await Task.Yield();
        }
    }

    public ValueTask<Result> RestoreAsync(string trashItemId, CancellationToken ct)
    {
        var entries = Load();
        var entry = entries.FirstOrDefault(e => e.Id == trashItemId);
        if (entry is null)
            return ValueTask.FromResult(Result.Fail(FileErrorKind.NotFound, "回收站条目不存在"));
        var stored = Path.Combine(_trashDir, entry.Id + "__" + entry.Name);
        var dest = entry.OriginalPath;
        try
        {
            if (File.Exists(dest) || Directory.Exists(dest))
            {
                var dir = Path.GetDirectoryName(dest) ?? string.Empty;
                dest = Path.Combine(dir, FileOperationUnique(entry.Name));
            }
            Directory.CreateDirectory(Path.GetDirectoryName(dest) ?? string.Empty);
            if (entry.IsDir) Directory.Move(stored, dest);
            else File.Move(stored, dest, overwrite: true);
            entries.Remove(entry);
            Save(entries);
            return ValueTask.FromResult(Result.Ok());
        }
        catch (Exception ex)
        {
            return ValueTask.FromResult(Result.Fail(FileErrorKind.IoError, ex.Message, null, ex));
        }
    }

    public ValueTask<Result> DeletePermanentlyAsync(FileLocator item, CancellationToken ct)
    {
        try
        {
            var p = item.ToLocalPath();
            if (File.Exists(p)) File.Delete(p);
            else if (Directory.Exists(p)) Directory.Delete(p, recursive: true);
            return ValueTask.FromResult(Result.Ok());
        }
        catch (Exception ex)
        {
            return ValueTask.FromResult(Result.Fail(FileErrorKind.IoError, ex.Message, item, ex));
        }
    }

    public ValueTask<Result> EmptyTrashAsync(CancellationToken ct)
    {
        try
        {
            foreach (var entry in Load())
            {
                var stored = Path.Combine(_trashDir, entry.Id + "__" + entry.Name);
                if (File.Exists(stored)) File.Delete(stored);
                else if (Directory.Exists(stored)) Directory.Delete(stored, recursive: true);
            }
            Save([]);
            return ValueTask.FromResult(Result.Ok());
        }
        catch (Exception ex)
        {
            return ValueTask.FromResult(Result.Fail(FileErrorKind.IoError, ex.Message, null, ex));
        }
    }

    private static string FileOperationUnique(string name)
    {
        var ext = Path.GetExtension(name);
        var stem = Path.GetFileNameWithoutExtension(name);
        return $"{stem} (restored){ext}";
    }
}

/// 回收站清单条目（JSON 源生成，AOT 安全）。
internal sealed record ManifestEntry(string Id, string OriginalPath, string Name, bool IsDir, long Size, long DeletedAt);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<ManifestEntry>))]
internal sealed partial class TrashJsonContext : JsonSerializerContext
{
}
