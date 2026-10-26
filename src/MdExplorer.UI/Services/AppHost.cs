using MdExplorer.Infrastructure;
using MdExplorer.Platform.Abstractions;
using MdExplorer.UI.ViewModels;

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
        IFolderWatchService FolderWatch);

    /// <param name="dataDirectory">设置/收藏落点；为空时按平台默认（LocalApplicationData/MdExplorer）。</param>
    /// <param name="startFolder">初始文件夹；为空时桌面=用户主目录，Android=个人目录。</param>
    /// <param name="launcher">打开方式服务；为空时使用桌面进程拉起实现（Android 宿主运行期由 MainView 重绑为 Launcher 实现）。</param>
    public static AppServices Build(string? dataDirectory = null, string? startFolder = null, IFileLauncherService? launcher = null)
    {
        var dataDir = dataDirectory ?? DefaultDataDirectory();
        Directory.CreateDirectory(dataDir);

        var fileSystem = new LocalFileSystemService();
        var knownFolders = new KnownFoldersService();
        var trash = new TrashService();
        var store = SettingsStore.LoadOrDefault(dataDir);

        var start = startFolder
                    ?? knownFolders.Get(KnownFolder.Home)
                    ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(start) || !Directory.Exists(start))
            start = Path.GetPathRoot(Environment.CurrentDirectory) ?? Environment.CurrentDirectory;

        var capabilities = new PlatformCapabilities(
            trash.CanMoveToTrash,
            trash.CanBrowseTrash,
            OperatingSystem.IsAndroid(),
            OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS());

        return new AppServices(
            fileSystem,
            new FileOperationService(),
            trash,
            new VolumeService(),
            knownFolders,
            new ArchiveService(),
            new SearchService(),
            store,
            launcher ?? new ProcessFileLauncher(),
            capabilities,
            dataDir,
            start,
            new FileInspectorService(),
            new FolderWatchService());
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
