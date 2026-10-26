using System.Text;
using Avalonia.Platform.Storage;
using MdExplorer.Domain;
using MdExplorer.Infrastructure;
using MdExplorer.UI.Services;
using Xunit;

namespace MdExplorer.Tests;

// StorageProvider 接口的测试替身（模拟 Android SAF 授权树）：无模拟器环境下验证委托层行为。
internal sealed class FakeStorageFile(string name, Uri path, FakeStorageFolder? parent) : IStorageFile
{
    public byte[] Content = [];
    public FakeStorageFolder? Parent { get; set; } = parent;
    public string Name { get; } = name;
    public Uri Path { get; set; } = path;
    public bool CanBookmark => true;

    public Task<StorageItemProperties> GetBasicPropertiesAsync() =>
        Task.FromResult(new StorageItemProperties((ulong)Content.Length, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));

    public Task<string?> SaveBookmarkAsync() => Task.FromResult<string?>("bm:" + Path);
    public Task<IStorageFolder?> GetParentAsync() => Task.FromResult<IStorageFolder?>(Parent);

    public Task DeleteAsync()
    {
        Parent?.Children.Remove(this);
        return Task.CompletedTask;
    }

    public Task<IStorageItem?> MoveAsync(IStorageFolder destination)
    {
        if (destination is FakeStorageFolder dest)
        {
            Parent?.Children.Remove(this);
            Parent = dest;
            dest.Children.Add(this);
            Path = new Uri(dest.Path + "/" + Name);
        }
        return Task.FromResult<IStorageItem?>(this);
    }

    public Task<Stream> OpenReadAsync() => Task.FromResult<Stream>(new MemoryStream(Content, writable: false));
    public Task<Stream> OpenWriteAsync() => Task.FromResult<Stream>(new CommitStream(this));
    public void Dispose() { }

    private sealed class CommitStream(FakeStorageFile owner) : MemoryStream
    {
        protected override void Dispose(bool disposing)
        {
            if (disposing) owner.Content = ToArray();
            base.Dispose(disposing);
        }
    }
}

internal sealed class FakeStorageFolder(string name, Uri path, FakeStorageFolder? parent) : IStorageFolder
{
    public List<IStorageItem> Children { get; } = [];
    public FakeStorageFolder? Parent { get; set; } = parent;
    public string Name { get; } = name;
    public Uri Path { get; set; } = path;
    public bool CanBookmark => true;

    public FakeStorageFile AddFile(string name, string content)
    {
        var file = new FakeStorageFile(name, new Uri(Path + "/" + name), this) { Content = Encoding.UTF8.GetBytes(content) };
        Children.Add(file);
        return file;
    }

    public FakeStorageFolder AddFolder(string name)
    {
        var folder = new FakeStorageFolder(name, new Uri(Path + "/" + name), this);
        Children.Add(folder);
        return folder;
    }

    public Task<StorageItemProperties> GetBasicPropertiesAsync() =>
        Task.FromResult(new StorageItemProperties(null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));

    public Task<string?> SaveBookmarkAsync() => Task.FromResult<string?>("bm:" + Path);
    public Task<IStorageFolder?> GetParentAsync() => Task.FromResult<IStorageFolder?>(Parent);

    public Task DeleteAsync()
    {
        Parent?.Children.Remove(this);
        return Task.CompletedTask;
    }

    public Task<IStorageItem?> MoveAsync(IStorageFolder destination)
    {
        if (destination is FakeStorageFolder dest)
        {
            Parent?.Children.Remove(this);
            Parent = dest;
            dest.Children.Add(this);
            Path = new Uri(dest.Path + "/" + Name);
        }
        return Task.FromResult<IStorageItem?>(this);
    }

    public async IAsyncEnumerable<IStorageItem> GetItemsAsync()
    {
        foreach (var child in Children.ToArray())
        {
            yield return child;
            await Task.Yield();
        }
    }

    public Task<IStorageFolder?> GetFolderAsync(string name) =>
        Task.FromResult<IStorageFolder?>(Children.OfType<FakeStorageFolder>().FirstOrDefault(f => f.Name == name));

    public Task<IStorageFile?> GetFileAsync(string name) =>
        Task.FromResult<IStorageFile?>(Children.OfType<FakeStorageFile>().FirstOrDefault(f => f.Name == name));

    public Task<IStorageFile?> CreateFileAsync(string name) =>
        Task.FromResult<IStorageFile?>(AddFile(name, ""));

    public Task<IStorageFolder?> CreateFolderAsync(string name) =>
        Task.FromResult<IStorageFolder?>(AddFolder(name));

    public void Dispose() { }
}

public sealed class StorageDelegationTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("mdexplorer-saf-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { }
    }

    private sealed record Tree(
        DelegatingFileSystemService Fs,
        DelegatingFileOperationService Ops,
        FakeStorageFolder Root,
        string RootKey,
        string AKey,
        string SubKey);

    private static Tree BuildTree()
    {
        var fs = new DelegatingFileSystemService(new LocalFileSystemService(), new StorageItemRegistry());
        var ops = new DelegatingFileOperationService(new FileOperationService(), fs);
        var root = new FakeStorageFolder("授权夹", new Uri("content://tree/root"), null);
        var a = root.AddFile("a.txt", "hello");
        root.AddFile(".hidden", "x");
        var sub = root.AddFolder("sub");
        sub.AddFile("b.txt", "world!");
        var rootKey = fs.RegisterRoot(root);
        return new Tree(fs, ops, root, rootKey, a.Path.ToString(), sub.Path.ToString());
    }

    private static async Task<List<FileItem>> ListAllAsync(DelegatingFileSystemService fs, string key, bool hidden = false)
    {
        var items = new List<FileItem>();
        await foreach (var item in fs.ListAsync(key, hidden, CancellationToken.None))
            items.Add(item);
        return items;
    }

    private static async Task<string> ReadTextAsync(DelegatingFileSystemService fs, string key)
    {
        await using var stream = await fs.OpenReadAsync(key, CancellationToken.None);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    [Fact]
    public async Task ListsStorageTreeWithKindsSizesAndHiddenFilter()
    {
        var t = BuildTree();
        var items = await ListAllAsync(t.Fs, t.RootKey);
        Assert.Equal(2, items.Count); // .hidden 被过滤
        var a = Assert.Single(items, i => i.Name == "a.txt");
        Assert.Equal(FileKind.Text, a.Kind);
        Assert.Equal(5, a.Length);
        Assert.True(a.Locator.IsStorage);
        Assert.Equal(FileKind.Folder, Assert.Single(items, i => i.Name == "sub").Kind);
        Assert.Equal(3, (await ListAllAsync(t.Fs, t.RootKey, hidden: true)).Count);
        Assert.Equal("hello", await ReadTextAsync(t.Fs, a.FullPath));
    }

    [Fact]
    public async Task BreadcrumbsAndParentsFollowRegistryChain()
    {
        var t = BuildTree();
        _ = await ListAllAsync(t.Fs, t.RootKey);
        Assert.Null(t.Fs.GetParentPath(t.RootKey));
        Assert.Equal(t.RootKey, t.Fs.GetParentPath(t.SubKey));
        Assert.Equal(new[] { "授权夹", "sub" }, t.Fs.GetBreadcrumbs(t.SubKey).Select(s => s.Name).ToArray());
    }

    [Fact]
    public async Task CopiesStorageFolderToLocalRecursively()
    {
        var t = BuildTree();
        _ = await ListAllAsync(t.Fs, t.RootKey);
        var outcome = await t.Ops.CopyAsync([t.SubKey], _tempDir, ConflictPolicy.Skip, null, CancellationToken.None);
        Assert.True(outcome.Success, string.Join(";", outcome.Errors.Select(e => e.Message)));
        Assert.Equal("world!", await File.ReadAllTextAsync(Path.Combine(_tempDir, "sub", "b.txt")));
    }

    [Fact]
    public async Task CopiesLocalFileIntoStorage()
    {
        var t = BuildTree();
        var localFile = Path.Combine(_tempDir, "note.md");
        await File.WriteAllTextAsync(localFile, "# 标题");
        var outcome = await t.Ops.CopyAsync([localFile], t.RootKey, ConflictPolicy.Skip, null, CancellationToken.None);
        Assert.True(outcome.Success, string.Join(";", outcome.Errors.Select(e => e.Message)));
        var produced = Assert.Single(outcome.ProducedPaths);
        Assert.Equal("# 标题", await ReadTextAsync(t.Fs, produced));
        var stat = await t.Fs.StatAsync(produced, CancellationToken.None);
        Assert.Equal("note.md", stat?.Name);
    }

    [Fact]
    public async Task MovesWithinStorageTreeUsingProviderMove()
    {
        var t = BuildTree();
        _ = await ListAllAsync(t.Fs, t.RootKey);
        var outcome = await t.Ops.MoveAsync([t.AKey], t.SubKey, ConflictPolicy.Skip, null, CancellationToken.None);
        Assert.True(outcome.Success, string.Join(";", outcome.Errors.Select(e => e.Message)));
        Assert.DoesNotContain(t.Root.Children, c => c.Name == "a.txt");
        var produced = Assert.Single(outcome.ProducedPaths);
        Assert.Equal(t.SubKey, t.Fs.GetParentPath(produced));
        Assert.Equal("hello", await ReadTextAsync(t.Fs, produced));
    }

    [Fact]
    public async Task RenamesStorageFileByCopyAndDelete()
    {
        var t = BuildTree();
        _ = await ListAllAsync(t.Fs, t.RootKey);
        var newKey = await t.Fs.RenameAsync(t.AKey, "a2.txt", CancellationToken.None);
        Assert.Equal("hello", await ReadTextAsync(t.Fs, newKey));
        Assert.False(t.Fs.Exists(t.AKey));
        Assert.Contains(await ListAllAsync(t.Fs, t.RootKey), i => i.Name == "a2.txt");
    }

    [Fact]
    public async Task DeletesStorageFolderRecursively()
    {
        var t = BuildTree();
        _ = await ListAllAsync(t.Fs, t.RootKey);
        var outcome = await t.Ops.DeletePermanentlyAsync([t.SubKey], null, CancellationToken.None);
        Assert.True(outcome.Success, string.Join(";", outcome.Errors.Select(e => e.Message)));
        Assert.DoesNotContain(t.Root.Children, c => c.Name == "sub");
    }

    [Fact]
    public async Task LocalPathsPassThroughUnchanged()
    {
        var t = BuildTree();
        var localFile = Path.Combine(_tempDir, "x.txt");
        await File.WriteAllTextAsync(localFile, "l");
        var items = await ListAllAsync(t.Fs, _tempDir);
        Assert.Contains(items, i => i.Name == "x.txt" && !i.Locator.IsStorage);
        Assert.Equal(_tempDir, t.Fs.GetParentPath(localFile));
        Assert.False(FileLocator.IsStorageUri(localFile));
        Assert.True(FileLocator.IsStorageUri(t.RootKey));
    }
}
