using MdExplorer.Domain;

namespace MdExplorer.Platform.Abstractions;

/// <summary>文件夹枚举与文件读写端口。v0.1 以本地路径定位符为实现（见 FileLocator 注释）。</summary>
public interface IFileSystemService
{
    IAsyncEnumerable<FileItem> ListAsync(string folderPath, bool includeHidden, CancellationToken cancellationToken);
    Task<FileItem?> StatAsync(string path, CancellationToken cancellationToken);
    Task<string> CreateFolderAsync(string parentPath, string name, CancellationToken cancellationToken);
    Task<string> CreateFileAsync(string parentPath, string name, CancellationToken cancellationToken);
    Task<string> RenameAsync(string sourcePath, string newName, CancellationToken cancellationToken);
    bool Exists(string path);
}

/// <summary>复制/移动/删除执行器：批处理、进度、冲突策略、部分失败明细（DEVELOPMENT.md §5.5 的 v0.1 串行版）。</summary>
public interface IFileOperationService
{
    Task<OperationOutcome> CopyAsync(
        IReadOnlyList<string> sources, string destinationFolder, ConflictPolicy policy,
        IProgress<OperationProgress>? progress, CancellationToken cancellationToken);

    Task<OperationOutcome> MoveAsync(
        IReadOnlyList<string> sources, string destinationFolder, ConflictPolicy policy,
        IProgress<OperationProgress>? progress, CancellationToken cancellationToken);

    Task<OperationOutcome> DeletePermanentlyAsync(
        IReadOnlyList<string> sources,
        IProgress<OperationProgress>? progress, CancellationToken cancellationToken);
}

/// <summary>文件夹统计结果（无权访问的子树被跳过时 IsPartial=true，数值为下限；不穿目录符号链接）。</summary>
public sealed record FolderSizeResult(long TotalBytes, long FileCount, long FolderCount, bool IsPartial);

/// <summary>文件检视端口：文件夹大小统计与流式校验和（大文件不整载内存）。</summary>
public interface IFileInspectorService
{
    Task<FolderSizeResult> GetFolderSizeAsync(string path, CancellationToken cancellationToken);

    /// <param name="algorithm">"SHA256" | "MD5"（不区分大小写）。</param>
    /// <param name="progress">0..1 完成度。</param>
    /// <returns>小写十六进制摘要。</returns>
    Task<string> ComputeHashAsync(string path, string algorithm, IProgress<double>? progress, CancellationToken cancellationToken);
}

/// <summary>
/// 当前文件夹外部变更监视端口：Watch 返回的句柄 Dispose 即停。变更回调已做防抖合并，
/// 且可能在任意线程触发（UI 层需自行切回 UI 线程）。监视不可用（特殊文件系统）时静默降级为不回调。
/// </summary>
public interface IFolderWatchService
{
    IDisposable Watch(string folderPath, Action changed);
}

/// <summary>回收站端口。各平台能力不一致，调用前查能力位。</summary>
public interface ITrashService
{
    bool CanMoveToTrash { get; }
    bool CanBrowseTrash { get; }
    Task MoveToTrashAsync(string path, CancellationToken cancellationToken);
    Task<IReadOnlyList<TrashedItem>> ListAsync(CancellationToken cancellationToken);
    Task RestoreAsync(string trashItemId, CancellationToken cancellationToken);
    Task DeletePermanentlyAsync(string trashItemId, CancellationToken cancellationToken);
}

public sealed record TrashedItem(string Id, string OriginalName, string OriginalPath, DateTimeOffset DeletedAt, bool IsFolder);

/// <summary>存储卷发现。</summary>
public interface IVolumeService
{
    Task<IReadOnlyList<StorageVolume>> GetVolumesAsync(CancellationToken cancellationToken);
}

public enum KnownFolder { Home, Documents, Downloads, Pictures, Music, Videos, Desktop }

/// <summary>已知目录位置（平台语义差异在此收敛，UI 永不自行拼路径）。</summary>
public interface IKnownFoldersService
{
    string? Get(KnownFolder folder);
}

/// <summary>用系统默认方式打开文件 / 在系统管理器中显示（实现位于 UI 层，走 Avalonia Launcher）。</summary>
public interface IFileLauncherService
{
    Task<bool> OpenAsync(string path, CancellationToken cancellationToken);
}

/// <summary>ZIP 压缩包端口（v0.1 以 System.IO.Compression 实现，格式扩展见选型表）。</summary>
public interface IArchiveService
{
    bool CanOpen(string path);
    Task<IReadOnlyList<ArchiveEntryItem>> ListEntriesAsync(string archivePath, int maxEntries, CancellationToken cancellationToken);
    Task<OperationOutcome> ExtractAsync(string archivePath, string destinationFolder,
        IProgress<OperationProgress>? progress, CancellationToken cancellationToken);
    Task<OperationOutcome> CompressToZipAsync(IReadOnlyList<string> sources, string destinationZipPath,
        IProgress<OperationProgress>? progress, CancellationToken cancellationToken);
}

public sealed record ArchiveEntryItem(string FullName, long Length, long CompressedLength, DateTimeOffset LastWriteTime, bool IsFolder);

/// <summary>递归文件名搜索（流式产出）。</summary>
public interface ISearchService
{
    IAsyncEnumerable<FileItem> SearchByNameAsync(SearchSpec spec, CancellationToken cancellationToken);
}

/// <summary>应用设置模型（JSON 持久化，源生成序列化）。</summary>
public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public string ThemeMode { get; set; } = "System";       // System | Light | Dark
    public string SeedColor { get; set; } = "#006A6A";      // MD3 HCT 种子色
    public string ViewMode { get; set; } = "List";          // List | Grid
    public string SortField { get; set; } = "Name";
    public string SortDirection { get; set; } = "Ascending";
    public bool ShowHidden { get; set; }
    public bool FoldersFirst { get; set; } = true;
    public bool ShowPreviewPane { get; set; } = true;
    public bool ConfirmPermanentDelete { get; set; } = true;
    public bool WatchFolderChanges { get; set; } = true;   // 外部增删改时自动刷新当前文件夹
}

/// <summary>设置与收藏的持久化端口。</summary>
public interface ISettingsStore
{
    AppSettings Settings { get; }
    IReadOnlyList<string> Favorites { get; }
    Task SaveSettingsAsync(CancellationToken cancellationToken);
    Task SetFavoriteAsync(string path, bool favorite, CancellationToken cancellationToken);
    bool IsFavorite(string path);
}

/// <summary>平台能力位（UI 据此显隐入口，不猜系统）。</summary>
public sealed record PlatformCapabilities(
    bool CanMoveToTrash,
    bool CanBrowseTrash,
    bool IsAndroid,
    bool IsDesktop);
