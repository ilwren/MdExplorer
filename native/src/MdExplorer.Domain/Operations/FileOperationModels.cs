using MdExplorer.Domain.Files;

namespace MdExplorer.Domain.Operations;

/// 冲突策略（§5.5）：KeepBoth → 「name (2).ext」自动改名。
public enum ConflictPolicy
{
    Ask,
    Overwrite,
    Skip,
    KeepBoth,
}

public enum FileJobKind
{
    Copy,
    Move,
    DeleteToTrash,
    DeletePermanently,
    Compress,
    Extract,
    CreateFolder,
    Rename,
    Save,
}

public sealed record ArchiveOptions(string ArchiveName, string? Password = null);

/// 一切写操作构造 FileOperationJob 交 IFileOperationQueue 串行执行（§5.5）。
public sealed record FileOperationJob(
    FileJobKind Kind,
    IReadOnlyList<FileLocator> Sources,
    FileLocator? DestinationFolder,
    ConflictPolicy Conflict = ConflictPolicy.Ask,
    string? NewName = null,
    ArchiveOptions? Archive = null);

public sealed record OperationProgress(
    Guid JobId,
    double Fraction,
    string CurrentItemName,
    long BytesDone,
    long? BytesTotal,
    int ItemsDone,
    int? ItemsTotal);

public enum ConflictResolution
{
    Overwrite,
    Skip,
    KeepBoth,
}

/// 冲突询问上下文（对齐 UI 冲突对话框：名称/大小/时间并排对比）。
public sealed record ConflictContext(
    string Name,
    long? SourceSize,
    DateTimeOffset SourceTime,
    FileKind SourceKind,
    long? DestSize,
    DateTimeOffset? DestTime);

public sealed record ConflictDecision(ConflictResolution Resolution, bool ApplyToAll);
