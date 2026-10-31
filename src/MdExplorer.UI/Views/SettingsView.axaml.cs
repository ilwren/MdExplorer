using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using MdExplorer.UI.Services;
using MdExplorer.UI.ViewModels;

namespace MdExplorer.UI.Views;

public partial class SettingsView : UserControl
{
    private bool? _isWideLayout;

    public SettingsView()
    {
        InitializeComponent();
        foreach (var (name, seed) in ThemeService.SeedPresets)
        {
            var swatch = new Border
            {
                Width = 44,
                Height = 44,
                CornerRadius = new Avalonia.CornerRadius(22),
                Margin = new Avalonia.Thickness(0, 0, 10, 10),
                Background = new SolidColorBrush(Color.Parse(seed)),
                Tag = seed,
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new TextBlock
                {
                    Text = name,
                    Foreground = Brushes.White,
                    FontSize = 13,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                },
            };
            swatch.Tapped += (_, _) =>
            {
                if (DataContext is ShellViewModel shell)
                    shell.SetSeedColorCommand.Execute(seed);
            };
            SeedPanel.Children.Add(swatch);
        }
    }

    private void OnSettingsColumnsSizeChanged(object? sender, SizeChangedEventArgs e) =>
        UpdateSettingsLayout(e.NewSize.Width);

    private void UpdateSettingsLayout(double width)
    {
        if (width <= 0 || _isWideLayout == (width >= 860))
            return;

        var wide = width >= 860;
        _isWideLayout = wide;
        SettingsColumns.ColumnDefinitions[1].Width = wide
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(0);
        Grid.SetColumn(SettingsSecondaryColumn, wide ? 1 : 0);
        Grid.SetRow(SettingsSecondaryColumn, wide ? 0 : 1);
    }

    private async void OnAuthorizedRootClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: AuthorizedRootModel root } && DataContext is ShellViewModel shell)
            await shell.OpenAuthorizedRootAsync(root);
    }

    private async void OnReauthorizeRootClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: AuthorizedRootModel root } && DataContext is ShellViewModel shell)
            await shell.ReauthorizeAuthorizedRootAsync(root);
        e.Handled = true;
    }

    private async void OnRemoveAuthorizedRootClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: AuthorizedRootModel root } && DataContext is ShellViewModel shell)
            await shell.RemoveAuthorizedRootAsync(root);
        e.Handled = true;
    }

    private async void OnLicensesClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not IDialogs dialogs) return;
        await dialogs.ShowPropertiesAsync("开源许可证",
            "本应用基于以下开源组件构建：\n\n" +
            "· Avalonia 12（MIT）\n" +
            "· Md3.Avalonia（Apache-2.0），含 Material Color Utilities（Apache-2.0）\n" +
            "· Material Symbols Rounded 字体（Apache-2.0）\n" +
            "· CommunityToolkit.Mvvm（MIT）\n" +
            "· .NET 10 运行时与类库（MIT）\n" +
            "· Inter 字体（SIL OFL 1.1）\n\n" +
            "完整文本见随包附带的 THIRD-PARTY-NOTICES 与各组件仓库。本应用自身以 Apache-2.0 许可发布。");
    }
}
