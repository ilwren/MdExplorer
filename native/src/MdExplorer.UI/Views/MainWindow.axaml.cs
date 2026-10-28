using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using MdExplorer.UI.ViewModels;
using MdExplorer.UI.Services;

namespace MdExplorer.UI.Views;

public sealed partial class MainWindow : Window
{
    private readonly ViewFactory _views;
    private ShellViewModel? _shell;

    public MainWindow()
    {
        InitializeComponent();
        _views = UiBoot.CreateViewFactory();
        SizeChanged += (_, _) => _shell?.SetWidth(Bounds.Width);
        DataContextChanged += (_, _) => AttachShell();
    }

    public MainWindow(ViewFactory views) : this()
    {
        // 保留参数构造（组合根注入测试工厂）；默认构造已建工厂。
        _views = views;
    }

    private void AttachShell()
    {
        if (DataContext is not ShellViewModel shell)
            return;
        _shell = shell;
        shell.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.CurrentPage) && shell.CurrentPage is { } page)
            {
                var view = _views.Create(page);
                view.DataContext = page;
                PageHost.Content = view;
            }
            else if (e.PropertyName == nameof(ShellViewModel.ThemeMode))
            {
                Avalonia.Application.Current!.RequestedThemeVariant =
                    shell.ThemeMode == "light" ? ThemeVariant.Light : ThemeVariant.Dark;
            }
        };
        shell.SetWidth(Bounds.Width);
        Avalonia.Application.Current!.RequestedThemeVariant =
            shell.ThemeMode == "light" ? ThemeVariant.Light : ThemeVariant.Dark;
        if (shell.CurrentPage is { } initial)
        {
            var view = _views.Create(initial);
            view.DataContext = initial;
            PageHost.Content = view;
        }
    }

    private void OnTabClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: BrowserViewModel tab } && _shell is not null)
        {
            _shell.ActiveTab = tab;
            _shell.ShowFiles();
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (_shell is null) return;
        if (e.Key == Key.F5 && _shell.ActiveTab is { } tab)
        {
            _ = tab.RefreshCommand.ExecuteAsync(null);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.T && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _shell.NewTabCommand.Execute(null);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.W && e.KeyModifiers.HasFlag(KeyModifiers.Control) && _shell.ActiveTab is { } current)
        {
            _shell.CloseTabCommand.Execute(current);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _shell.OpenSearchCenterCommand.Execute(null);
            e.Handled = true;
        }
    }
}
