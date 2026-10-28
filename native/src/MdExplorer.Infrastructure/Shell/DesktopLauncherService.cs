using System.Diagnostics;
using MdExplorer.Domain.Errors;
using MdExplorer.Domain.Files;
using MdExplorer.Platform.Abstractions.Shell;

namespace MdExplorer.Infrastructure.Shell;

/// 桌面 Shell 打开（§5.3）：UseShellExecute；Android 平台层另实现。
public sealed class DesktopLauncherService : IFileLauncherService
{
    public ValueTask<Result> OpenAsync(FileLocator file, CancellationToken ct)
        => StartAsync(file.ToLocalPath(), useShell: true);

    public ValueTask<Result> OpenWithChooserAsync(FileLocator file, CancellationToken ct)
    {
        // 桌面端无统一「选择打开应用」API；以打开方式回退（能力位 HasRevealInExplorer 提示）。
        return StartAsync(file.ToLocalPath(), useShell: true);
    }

    public ValueTask<Result> RevealInSystemExplorerAsync(FileLocator file, CancellationToken ct)
    {
        var path = file.ToLocalPath();
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start(new ProcessStartInfo("open", $"-R \"{path}\"") { UseShellExecute = true });
            }
            else
            {
                var dir = File.Exists(path) ? Path.GetDirectoryName(path) : path;
                Process.Start(new ProcessStartInfo("xdg-open", dir ?? path) { UseShellExecute = true });
            }
            return ValueTask.FromResult(Result.Ok());
        }
        catch (Exception ex)
        {
            return ValueTask.FromResult(Result.Fail(FileErrorKind.Unsupported, ex.Message, file, ex));
        }
    }

    private static ValueTask<Result> StartAsync(string path, bool useShell)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = useShell });
            return ValueTask.FromResult(Result.Ok());
        }
        catch (Exception ex)
        {
            return ValueTask.FromResult(Result.Fail(FileErrorKind.Unsupported, ex.Message, FileLocator.FromPath(path), ex));
        }
    }
}

/// 桌面能力位（§5.2）。
public sealed class DesktopCapabilities : IPlatformCapabilities
{
    public bool HasTrash => true;
    public bool HasSystemFileClipboard => true;
    public bool HasRevealInExplorer => true;
    public bool SupportsMultiWindow => true;
    public bool TouchFirst => false;
}
