using Avalonia;
using MdExplorer.Desktop;

namespace MdExplorer.Desktop;

/// 桌面宿主入口（AOT 友好：无反射启动路径）。
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
        => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
