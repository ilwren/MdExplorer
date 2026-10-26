using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Platform.Storage;
using MdExplorer.Application;
using MdExplorer.Domain;
using MdExplorer.Platform.Abstractions;

namespace MdExplorer.UI.Services;

/// <summary>
/// 存储提供者条目注册表：URI → IStorageItem 与父链。
/// 浏览器以 URI 字符串为定位键（FileLocator 存储形态），条目实例由此按会话缓存与复用（不 Dispose）。
/// </summary>
public sealed class StorageItemRegistry
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, IStorageItem> _items = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _parents = new(StringComparer.Ordinal);

    public static string KeyOf(IStorageItem item) => item.Path.ToString();

    public void Register(IStorageItem item, string? parentKey)
    {
        var key = KeyOf(item);
        lock (_gate)
        {
            _items[key] = item;
            if (parentKey is not null)
                _parents[key] = parentKey;
        }
    }

    public IStorageItem? Resolve(string key)
    {
        lock (_gate)
            return _items.GetValueOrDefault(key);
    }

    public string? ParentOf(string key)
    {
        lock (_gate)
            return _parents.GetValueOrDefault(key);
    }

    public bool Contains(string key)
    {
        lock (_gate)
            return _items.ContainsKey(key);
    }

    public void Forget(string key)
    {
        lock (_gate)
        {
            _items.Remove(key);
            _parents.Remove(key);
        }
    }
}

/// <summary>
/// 双形态委托文件系统：本地路径转发 <see cref="IFileSystemService"/> 本地实现；
/// content:// 定位串走 Avalonia StorageProvider（Android SAF 授权树）。StorageProvider 在视图挂载后注入。
/// </summary>
public sealed class DelegatingFileSystemService(IFileSystemService local, StorageItemRegistry registry) : IFileSystemService
{
    public IStorageProvider? StorageProvider { get; set; }

    internal StorageItemRegistry Registry => registry;

    internal static bool IsStorage(string key) => FileLocator.IsStorageUri(key);

    /// <summary>根文件夹（选择器/书签打开结果）登记入册，返回其定位串。</summary>
    public string RegisterRoot(IStorageFolder folder)
    {
        registry.Register(folder, null);
        return StorageItemRegistry.KeyOf(folder);
    }

    internal IStorageItem? ResolveItem(string key) => registry.Resolve(key);

    internal IStorageFolder? ResolveFolder(string key) => registry.Resolve(key) as IStorageFolder;

    public async IAsyncEnumerable<FileItem> ListAsync(
        string folderPath, bool includeHidden, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!IsStorage(folderPath))
        {
            await foreach (var item in local.ListAsync(folderPath, includeHidden, cancellationToken).ConfigureAwait(false))
                yield return item;
            yield break;
        }

        var folder = ResolveFolder(folderPath)
                     ?? throw new DirectoryNotFoundException("授权文件夹不可用，可能授权已被系统撤销。");
        await foreach (var child in folder.GetItemsAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            registry.Register(child, folderPath);
            var hidden = child.Name.StartsWith('.');
            if (hidden && !includeHidden) continue;
            yield return await ToFileItemAsync(child).ConfigureAwait(false);
        }
    }

    public async Task<FileItem?> StatAsync(string path, CancellationToken cancellationToken)
    {
        if (!IsStorage(path))
            return await local.StatAsync(path, cancellationToken).ConfigureAwait(false);
        var item = ResolveItem(path);
        return item is null ? null : await ToFileItemAsync(item).ConfigureAwait(false);
    }

    private static async Task<FileItem> ToFileItemAsync(IStorageItem item)
    {
        StorageItemProperties? props = null;
        try { props = await item.GetBasicPropertiesAsync().ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { }

        var isFolder = item is IStorageFolder;
        return new FileItem(
            FileLocator.FromStorage(StorageItemRegistry.KeyOf(item)),
            item.Name,
            isFolder ? FileKind.Folder : FileKindClassifier.Classify(item.Name),
            props?.Size is { } size ? (long)Math.Min(size, (ulong)long.MaxValue) : null,
            props?.DateModified ?? props?.DateCreated ?? DateTimeOffset.MinValue,
            item.Name.StartsWith('.'),
            IsSymlink: false,
            isFolder ? null : Path.GetExtension(item.Name));
    }

    public async Task<string> CreateFolderAsync(string parentPath, string name, CancellationToken cancellationToken)
    {
        if (!IsStorage(parentPath))
            return await local.CreateFolderAsync(parentPath, name, cancellationToken).ConfigureAwait(false);
        var parent = ResolveFolder(parentPath) ?? throw new DirectoryNotFoundException("授权文件夹不可用。");
        var created = await parent.CreateFolderAsync(name).ConfigureAwait(false)
                      ?? await parent.GetFolderAsync(name).ConfigureAwait(false)
                      ?? throw new IOException($"无法创建文件夹：{name}");
        registry.Register(created, parentPath);
        return StorageItemRegistry.KeyOf(created);
    }

    public async Task<string> CreateFileAsync(string parentPath, string name, CancellationToken cancellationToken)
    {
        if (!IsStorage(parentPath))
            return await local.CreateFileAsync(parentPath, name, cancellationToken).ConfigureAwait(false);
        var parent = ResolveFolder(parentPath) ?? throw new DirectoryNotFoundException("授权文件夹不可用。");
        var created = await parent.CreateFileAsync(name).ConfigureAwait(false)
                      ?? throw new IOException($"无法创建文件：{name}");
        registry.Register(created, parentPath);
        return StorageItemRegistry.KeyOf(created);
    }

    public async Task<string> RenameAsync(string sourcePath, string newName, CancellationToken cancellationToken)
    {
        if (!IsStorage(sourcePath))
            return await local.RenameAsync(sourcePath, newName, cancellationToken).ConfigureAwait(false);

        // StorageProvider 无原生改名：同夹复制（夹则递归）后删源。v1 已知代价，文档在册。
        var item = ResolveItem(sourcePath) ?? throw new FileNotFoundException("项目不可用。", sourcePath);
        var parentKey = registry.ParentOf(sourcePath) ?? throw new IOException("无法确定父文件夹。");
        var produced = await CopyItemAsync(sourcePath, parentKey, newName, cancellationToken).ConfigureAwait(false);
        await DeleteKeyAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        registry.Forget(sourcePath);
        return produced;
    }

    public bool Exists(string path) => IsStorage(path) ? registry.Contains(path) : local.Exists(path);

    public async Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!IsStorage(path))
            return await local.OpenReadAsync(path, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var file = ResolveItem(path) as IStorageFile ?? throw new FileNotFoundException("文件不可用。", path);
        return await file.OpenReadAsync().ConfigureAwait(false);
    }

    public string? GetParentPath(string path) =>
        IsStorage(path) ? registry.ParentOf(path) : local.GetParentPath(path);

    public IReadOnlyList<PathSegment> GetBreadcrumbs(string path)
    {
        if (!IsStorage(path))
            return local.GetBreadcrumbs(path);
        var segments = new List<PathSegment>();
        string? current = path;
        while (current is not null)
        {
            var item = ResolveItem(current);
            segments.Add(new PathSegment(item?.Name ?? current, current));
            current = registry.ParentOf(current);
        }
        segments.Reverse();
        return segments;
    }

    // ---------- 供委托操作服务复用的原语（双形态统一） ----------

    internal async Task<bool> ChildExistsAsync(string folderKey, string name)
    {
        if (!IsStorage(folderKey))
        {
            var full = Path.Combine(folderKey, name);
            return File.Exists(full) || Directory.Exists(full);
        }
        var folder = ResolveFolder(folderKey);
        if (folder is null) return false;
        if (await folder.GetFileAsync(name).ConfigureAwait(false) is { } file)
        {
            registry.Register(file, folderKey);
            return true;
        }
        if (await folder.GetFolderAsync(name).ConfigureAwait(false) is { } sub)
        {
            registry.Register(sub, folderKey);
            return true;
        }
        return false;
    }

    internal async Task<string?> ChildKeyAsync(string folderKey, string name)
    {
        if (!IsStorage(folderKey))
        {
            var full = Path.Combine(folderKey, name);
            return File.Exists(full) || Directory.Exists(full) ? full : null;
        }
        var folder = ResolveFolder(folderKey);
        if (folder is null) return null;
        IStorageItem? item = await folder.GetFileAsync(name).ConfigureAwait(false)
                             ?? (IStorageItem?)await folder.GetFolderAsync(name).ConfigureAwait(false);
        if (item is null) return null;
        registry.Register(item, folderKey);
        return StorageItemRegistry.KeyOf(item);
    }

    internal async Task<string> CreateFolderDirectAsync(string parentKey, string name, CancellationToken ct) =>
        IsStorage(parentKey)
            ? await CreateFolderAsync(parentKey, name, ct).ConfigureAwait(false)
            : await local.CreateFolderAsync(parentKey, name, ct).ConfigureAwait(false);

    /// <summary>在目标夹中建文件并打开写流（存储形态下先删后建以保证截断语义）。返回定位串。</summary>
    internal async Task<(Stream Stream, string Key)> OpenWriteInFolderAsync(string folderKey, string name, CancellationToken ct)
    {
        if (!IsStorage(folderKey))
        {
            var full = Path.Combine(folderKey, name);
            return (new FileStream(full, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true), full);
        }
        var folder = ResolveFolder(folderKey) ?? throw new DirectoryNotFoundException("授权文件夹不可用。");
        if (await folder.GetFileAsync(name).ConfigureAwait(false) is { } existing)
            await existing.DeleteAsync().ConfigureAwait(false);
        var file = await folder.CreateFileAsync(name).ConfigureAwait(false)
                   ?? throw new IOException($"无法创建文件：{name}");
        registry.Register(file, folderKey);
        return (await file.OpenWriteAsync().ConfigureAwait(false), StorageItemRegistry.KeyOf(file));
    }

    internal bool IsFolderKey(string key) =>
        IsStorage(key) ? ResolveItem(key) is IStorageFolder : Directory.Exists(key);

    internal string DisplayNameOf(string key) =>
        IsStorage(key) ? ResolveItem(key)?.Name ?? key : Path.GetFileName(key.TrimEnd(Path.DirectorySeparatorChar));

    /// <summary>把任一形态的条目流式复制到目标夹并命名（夹递归）。返回产出定位串。</summary>
    internal async Task<string> CopyItemAsync(string sourceKey, string destFolderKey, string destName, CancellationToken ct)
    {
        if (!IsFolderKey(sourceKey))
        {
            await using var source = await OpenReadAsync(sourceKey, ct).ConfigureAwait(false);
            var (stream, producedKey) = await OpenWriteInFolderAsync(destFolderKey, destName, ct).ConfigureAwait(false);
            await using (stream)
                await source.CopyToAsync(stream, ct).ConfigureAwait(false);
            return producedKey;
        }

        var newFolderKey = await CreateFolderDirectAsync(destFolderKey, destName, ct).ConfigureAwait(false);
        foreach (var (childKey, childName) in await ListChildrenKeysAsync(sourceKey, ct).ConfigureAwait(false))
            await CopyItemAsync(childKey, newFolderKey, childName, ct).ConfigureAwait(false);
        return newFolderKey;
    }

    internal async Task<IReadOnlyList<(string Key, string Name)>> ListChildrenKeysAsync(string folderKey, CancellationToken ct)
    {
        var result = new List<(string, string)>();
        if (!IsStorage(folderKey))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(folderKey))
            {
                ct.ThrowIfCancellationRequested();
                result.Add((entry, Path.GetFileName(entry)));
            }
            return result;
        }
        var folder = ResolveFolder(folderKey) ?? throw new DirectoryNotFoundException("授权文件夹不可用。");
        await foreach (var child in folder.GetItemsAsync().ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            registry.Register(child, folderKey);
            result.Add((StorageItemRegistry.KeyOf(child), child.Name));
        }
        return result;
    }

    /// <summary>递归删除任一形态条目（存储文件夹先清子项再删本体，规避提供者差异）。</summary>
    internal async Task DeleteKeyAsync(string key, CancellationToken ct)
    {
        if (!IsStorage(key))
        {
            await Task.Run(() =>
            {
                if (Directory.Exists(key)) Directory.Delete(key, recursive: true);
                else if (File.Exists(key)) File.Delete(key);
            }, ct).ConfigureAwait(false);
            return;
        }
        var item = ResolveItem(key) ?? throw new FileNotFoundException("项目不可用。", key);
        if (item is IStorageFolder)
        {
            foreach (var (childKey, _) in await ListChildrenKeysAsync(key, ct).ConfigureAwait(false))
                await DeleteKeyAsync(childKey, ct).ConfigureAwait(false);
        }
        await item.DeleteAsync().ConfigureAwait(false);
        registry.Forget(key);
    }
}

/// <summary>
/// 双形态委托操作服务：整批与目标皆本地时原样转发既有引擎（桌面零行为变化）；
/// 含存储形态时走统一流复制引擎（移动同树优先 MoveAsync，冲突策略与 §5.5 对齐）。
/// </summary>
public sealed class DelegatingFileOperationService(
    IFileOperationService local,
    DelegatingFileSystemService fileSystem) : IFileOperationService
{
    public Task<OperationOutcome> CopyAsync(
        IReadOnlyList<string> sources, string destinationFolder, ConflictPolicy policy,
        IProgress<OperationProgress>? progress, CancellationToken cancellationToken) =>
        AllLocal(sources, destinationFolder)
            ? local.CopyAsync(sources, destinationFolder, policy, progress, cancellationToken)
            : TransferAsync(sources, destinationFolder, policy, isMove: false, progress, cancellationToken);

    public Task<OperationOutcome> MoveAsync(
        IReadOnlyList<string> sources, string destinationFolder, ConflictPolicy policy,
        IProgress<OperationProgress>? progress, CancellationToken cancellationToken) =>
        AllLocal(sources, destinationFolder)
            ? local.MoveAsync(sources, destinationFolder, policy, progress, cancellationToken)
            : TransferAsync(sources, destinationFolder, policy, isMove: true, progress, cancellationToken);

    public async Task<OperationOutcome> DeletePermanentlyAsync(
        IReadOnlyList<string> sources,
        IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        if (sources.All(s => !DelegatingFileSystemService.IsStorage(s)))
            return await local.DeletePermanentlyAsync(sources, progress, cancellationToken).ConfigureAwait(false);

        var errors = new List<OperationError>();
        var done = 0;
        foreach (var source in sources)
        {
            if (cancellationToken.IsCancellationRequested)
                return new OperationOutcome(done, errors, [], Cancelled: true);
            try
            {
                await fileSystem.DeleteKeyAsync(source, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return new OperationOutcome(done, errors, [], Cancelled: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
            {
                errors.Add(new OperationError(fileSystem.DisplayNameOf(source), ex.Message));
            }
            done++;
            progress?.Report(new OperationProgress((double)done / Math.Max(1, sources.Count),
                fileSystem.DisplayNameOf(source), done, sources.Count));
        }
        return new OperationOutcome(done - errors.Count, errors, []);
    }

    private static bool AllLocal(IReadOnlyList<string> sources, string destinationFolder) =>
        !DelegatingFileSystemService.IsStorage(destinationFolder)
        && sources.All(s => !DelegatingFileSystemService.IsStorage(s));

    private async Task<OperationOutcome> TransferAsync(
        IReadOnlyList<string> sources, string destinationFolder, ConflictPolicy policy, bool isMove,
        IProgress<OperationProgress>? progress, CancellationToken ct)
    {
        var errors = new List<OperationError>();
        var produced = new List<string>();
        var done = 0;
        foreach (var source in sources)
        {
            if (ct.IsCancellationRequested)
                return new OperationOutcome(done, errors, produced, Cancelled: true);
            var name = fileSystem.DisplayNameOf(source);
            try
            {
                var parent = fileSystem.GetParentPath(source);
                if (isMove && parent is not null && string.Equals(parent, destinationFolder, StringComparison.Ordinal))
                {
                    done++;
                    continue; // 移到自身目录视为无操作（与本地引擎一致）
                }

                var targetName = name;
                var exists = await fileSystem.ChildExistsAsync(destinationFolder, targetName).ConfigureAwait(false);
                var sameFolder = parent is not null && string.Equals(parent, destinationFolder, StringComparison.Ordinal);
                if (exists && (policy == ConflictPolicy.KeepBoth || (sameFolder && policy == ConflictPolicy.Overwrite)))
                {
                    var i = 1;
                    do { targetName = PathText.KeepBothName(name, i++); }
                    while (await fileSystem.ChildExistsAsync(destinationFolder, targetName).ConfigureAwait(false));
                    exists = false;
                }
                if (exists && policy == ConflictPolicy.Skip)
                {
                    done++;
                    continue;
                }
                if (exists && policy == ConflictPolicy.Overwrite)
                {
                    if (await fileSystem.ChildKeyAsync(destinationFolder, targetName).ConfigureAwait(false) is { } victim)
                        await fileSystem.DeleteKeyAsync(victim, ct).ConfigureAwait(false);
                }

                string producedKey;
                var bothStorage = DelegatingFileSystemService.IsStorage(source)
                                  && DelegatingFileSystemService.IsStorage(destinationFolder);
                if (isMove && bothStorage && targetName == name
                    && fileSystem.ResolveItem(source) is { } item
                    && fileSystem.ResolveFolder(destinationFolder) is { } destFolder)
                {
                    var moved = await item.MoveAsync(destFolder).ConfigureAwait(false) ?? item;
                    fileSystem.Registry.Forget(source);
                    fileSystem.Registry.Register(moved, destinationFolder);
                    producedKey = StorageItemRegistry.KeyOf(moved);
                }
                else
                {
                    producedKey = await fileSystem.CopyItemAsync(source, destinationFolder, targetName, ct).ConfigureAwait(false);
                    if (isMove)
                        await fileSystem.DeleteKeyAsync(source, ct).ConfigureAwait(false);
                }
                produced.Add(producedKey);
            }
            catch (OperationCanceledException)
            {
                return new OperationOutcome(done, errors, produced, Cancelled: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
            {
                errors.Add(new OperationError(fileSystem.DisplayNameOf(source), ex.Message));
            }
            done++;
            progress?.Report(new OperationProgress((double)done / Math.Max(1, sources.Count),
                fileSystem.DisplayNameOf(source), done, sources.Count));
        }
        return new OperationOutcome(done - errors.Count, errors, produced);
    }
}

/// <summary>已授权文件夹（书签）的持久化（storage-roots.json，源生成 JSON 与设置同风格）。</summary>
public sealed record AuthorizedRoot(string Name, string Bookmark);

public static class AuthorizedRootsStore
{
    public static IReadOnlyList<AuthorizedRoot> Load(string dataDirectory)
    {
        try
        {
            var path = Path.Combine(dataDirectory, "storage-roots.json");
            if (!File.Exists(path)) return [];
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize(json, AuthorizedRootsJsonContext.Default.ListAuthorizedRoot) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static async Task SaveAsync(string dataDirectory, IReadOnlyList<AuthorizedRoot> roots)
    {
        var path = Path.Combine(dataDirectory, "storage-roots.json");
        var temp = path + ".tmp";
        var json = JsonSerializer.Serialize(roots.ToList(), AuthorizedRootsJsonContext.Default.ListAuthorizedRoot);
        await File.WriteAllTextAsync(temp, json).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);
    }
}

[JsonSerializable(typeof(List<AuthorizedRoot>))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class AuthorizedRootsJsonContext : JsonSerializerContext { }
