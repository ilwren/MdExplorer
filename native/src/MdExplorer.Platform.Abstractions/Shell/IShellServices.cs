using MdExplorer.Domain.Errors;
using MdExplorer.Domain.Files;

namespace MdExplorer.Platform.Abstractions.Shell;

public sealed record TrashedItem(
    string Id,
    FileItem Item,
    DateTimeOffset DeletedAt,
    string OriginalPath);

/// 回收站端口（§5.3）。能力不可用时 IsSupported=false，UI 改显「永久删除」。
public interface ITrashService
{
    bool IsSupported { get; }
    ValueTask<Result> MoveToTrashAsync(FileLocator item, CancellationToken ct);
    IAsyncEnumerable<TrashedItem> ListTrashAsync(CancellationToken ct);
    ValueTask<Result> RestoreAsync(string trashItemId, CancellationToken ct);
    ValueTask<Result> DeletePermanentlyAsync(FileLocator item, CancellationToken ct);
    ValueTask<Result> EmptyTrashAsync(CancellationToken ct);
}

public interface IFileLauncherService
{
    ValueTask<Result> OpenAsync(FileLocator file, CancellationToken ct);
    ValueTask<Result> OpenWithChooserAsync(FileLocator file, CancellationToken ct);
    ValueTask<Result> RevealInSystemExplorerAsync(FileLocator file, CancellationToken ct);
}

public interface IShareService
{
    ValueTask<Result> ShareAsync(IReadOnlyList<FileLocator> files, CancellationToken ct);
}

/// 能力位汇总：UI 据此显隐入口而非猜平台（§5.2）。
public interface IPlatformCapabilities
{
    bool HasTrash { get; }
    bool HasSystemFileClipboard { get; }
    bool HasRevealInExplorer { get; }
    bool SupportsMultiWindow { get; }
    bool TouchFirst { get; }
}
