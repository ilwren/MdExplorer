using Avalonia;
#if WINDOWS
using MdExplorer.UI.Services;
#endif

namespace MdExplorer.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
#if WINDOWS
        // 必须在创建窗口前固定进程身份；快捷方式注册延迟到用户首次真正发送通知时。
        WindowsNotificationRegistration.TrySetProcessAppUserModelId();
        MdSystemNotificationServiceAdapter.PlatformPreparation = cancellationToken =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            WindowsNotificationRegistration.RegisterShortcutForCurrentUser();
            return Task.CompletedTask;
        };
#endif
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<MdExplorer.UI.App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
