using MdExplorer.Application.Operations;
using MdExplorer.Application.Search;
using MdExplorer.Application.Settings;
using MdExplorer.Infrastructure.Archives;
using MdExplorer.Infrastructure.FileSystem;
using MdExplorer.Infrastructure.Persistence;
using MdExplorer.Infrastructure.PreviewProviders;
using MdExplorer.Infrastructure.Shell;
using MdExplorer.Infrastructure.Thumbnails;
using MdExplorer.Platform.Abstractions.Archives;
using MdExplorer.Platform.Abstractions.FileSystem;
using MdExplorer.Platform.Abstractions.Preview;
using MdExplorer.Platform.Abstractions.Shell;
using MdExplorer.UI.Services;
using MdExplorer.UI.ViewModels;

namespace MdExplorer.Desktop;

/// 每宿主唯一组合根（§4.3）：显式注册，禁扫描；桌面按能力位选实现。
public static class CompositionRoot
{
    public static ShellViewModel Build(out NotificationService notify)
    {
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MdExplorer");
        Directory.CreateDirectory(dataDir);

        // —— 基础设施 ——
        IMimeTypeService mime = new MimeTypeService();
        IPathService paths = new LocalPathService();
        IFileSystemService files = new LocalFileSystemService(mime);
        IKnownFoldersService known = new LocalKnownFoldersService();
        IVolumeService volumes = new LocalVolumeService();
        IArchiveService zip = new ZipArchiveService();
        ITrashService trash = new AppTrashService(files, mime, dataDir);
        IFileLauncherService launcher = new DesktopLauncherService();
        IAppClipboardService clipboard = new AppClipboardService();
        IPreviewService preview = new PreviewProviderChain(
        [
            new TextPreviewProvider(),
            new ArchivePreviewProvider(zip),
            new ImagePreviewProvider(),
            new MediaPreviewProvider(),
            new MetadataPreviewProvider(),
        ]);
        IThumbnailService thumbs = new SkiaThumbnailService();
        ISettingsService settings = new JsonSettingsStore(dataDir);
        ISearchIndexService index = new SqliteSearchIndexService(Path.Combine(dataDir, "index"));
        ISearchService search = new TraversalSearchService(files);

        // —— UI 端口 ——
        var dialogs = new DialogService();
        var notifications = new NotificationService();
        notify = notifications;

        // —— 写操作全局串行队列（§5.5） ——
        var queue = new FileOperationQueue(files, paths, trash, dialogs, zip);

        var services = new UiServices(
            files, paths, mime, known, volumes, trash, launcher, clipboard,
            queue, preview, thumbs, zip, search, index, settings, dialogs, notifications);

        return new ShellViewModel(services);
    }
}
