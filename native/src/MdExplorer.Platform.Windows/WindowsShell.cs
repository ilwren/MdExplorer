using MdExplorer.Domain.Errors;
using MdExplorer.Domain.Files;
using MdExplorer.Platform.Abstractions.Shell;

namespace MdExplorer.Platform.Windows;

/// Windows 分享/打开钩子（Shell 打开走 Infrastructure 的 DesktopLauncherService）。
public sealed class WindowsShareService : IShareService
{
    public ValueTask<Result> ShareAsync(IReadOnlyList<FileLocator> files, CancellationToken ct)
        => ValueTask.FromResult(Result.Fail(FileErrorKind.Unsupported, "桌面端无系统分享面板；请用「打开」或复制路径"));
}

/// Windows 能力位。回收站：组合根可注册 Infrastructure 的应用私有回收站，
/// 或后续接 Shell IFileOperation（Spike）。
public sealed class WindowsCapabilities : IPlatformCapabilities
{
    public bool HasTrash => true;
    public bool HasSystemFileClipboard => true;
    public bool HasRevealInExplorer => true;
    public bool SupportsMultiWindow => true;
    public bool TouchFirst => false;
}
