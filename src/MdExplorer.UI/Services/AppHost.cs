using MdExplorer.Infrastructure;
using MdExplorer.Platform.Abstractions;
using MdExplorer.UI.ViewModels;
using MdExplorer.Infrastructure.Ai;

namespace MdExplorer.UI.Services;

/// <summary>组合根（开发文档 §4.3）：手工装配全部服务，宿主与截图工具共用同一入口。</summary>
public static class AppHost
{
    public sealed record AppServices(
        IFileSystemService FileSystem,
        IFileOperationService Operations,
        ITrashService Trash,
        IVolumeService Volumes,
        IKnownFoldersService KnownFolders,
        IArchiveService Archive,
        ISearchService Search,
        ISettingsStore SettingsStore,
        IFileLauncherService Launcher,
        PlatformCapabilities Capabilities,
        string DataDirectory,
        string StartFolder,
        IFileInspectorService Inspector,
        IFolderWatchService FolderWatch,
        DelegatingFileSystemService StorageFileSystem,
        IReadOnlyList<AuthorizedRoot> PersistedRoots,
        ISystemClipboardService SystemClipboard,
        IFileNameIndexService SearchIndex,
        IStorageInsightsService StorageInsights,
        ITextFileService TextFiles,
        ISystemNotificationService SystemNotifications,
        IMediaPlaybackService MediaPlayback,
        AiModelCatalog AiModels);

    /// <summary>
    /// 平台内嵌播放后端的注入点。Android 宿主在 Activity 创建时注册 MediaPlayer 实现；
    /// 未注册时（桌面、测试）退回明确自述“无后端”的实现，UI 据此只给系统打开入口。
    /// </summary>
    public static Func<IMediaPlaybackService>? MediaPlaybackFactory { get; set; }

    private const string DefaultMediaDiagnostic =
        "桌面端暂无内嵌解码后端：Windows 需 Media Foundation、Linux 需系统 FFmpeg，接入前不做静默降级。可用系统应用打开。";

    /// <param name="dataDirectory">设置/收藏落点；为空时按平台默认（LocalApplicationData/MdExplorer）。</param>
    /// <param name="startFolder">初始文件夹；为空时桌面=用户主目录，Android=个人目录。</param>
    /// <param name="launcher">打开方式服务；为空时使用桌面进程拉起实现（Android 宿主运行期由 MainView 重绑为 Launcher 实现）。</param>
    public static AppServices Build(
        string? dataDirectory = null,
        string? startFolder = null,
        IFileLauncherService? launcher = null,
        ISystemClipboardService? systemClipboard = null,
        ISystemNotificationService? systemNotifications = null)
    {
        var dataDir = dataDirectory ?? DefaultDataDirectory();
        Directory.CreateDirectory(dataDir);

        // 双形态委托：本地路径转发本地实现；content:// 走 StorageProvider（Android SAF，视图挂载后注入）
        var fileSystem = new DelegatingFileSystemService(new LocalFileSystemService(), new StorageItemRegistry());
        var operations = new DelegatingFileOperationService(new FileOperationService(), fileSystem);
        var knownFolders = new KnownFoldersService();
        // Android 没有可统一浏览/还原的系统回收站：改用应用数据目录内的配额回收站，
        // 同一服务同时覆盖应用专属目录与 SAF 授权树；桌面继续走系统回收站。
        var store = SettingsStore.LoadOrDefault(dataDir);
        ITrashService trash = OperatingSystem.IsAndroid()
            ? new AppTrashService(
                dataDir, fileSystem,
                quotaBytes: store.Settings.AppTrashQuotaBytes,
                retentionDays: store.Settings.AppTrashRetentionDays)
            : new TrashService();
        var searchIndex = new FileNameIndexService(dataDir, store);
        var search = new IndexedSearchService(searchIndex, new SearchService(), store);

        var start = startFolder
                    ?? knownFolders.Get(KnownFolder.Home)
                    ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(start) || !Directory.Exists(start))
            start = Path.GetPathRoot(Environment.CurrentDirectory) ?? Environment.CurrentDirectory;

        var capabilities = new PlatformCapabilities(
            trash.CanMoveToTrash,
            trash.CanBrowseTrash,
            OperatingSystem.IsAndroid(),
            OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(),
            UsesAppTrash: trash is AppTrashService);

        return new AppServices(
            fileSystem,
            operations,
            trash,
            new VolumeService(),
            knownFolders,
            new ArchiveService(),
            search,
            store,
            launcher ?? new ProcessFileLauncher(),
            capabilities,
            dataDir,
            start,
            new FileInspectorService(),
            new FolderWatchService(),
            fileSystem,
            AuthorizedRootsStore.Load(dataDir),
            systemClipboard ?? new AvaloniaSystemClipboardService(),
            searchIndex,
            new StorageInsightsService(),
            new DelegatingTextFileService(new LocalTextFileService(), new StorageTextFileService(fileSystem)),
            systemNotifications ?? new MdSystemNotificationServiceAdapter(),
            MediaPlaybackFactory?.Invoke() ?? new UnsupportedMediaPlaybackService(DefaultMediaDiagnostic),
            // 端侧 AI 的模型目录：只扫描文件，不初始化任何推理运行时。
            new AiModelCatalog(AiModelCatalog.ResolveDirectory(dataDir)));
    }

    public static ShellViewModel CreateShell(AppServices services) => new(services);

    private static string DefaultDataDirectory()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(baseDir))
            baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(baseDir))
            baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        return Path.Combine(baseDir, "MdExplorer");
    }
}
