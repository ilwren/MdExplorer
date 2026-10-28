using System.Threading.Channels;
using MdExplorer.Domain.Errors;
using MdExplorer.Domain.Files;
using MdExplorer.Domain.Operations;
using MdExplorer.Platform.Abstractions.FileSystem;
using MdExplorer.Platform.Abstractions.Shell;

namespace MdExplorer.Application.Operations;

public enum OperationState
{
    Pending,
    Running,
    Done,
    Failed,
    Cancelled,
    Skipped,
}

public sealed record OperationItem(string Name, OperationState State, string? Detail = null);

/// 任务句柄：进度、条目明细、完成源（§5.5）。
public sealed class OperationHandle
{
    private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Guid Id { get; } = Guid.NewGuid();
    public required string Label { get; init; }
    public FileJobKind Kind { get; init; }
    public FileOperationJob? Job { get; set; }
    public OperationState State { get; set; } = OperationState.Pending;
    public double Fraction { get; set; }
    public List<OperationItem> Items { get; } = [];
    public CancellationTokenSource Cancellation { get; } = new();
    public Task Completion => _tcs.Task;

    public void Finish(OperationState state)
    {
        State = state;
        _tcs.TrySetResult();
    }
}

public interface IFileOperationQueue
{
    OperationHandle Enqueue(FileOperationJob job);
    IReadOnlyList<OperationHandle> ActiveJobs { get; }
    event EventHandler? JobsChanged;
}

/// UI 永远不直接调用写盘 API：一切写操作经此串行执行（§5.5）。
/// 冲突询问经 IDialogService（可对「其余全部」一次决策）。
public sealed class FileOperationQueue : IFileOperationQueue
{
    private readonly IFileSystemService _files;
    private readonly IPathService _paths;
    private readonly ITrashService _trash;
    private readonly UI.IDialogService _dialogs;
    private readonly Platform.Abstractions.Archives.IArchiveService? _archives;
    private readonly object _gate = new();
    private readonly List<OperationHandle> _jobs = [];
    private readonly Channel<OperationHandle> _channel = Channel.CreateUnbounded<OperationHandle>();
    private ConflictPolicy? _stickyPolicy;

    public event EventHandler? JobsChanged;

    public FileOperationQueue(
        IFileSystemService files,
        IPathService paths,
        ITrashService trash,
        UI.IDialogService dialogs,
        Platform.Abstractions.Archives.IArchiveService? archives = null)
    {
        _files = files;
        _paths = paths;
        _trash = trash;
        _dialogs = dialogs;
        _archives = archives;
        _ = DrainAsync();
    }

    public IReadOnlyList<OperationHandle> ActiveJobs
    {
        get
        {
            lock (_gate)
                return _jobs.Where(j => j.State is OperationState.Pending or OperationState.Running).ToList();
        }
    }

    public OperationHandle Enqueue(FileOperationJob job)
    {
        var label = job.Kind switch
        {
            FileJobKind.Copy => $"复制 {job.Sources.Count} 项",
            FileJobKind.Move => $"移动 {job.Sources.Count} 项",
            FileJobKind.DeleteToTrash => $"删除到回收站 {job.Sources.Count} 项",
            FileJobKind.DeletePermanently => $"永久删除 {job.Sources.Count} 项",
            FileJobKind.Extract => $"解压 {job.Sources.Count} 项",
            FileJobKind.Compress => $"压缩 {job.Sources.Count} 项",
            FileJobKind.Rename => "重命名",
            FileJobKind.CreateFolder => "新建文件夹",
            FileJobKind.Save => "保存",
            _ => "文件操作",
        };
        var handle = new OperationHandle { Label = label, Kind = job.Kind, Job = job };
        lock (_gate) _jobs.Add(handle);
        JobsChanged?.Invoke(this, EventArgs.Empty);
        _channel.Writer.TryWrite(handle);
        return handle;
    }

    private async Task DrainAsync()
    {
        await foreach (var handle in _channel.Reader.ReadAllAsync())
        {
            if (handle.Cancellation.IsCancellationRequested)
            {
                handle.Finish(OperationState.Cancelled);
                JobsChanged?.Invoke(this, EventArgs.Empty);
                continue;
            }
            handle.State = OperationState.Running;
            JobsChanged?.Invoke(this, EventArgs.Empty);
            try
            {
                await RunAsync(handle).ConfigureAwait(false);
                handle.Finish(handle.Items.Any(i => i.State == OperationState.Failed)
                    ? OperationState.Failed
                    : OperationState.Done);
            }
            catch (OperationCanceledException)
            {
                handle.Finish(OperationState.Cancelled);
            }
            catch (Exception ex)
            {
                handle.Items.Add(new OperationItem("操作", OperationState.Failed, ex.Message));
                handle.Finish(OperationState.Failed);
            }
            JobsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task RunAsync(OperationHandle handle)
    {
        var job = handle.Job!;
        _stickyPolicy = null;
        switch (job.Kind)
        {
            case FileJobKind.Copy:
            case FileJobKind.Move:
                await RunTransferAsync(handle, job).ConfigureAwait(false);
                break;
            case FileJobKind.DeleteToTrash:
                foreach (var src in job.Sources)
                {
                    var r = await _trash.MoveToTrashAsync(src, handle.Cancellation.Token).ConfigureAwait(false);
                    handle.Items.Add(new OperationItem(NameOf(src),
                        r.IsSuccess ? OperationState.Done : OperationState.Failed, r.Error?.Message));
                }
                break;
            case FileJobKind.DeletePermanently:
                foreach (var src in job.Sources)
                {
                    var r = await _files.DeletePermanentlyAsync(src, handle.Cancellation.Token).ConfigureAwait(false);
                    handle.Items.Add(new OperationItem(NameOf(src),
                        r.IsSuccess ? OperationState.Done : OperationState.Failed, r.Error?.Message));
                }
                break;
            case FileJobKind.CreateFolder when job.DestinationFolder is { } parent:
            {
                var created = await _files.CreateFolderAsync(parent, job.NewName ?? "新建文件夹", handle.Cancellation.Token).ConfigureAwait(false);
                handle.Items.Add(new OperationItem(job.NewName ?? "新建文件夹",
                    created.IsSuccess ? OperationState.Done : OperationState.Failed, created.Error?.Message));
                break;
            }
            case FileJobKind.Rename when job.Sources.Count > 0 && job.NewName is { } newName:
            {
                var renamed = await _files.RenameAsync(job.Sources[0], newName, handle.Cancellation.Token).ConfigureAwait(false);
                handle.Items.Add(new OperationItem(newName,
                    renamed.IsSuccess ? OperationState.Done : OperationState.Failed, renamed.Error?.Message));
                break;
            }
            case FileJobKind.Extract when _archives is not null && job.Sources.Count > 0:
            {
                var zip = job.Sources[0].IsLocalPath ? job.Sources[0].ToLocalPath() : job.Sources[0].Value;
                var destDir = job.DestinationFolder is { } d && d.IsLocalPath ? d.ToLocalPath()
                    : Path.GetDirectoryName(zip) ?? ".";
                var policy = job.Conflict == ConflictPolicy.Ask ? ConflictPolicy.KeepBoth : job.Conflict;
                var r = _archives.Extract(zip, null, destDir, true, policy);
                handle.Items.Add(new OperationItem(Path.GetFileName(zip),
                    r.IsSuccess ? OperationState.Done : OperationState.Failed, r.Error?.Message));
                break;
            }
            case FileJobKind.Compress when _archives is not null:
            {
                var destDir = job.DestinationFolder is { } d2 && d2.IsLocalPath ? d2.ToLocalPath()
                    : Environment.CurrentDirectory;
                var zipPath = Path.Combine(destDir, job.NewName ?? "archive.zip");
                var r = _archives.Compress(job.Sources.Select(x => x.IsLocalPath ? x.ToLocalPath() : x.Value), zipPath);
                handle.Items.Add(new OperationItem(Path.GetFileName(zipPath),
                    r.IsSuccess ? OperationState.Done : OperationState.Failed, r.Error?.Message));
                break;
            }
            default:
                handle.Items.Add(new OperationItem("操作", OperationState.Failed, "不支持的任务类型"));
                break;
        }
    }

    private async Task RunTransferAsync(OperationHandle handle, FileOperationJob job)
    {
        if (job.DestinationFolder is not { } dest) return;
        for (var i = 0; i < job.Sources.Count; i++)
        {
            handle.Cancellation.Token.ThrowIfCancellationRequested();
            var src = job.Sources[i];
            var name = NameOf(src);
            handle.Fraction = (double)i / job.Sources.Count;
            JobsChanged?.Invoke(this, EventArgs.Empty);

            var policy = _stickyPolicy ?? job.Conflict;
            var target = _paths.Combine(dest, name);
            var existing = await _files.ExistsAsync(target, handle.Cancellation.Token).ConfigureAwait(false);

            if (existing && policy == ConflictPolicy.Ask)
            {
                var srcItem = await _files.StatAsync(src, handle.Cancellation.Token).ConfigureAwait(false);
                var destItem = await _files.StatAsync(target, handle.Cancellation.Token).ConfigureAwait(false);
                var decision = await _dialogs.ResolveConflictAsync(
                    new ConflictContext(
                        name,
                        srcItem.Value?.Length,
                        srcItem.Value?.LastWriteTime ?? default,
                        srcItem.Value?.Kind ?? FileKind.Unknown,
                        destItem.Value?.Length,
                        destItem.Value?.LastWriteTime),
                    handle.Cancellation.Token).ConfigureAwait(false);
                if (decision.ApplyToAll)
                {
                    _stickyPolicy = decision.Resolution switch
                    {
                        ConflictResolution.Overwrite => ConflictPolicy.Overwrite,
                        ConflictResolution.Skip => ConflictPolicy.Skip,
                        _ => ConflictPolicy.KeepBoth,
                    };
                }
                policy = decision.Resolution switch
                {
                    ConflictResolution.Overwrite => ConflictPolicy.Overwrite,
                    ConflictResolution.Skip => ConflictPolicy.Skip,
                    _ => ConflictPolicy.KeepBoth,
                };
            }

            if (existing && policy == ConflictPolicy.Skip)
            {
                handle.Items.Add(new OperationItem(name, OperationState.Skipped, "已跳过"));
                continue;
            }

            if (existing && policy == ConflictPolicy.KeepBoth)
            {
                var unique = await UniqueNameAsync(dest, name).ConfigureAwait(false);
                await MoveOrCopyAsync(handle, src, dest, unique, job.Kind == FileJobKind.Move).ConfigureAwait(false);
                continue;
            }

            await MoveOrCopyAsync(handle, src, dest, name, job.Kind == FileJobKind.Move).ConfigureAwait(false);
        }
    }

    private async Task MoveOrCopyAsync(OperationHandle handle, FileLocator src, FileLocator dest, string targetName, bool move)
    {
        Result<FileLocator> r = move
            ? await _files.MoveAsync(src, dest, handle.Cancellation.Token).ConfigureAwait(false)
            : await _files.CopyAsync(src, dest, handle.Cancellation.Token).ConfigureAwait(false);
        handle.Items.Add(new OperationItem(targetName,
            r.IsSuccess ? OperationState.Done : OperationState.Failed, r.Error?.Message));
    }

    private async Task<string> UniqueNameAsync(FileLocator dest, string name)
    {
        for (var i = 2; i < 1000; i++)
        {
            var candidate = UniqueName(name, i);
            if (!await _files.ExistsAsync(_paths.Combine(dest, candidate), CancellationToken.None).ConfigureAwait(false))
                return candidate;
        }
        return UniqueName(name, 2);
    }

    /// `name.ext` → `name (2).ext` …
    public static string UniqueName(string name, int index)
    {
        var ext = Path.GetExtension(name);
        var stem = Path.GetFileNameWithoutExtension(name);
        return $"{stem} ({index}){ext}";
    }

    private static string NameOf(FileLocator locator)
        => Path.GetFileName(locator.IsLocalPath ? locator.ToLocalPath() : locator.Value);
}
