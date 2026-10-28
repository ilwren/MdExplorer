using Avalonia.Controls;
using Avalonia.Interactivity;
using MdExplorer.UI.ViewModels;

namespace MdExplorer.UI.Views;

public sealed partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
    }

    private void OnShortcutClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ShortcutRow row } &&
            TopLevel.GetTopLevel(this)?.DataContext is ShellViewModel shell)
        {
            shell.OpenTab(row.Destination);
        }
    }
}
