using System.IO.Compression;
using MdExplorer.Domain.Errors;
using MdExplorer.Domain.Files;
using MdExplorer.Platform.Abstractions.Preview;

namespace MdExplorer.Infrastructure.Archives;

/// ZIP 只读虚拟目录 + 解压/压缩（§6.2 压缩能力）。
/// 仅支持 zip（System.IO.Compression）；7z/rar 预览降级为元数据（§6.5 降级策略）。
public sealed class ZipArchiveService : MdExplorer.Platform.Abstractions.Archives.IArchiveService
{
    /// 列出 zip 的虚拟目录 children（inner = '' 时为顶层）。
    public Result<IReadOnlyList<ArchiveEntry>> ListChildren(string zipPath, string inner)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var prefix = inner.Length > 0 ? inner.TrimEnd('/') + "/" : string.Empty;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var outList = new List<ArchiveEntry>();
            foreach (var entry in zip.Entries)
            {
                var full = entry.FullName;
                if (!full.StartsWith(prefix, StringComparison.Ordinal)) continue;
                var rest = full[prefix.Length..];
                if (rest.Length == 0) continue;
                var slash = rest.IndexOf('/');
                if (slash >= 0)
                {
                    var dirName = rest[..slash];
                    if (!seen.Add(dirName)) continue;
                    outList.Add(new ArchiveEntry(dirName, true, null, null));
                }
                else
                {
                    outList.Add(new ArchiveEntry(rest, false, entry.Length, entry.CompressedLength));
                }
            }
            return Result<IReadOnlyList<ArchiveEntry>>.Ok(outList);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<ArchiveEntry>>.Fail(FileErrorKind.Unsupported, $"无法读取压缩包: {ex.Message}", null, ex);
        }
    }

    /// 全量条目（树构建/解压用）。
    public Result<IReadOnlyList<ArchiveEntry>> ListAll(string zipPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            return Result<IReadOnlyList<ArchiveEntry>>.Ok(
                zip.Entries.Select(e => new ArchiveEntry(e.FullName, e.FullName.EndsWith('/'), e.Length, e.CompressedLength)).ToList());
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<ArchiveEntry>>.Fail(FileErrorKind.Unsupported, $"无法读取压缩包: {ex.Message}", null, ex);
        }
    }

    public Result<byte[]> ReadEntry(string zipPath, string entryName)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var entry = zip.GetEntry(entryName);
            if (entry is null)
                return Result<byte[]>.Fail(FileErrorKind.NotFound, "压缩包条目不存在");
            using var s = entry.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return Result<byte[]>.Ok(ms.ToArray());
        }
        catch (Exception ex)
        {
            return Result<byte[]>.Fail(FileErrorKind.IoError, ex.Message, null, ex);
        }
    }

    /// 解压（含冲突策略：Overwrite/Skip/KeepBoth；Ask 由调用方先问）。
    public Result<int> Extract(string zipPath, IEnumerable<string>? entryNames, string destDir, bool keepStructure, ConflictPolicy policy)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var names = entryNames?.ToHashSet(StringComparer.Ordinal);
            var count = 0;
            Directory.CreateDirectory(destDir);
            foreach (var entry in zip.Entries)
            {
                if (names is not null && !names.Contains(entry.FullName)) continue;
                if (entry.FullName.EndsWith('/')) continue;
                var rel = keepStructure ? entry.FullName : Path.GetFileName(entry.FullName);
                var target = Path.Combine(destDir, rel);
                var targetDir = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(targetDir)) Directory.CreateDirectory(targetDir);
                if (File.Exists(target))
                {
                    if (policy == ConflictPolicy.Skip) continue;
                    if (policy == ConflictPolicy.KeepBoth) target = Unique(target);
                }
                entry.ExtractToFile(target, overwrite: policy == ConflictPolicy.Overwrite);
                count++;
            }
            return Result<int>.Ok(count);
        }
        catch (Exception ex)
        {
            return Result<int>.Fail(FileErrorKind.IoError, ex.Message, null, ex);
        }
    }

    public Result Compress(IEnumerable<string> paths, string zipPath)
    {
        try
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            foreach (var path in paths)
            {
                if (File.Exists(path))
                {
                    zip.CreateEntryFromFile(path, Path.GetFileName(path), CompressionLevel.Optimal);
                }
                else if (Directory.Exists(path))
                {
                    var baseName = Path.GetFileName(path);
                    foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    {
                        var rel = Path.Combine(baseName, Path.GetRelativePath(path, file));
                        zip.CreateEntryFromFile(file, rel, CompressionLevel.Optimal);
                    }
                }
            }
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail(FileErrorKind.IoError, ex.Message, null, ex);
        }
    }

    private static string Unique(string target)
    {
        var dir = Path.GetDirectoryName(target) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(target);
        var ext = Path.GetExtension(target);
        for (var i = 2; i < 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
        return Path.Combine(dir, $"{name} (restored){ext}");
    }
}
