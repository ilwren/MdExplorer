using System.IO.Compression;
using System.Runtime.CompilerServices;
using MdExplorer.Domain;
using MdExplorer.Platform.Abstractions;

namespace MdExplorer.Infrastructure;

/// <summary>递归文件名搜索：深度优先产出、不穿越目录符号链接、无权限子树跳过。</summary>
public sealed class SearchService : ISearchService
{
    public async IAsyncEnumerable<FileItem> SearchByNameAsync(
        SearchSpec spec, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var query = spec.Query.Trim();
        if (query.Length == 0 || !Directory.Exists(spec.RootPath))
            yield break;

        var found = 0;
        var pending = new Stack<string>();
        pending.Push(spec.RootPath);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();

            List<string> entries;
            try
            {
                entries = await Task.Run(() => Directory.EnumerateFileSystemEntries(current).ToList(), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException) { continue; }
            catch (IOException) { continue; }

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileItem? item;
                try { item = LocalFileSystemService.StatPath(entry); }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }
                if (item is null) continue;

                var isHiddenDir = item.IsHidden && item.IsFolder;
                if (item.IsFolder && !item.IsSymlink && (spec.IncludeHidden || !isHiddenDir))
                    pending.Push(entry);

                if (!spec.IncludeHidden && item.IsHidden) continue;
                if (spec.Kinds is not null && !spec.Kinds.Contains(item.Kind)) continue;
                if (query.Length > 0 && !item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;

                yield return item;
                if (++found >= spec.MaxResults) yield break;
            }
        }
    }
}

/// <summary>ZIP 压缩包（System.IO.Compression 实现），含 Zip Slip 防护。</summary>
public sealed class ArchiveService : IArchiveService
{
    public bool CanOpen(string path) =>
        string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Path.GetExtension(path), ".jar", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Path.GetExtension(path), ".apk", StringComparison.OrdinalIgnoreCase);

    public Task<IReadOnlyList<ArchiveEntryItem>> ListEntriesAsync(
        string archivePath, int maxEntries, CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<ArchiveEntryItem>>(() =>
        {
            var items = new List<ArchiveEntryItem>();
            using var archive = ZipFile.OpenRead(archivePath);
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var isFolder = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');
                items.Add(new ArchiveEntryItem(entry.FullName, entry.Length, entry.CompressedLength, entry.LastWriteTime, isFolder));
                if (items.Count >= maxEntries) break;
            }
            return items;
        }, cancellationToken);

    public async Task<OperationOutcome> ExtractAsync(
        string archivePath, string destinationFolder,
        IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        var errors = new List<OperationError>();
        var produced = new List<string>();
        try
        {
            await Task.Run(() =>
            {
                Directory.CreateDirectory(destinationFolder);
                var destRoot = Path.GetFullPath(destinationFolder);
                using var archive = ZipFile.OpenRead(archivePath);
                var total = archive.Entries.Count;
                var done = 0;
                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    done++;
                    progress?.Report(new OperationProgress(total == 0 ? 1 : (double)done / total, entry.FullName, done, total));

                    var target = Path.GetFullPath(Path.Combine(destRoot, entry.FullName));
                    // Zip Slip：落点必须仍在目标子树内
                    var prefix = destRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                    if (!target.StartsWith(prefix, cmp) && !string.Equals(target, destRoot, cmp))
                    {
                        errors.Add(new OperationError(entry.FullName, "不安全的路径（越出解压目录），已跳过"));
                        continue;
                    }

                    try
                    {
                        if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                        {
                            Directory.CreateDirectory(target);
                        }
                        else
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                            entry.ExtractToFile(target, overwrite: true);
                        }
                        produced.Add(target);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                    {
                        errors.Add(new OperationError(entry.FullName, ex.Message));
                    }
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new OperationOutcome(0, errors, produced, Cancelled: true);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            errors.Add(new OperationError(Path.GetFileName(archivePath), $"无法打开压缩包：{ex.Message}"));
        }

        return new OperationOutcome(produced.Count, errors, produced);
    }

    public async Task<OperationOutcome> CompressToZipAsync(
        IReadOnlyList<string> sources, string destinationZipPath,
        IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        var errors = new List<OperationError>();
        try
        {
            await Task.Run(() =>
            {
                using var stream = new FileStream(destinationZipPath, FileMode.CreateNew, FileAccess.Write);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
                var done = 0;
                foreach (var source in sources)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        AddToArchive(archive, source, Path.GetFileName(source.TrimEnd('/', '\\')), cancellationToken);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        errors.Add(new OperationError(Path.GetFileName(source), ex.Message));
                    }
                    done++;
                    progress?.Report(new OperationProgress((double)done / Math.Max(1, sources.Count), Path.GetFileName(source), done, sources.Count));
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new OperationOutcome(0, errors, [], Cancelled: true);
        }

        return new OperationOutcome(1, errors, [destinationZipPath]);
    }

    private static void AddToArchive(ZipArchive archive, string sourcePath, string entryName, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (Directory.Exists(sourcePath))
        {
            var children = Directory.EnumerateFileSystemEntries(sourcePath).ToList();
            if (children.Count == 0)
                archive.CreateEntry(entryName.TrimEnd('/') + "/");
            foreach (var child in children)
                AddToArchive(archive, child, entryName.TrimEnd('/') + "/" + Path.GetFileName(child), ct);
            return;
        }

        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var input = File.OpenRead(sourcePath);
        using var output = entry.Open();
        input.CopyTo(output);
    }
}
