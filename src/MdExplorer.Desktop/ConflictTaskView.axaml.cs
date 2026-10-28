using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MdExplorer.Desktop;

public partial class ConflictTaskView : UserControl
{
    private readonly ConflictTaskViewModel _viewModel = new();

    public ConflictTaskView()
    {
        InitializeComponent();
        DataContext = _viewModel;
        SizeChanged += (_, args) => ApplyResponsiveLayout(args.NewSize.Width);
        AttachedToVisualTree += (_, _) => ApplyResponsiveLayout(Bounds.Width);
    }

    public event EventHandler? CloseRequested;
    public event EventHandler? ArchivePreviewRequested;

    private void ApplyResponsiveLayout(double width)
    {
        if (ConflictColumns.ColumnDefinitions.Count < 2) return;
        var compact = width < 840;
        ConflictColumns.ColumnDefinitions[0].Width = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(1.2, GridUnitType.Star);
        ConflictColumns.ColumnDefinitions[1].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        TaskColumn.IsVisible = !compact;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
    private void OnArchivePreviewClick(object? sender, RoutedEventArgs e) => ArchivePreviewRequested?.Invoke(this, EventArgs.Empty);
    private void OnKeepBothClick(object? sender, RoutedEventArgs e) => _viewModel.ChooseResolution("保留两者");
    private void OnReplaceClick(object? sender, RoutedEventArgs e) => _viewModel.ChooseResolution("替换目标");
    private void OnSkipClick(object? sender, RoutedEventArgs e) => _viewModel.ChooseResolution("跳过此项");
    private void OnResolveClick(object? sender, RoutedEventArgs e) => _viewModel.Resolve();
    private void OnRetryClick(object? sender, RoutedEventArgs e) => _viewModel.Retry();
    private void OnPauseClick(object? sender, RoutedEventArgs e) => _viewModel.ToggleTaskRun();
    private void OnCancelClick(object? sender, RoutedEventArgs e) => _viewModel.CancelTask();
}
