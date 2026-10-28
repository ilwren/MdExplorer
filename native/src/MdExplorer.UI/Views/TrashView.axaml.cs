using Avalonia.Controls;
using Avalonia.Interactivity;
using MdExplorer.UI.ViewModels;

namespace MdExplorer.UI.Views;

public sealed partial class TrashView : UserControl
{
    public TrashView()
    {
        InitializeComponent();
    }

    private void OnRestoreClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TrashRow row } && DataContext is TrashViewModel vm)
            vm.RestoreCommand.Execute(row);
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TrashRow row } && DataContext is TrashViewModel vm)
            vm.DeletePermanentlyCommand.Execute(row);
    }

    private void OnEmptyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TrashViewModel vm)
            vm.EmptyTrashCommand.Execute(null);
    }
}
