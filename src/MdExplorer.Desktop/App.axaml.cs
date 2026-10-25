using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MdExplorer.Desktop.Views;
using Md3.Avalonia.Themes.Dynamic;
using Avalonia.Styling;

namespace MdExplorer.Desktop;

public sealed class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var mode = ThemePreferenceStore.Load();
        RequestedThemeVariant = mode switch
        {
            MdThemeMode.Light => ThemeVariant.Light,
            MdThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
        var options = new MdThemeOptions { ThemeMode = mode };
        var isDark = mode == MdThemeMode.Dark || (mode == MdThemeMode.System && ActualThemeVariant == ThemeVariant.Dark);
        MdThemeManager.Apply(this, options, isDark);
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
