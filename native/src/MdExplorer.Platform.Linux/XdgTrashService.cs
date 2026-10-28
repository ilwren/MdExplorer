using System.Runtime.CompilerServices;
using MdExplorer.Domain.Errors;
using MdExplorer.Domain.Files;
using MdExplorer.Platform.Abstractions.Shell;

namespace MdExplorer.Platform.Linux;

/// freedesktop XDG 回收站实现（~/.local/share/Trash）。
public sealed class XdgTrashService : ITrashService
{
    private readonly IMimeTypeLike _kindProbe;
    private readonly string _trashHome;

    public XdgTrashService(IMimeTypeLike kindProbe)
    {
        _kindProbe = kindProbe;
        _trashHome = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "share", "Trash");
        Directory.CreateDirectory(Path.Combine(_trashHome, "files"));
        Directory.CreateDirectory(Path.Combine(_trashHome, "info"));
    }

    public bool IsSupported => true;

    public ValueTask<Result> MoveToTrashAsync(FileLocator item, CancellationToken ct)
    {
        var path = item.ToLocalPath();
        if (!File.Exists(path) && !Directory.Exists(path))
            return ValueTask.FromResult(Result.Fail(FileErrorKind.NotFound, "条目不存在", item));
        var name = Path.GetFileName(path);
        var stored = Path.Combine(_trashHome, "files", name);
        var n = 2;
        while (File.Exists(stored) || Directory.Exists(stored))
            stored = Path.Combine(_trashHome, "files", $"{Path.GetFileNameWithoutExtension(name)} ({n++}){Path.GetExtension(name)}");
        try
        {
            if (Directory.Exists(path)) Directory.Move(path, stored);
            else File.Move(path, stored, overwrite: true);
            var infoPath = Path.Combine(_trashHome, "info", Path.GetFileName(stored) + ".trashinfo");
            File.WriteAllText(infoPath,
                $"[Trash Info]\nPath={Uri.EscapeDataString(path)}\nDeletionDate={DateTime.Now:yyyy-MM-ddTHH:mm:ss}\n");
            return ValueTask.FromResult(Result.Ok());
        }
        catch (Exception ex)
        {
            return ValueTask.FromResult(Result.Fail(FileErrorKind.IoError, ex.Message, item, ex));
        }
    }

    public async IAsyncEnumerable<TrashedItem> ListTrashAsync([EnumeratorCancellation] CancellationToken ct)
    {
        string[] infos;
        try
        {
            infos = Directory.GetFiles(Path.Combine(_trashHome, "info"), "*.trashinfo");
        }
        catch
        {
            yield break;
        }
        foreach (var info in infos)
        {
            ct.ThrowIfCancellationRequested();
            TrashedItem? item = null;
            try
            {
                var text = File.ReadAllText(info);
                var name = Path.GetFileNameWithoutExtension(info); // 去掉 .trashinfo
                var stored = Path.Combine(_trashHome, "files", name);
                if (!File.Exists(stored) && !Directory.Exists(stored)) continue;
                var original = "";
                foreach (var line in text.Split('\n'))
                {
                    if (line.StartsWith("Path=", StringComparison.Ordinal))
                        original = Uri.UnescapeDataString(line["Path=".Length..].Trim());
                }
                var isDir = Directory.Exists(stored);
                var fi = new FileInfo(stored);
                var fileItem = new FileItem(
                    FileLocator.FromPath(stored),
                    name,
                    isDir ? FileKind.Folder : _kindProbe.Probe(name),
                    isDir ? null : fi.Length,
                    fi.LastWriteTimeUtc,
                    false, false, false,
                    isDir ? null : Path.GetExtension(name),
                    null);
                item = new TrashedItem(info, fileItem, fi.LastWriteTimeUtc, original);
            }
            catch
            {
                // 单条损坏跳过
            }
            if (item is not null)
            {
                yield return item;
                await Task.Yield();
            }
        }
    }

    public ValueTask<Result> RestoreAsync(string trashItemId, CancellationToken ct)
    {
        try
        {
            var info = trashItemId;
            var name = Path.GetFileNameWithoutExtension(info); // 去掉 .trashinfo
            var stored = Path.Combine(_trashHome, "files", name);
            var text = File.ReadAllText(info);
            var original = "";
            foreach (var line in text.Split('\n'))
            {
                if (line.StartsWith("Path=", StringComparison.Ordinal))
                    original = Uri.UnescapeDataString(line["Path=".Length..].Trim());
            }
            if (original.Length == 0)
                return ValueTask.FromResult(Result.Fail(FileErrorKind.IoError, "回收站元数据损坏"));
            if (File.Exists(original) || Directory.Exists(original))
            {
                var dir = Path.GetDirectoryName(original) ?? "";
                original = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(original)} (restored){Path.GetExtension(original)}");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(original) ?? "");
            if (Directory.Exists(stored)) Directory.Move(stored, original);
            else File.Move(stored, original, overwrite: true);
            File.Delete(info);
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
            foreach (var f in Directory.GetFiles(Path.Combine(_trashHome, "files"))) File.Delete(f);
            foreach (var d in Directory.GetDirectories(Path.Combine(_trashHome, "files"))) Directory.Delete(d, true);
            foreach (var f in Directory.GetFiles(Path.Combine(_trashHome, "info"))) File.Delete(f);
            return ValueTask.FromResult(Result.Ok());
        }
        catch (Exception ex)
        {
            return ValueTask.FromResult(Result.Fail(FileErrorKind.IoError, ex.Message, null, ex));
        }
    }
}

/// 类型探测的最小钩子（避免平台层引用 Infrastructure）。
public interface IMimeTypeLike
{
    FileKind Probe(string name);
}

/// Linux 能力位。
public sealed class LinuxCapabilities : IPlatformCapabilities
{
    public bool HasTrash => true;
    public bool HasSystemFileClipboard => true;
    public bool HasRevealInExplorer => true;
    public bool SupportsMultiWindow => true;
    public bool TouchFirst => false;
}
