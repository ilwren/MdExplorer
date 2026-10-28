using MdExplorer.Application.Operations;
using MdExplorer.Application.Search;
using MdExplorer.Application.Settings;
using MdExplorer.Application.UI;
using MdExplorer.Platform.Abstractions.Archives;
using MdExplorer.Platform.Abstractions.FileSystem;
using MdExplorer.Platform.Abstractions.Preview;
using MdExplorer.Platform.Abstractions.Shell;

namespace MdExplorer.UI.Services;

/// UI 服务包：组合根装配后注入各 ViewModel（§4.3 显式注册）。
public sealed record UiServices(
    IFileSystemService Fs,
    IPathService Path,
    IMimeTypeService Mime,
    IKnownFoldersService Known,
    IVolumeService Volumes,
    ITrashService Trash,
    IFileLauncherService Launcher,
    IAppClipboardService Clipboard,
    FileOperationQueue Queue,
    IPreviewService Preview,
    IThumbnailService Thumbs,
    IArchiveService Zip,
    ISearchService Search,
    ISearchIndexService Index,
    ISettingsService Settings,
    DialogService Dialogs,
    INotificationService Notify);
