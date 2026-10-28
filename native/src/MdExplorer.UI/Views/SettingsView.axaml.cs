using Avalonia.Controls;
using Avalonia.Controls.Selection;
using MdExplorer.UI.ViewModels;

namespace MdExplorer.UI.Views;

public sealed partial class SettingsView : UserControl
{
    private static readonly string[] Themes = ["dark", "light", "system"];
    private static readonly string[] ViewModes = ["grid", "list"];

    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Sync();
    }

    private void Sync()
    {
        if (DataContext is not SettingsViewModel vm)
            return;
        ThemeBox.ItemsSource = Themes;
        ViewBox.ItemsSource = ViewModes;
        ThemeBox.SelectedIndex = Array.IndexOf(Themes, vm.Settings.Theme);
        ViewBox.SelectedIndex = Array.IndexOf(ViewModes, vm.Settings.ViewMode);
    }

    private void OnThemeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm && ThemeBox.SelectedItem is string s)
            vm.Settings.Theme = s;
    }

    private void OnViewChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm && ViewBox.SelectedItem is string s)
            vm.Settings.ViewMode = s;
    }
}
