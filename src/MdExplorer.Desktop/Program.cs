using Avalonia;

namespace MdExplorer.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<MdExplorer.UI.App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
