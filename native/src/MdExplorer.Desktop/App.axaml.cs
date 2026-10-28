using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MdExplorer.UI.ViewModels;
using MdExplorer.UI.Views;

namespace MdExplorer.Desktop;

public sealed class App : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var shell = CompositionRoot.Build(out var notify);
            notify.OnShow = msg =>
            {
                shell.SnackbarText = msg;
                shell.IsSnackbarOpen = true;
            };
            var window = new MainWindow { DataContext = shell };
            shell.Services.Dialogs.Attach(window);
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
