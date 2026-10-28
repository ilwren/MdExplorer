using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace MdExplorer.Desktop;

public partial class ArchiveWorkbenchView : UserControl
{
    private readonly ArchiveWorkbenchViewModel _viewModel = new();

    public ArchiveWorkbenchView()
    {
        InitializeComponent();
        DataContext = _viewModel;
        SizeChanged += (_, args) => ApplyResponsiveLayout(args.NewSize.Width);
        AttachedToVisualTree += (_, _) => ApplyResponsiveLayout(Bounds.Width);
    }

    public event EventHandler? CloseRequested;
    public event EventHandler? ConflictPreviewRequested;

    private void ApplyResponsiveLayout(double width)
    {
        if (ArchiveColumns.ColumnDefinitions.Count < 3) return;
        var compact = width < 840;
        ArchiveColumns.ColumnDefinitions[0].Width = compact ? new GridLength(0) : new GridLength(190);
        ArchiveColumns.ColumnDefinitions[2].Width = compact ? new GridLength(0) : new GridLength(310);
        ArchiveTree.IsVisible = !compact;
        ArchivePreviewPanel.IsVisible = !compact;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
    private void OnConflictPreviewClick(object? sender, RoutedEventArgs e) => ConflictPreviewRequested?.Invoke(this, EventArgs.Empty);
    private void OnRootTreeClick(object? sender, RoutedEventArgs e) => _viewModel.NavigateTree("/");
    private void OnAssetsTreeClick(object? sender, RoutedEventArgs e) => _viewModel.NavigateTree("/assets");
    private void OnDocsTreeClick(object? sender, RoutedEventArgs e) => _viewModel.NavigateTree("/docs");
    private void OnArchiveBackClick(object? sender, RoutedEventArgs e) => _viewModel.NavigateTree("/");
    private void OnRefreshClick(object? sender, RoutedEventArgs e) => _viewModel.StatusText = "演示：虚拟目录已刷新；没有读取实际压缩包";
    private void OnExtractAllClick(object? sender, RoutedEventArgs e) => _viewModel.ExtractAll();
    private void OnExtractSelectedClick(object? sender, RoutedEventArgs e) => _viewModel.ExtractSelected();

    private void OnArchiveEntryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ArchiveEntry entry }) _viewModel.SelectEntry(entry);
    }

    private void OnArchiveEntryDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Button { Tag: ArchiveEntry { Kind: not "文件夹" } entry })
            _viewModel.SelectEntry(entry);
    }
}
