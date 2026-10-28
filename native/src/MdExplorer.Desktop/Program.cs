using Avalonia;
using MdExplorer.Desktop;

namespace MdExplorer.Desktop;

/// 桌面宿主入口（AOT 友好：无反射启动路径）。
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // CI/AOT 冒烟：在 Native AOT 产物内跑一次启动级自检（见 SelfTest.cs）。
        if (args.AsSpan().IndexOf("--selftest") >= 0)
            return SelfTest.Run();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
