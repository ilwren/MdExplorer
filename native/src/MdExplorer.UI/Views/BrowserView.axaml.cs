using Avalonia.Controls;
using Avalonia.Input;
using MdExplorer.UI.ViewModels;

namespace MdExplorer.UI.Views;

public sealed partial class BrowserView : UserControl
{
    public BrowserView()
    {
        InitializeComponent();
    }

    private EntryViewModel? Row(object? sender)
        => (sender as Control)?.Tag as EntryViewModel ?? (sender as Control)?.DataContext as EntryViewModel;

    private void OnEntryPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not BrowserViewModel vm || Row(sender) is not { } entry)
            return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            vm.ToggleSelect(entry);
            e.Handled = true;
        }
        else
        {
            foreach (var x in vm.Entries.Where(x => x.IsSelected))
                x.IsSelected = false;
            entry.IsSelected = true;
            vm.SetPreview(entry);
        }
    }

    private void OnEntryDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not BrowserViewModel vm || Row(sender) is not { } entry)
            return;
        _ = vm.OpenAsync(entry);
        e.Handled = true;
    }
}
