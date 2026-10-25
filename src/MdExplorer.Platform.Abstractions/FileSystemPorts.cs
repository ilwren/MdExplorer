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
