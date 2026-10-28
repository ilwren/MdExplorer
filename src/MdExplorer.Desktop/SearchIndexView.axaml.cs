using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MdExplorer.Desktop;

public partial class SearchIndexView : UserControl
{
    private readonly SearchIndexViewModel _viewModel = new();

    public SearchIndexView()
    {
        InitializeComponent();
        DataContext = _viewModel;
        SizeChanged += (_, args) => ApplyResponsiveLayout(args.NewSize.Width);
        AttachedToVisualTree += (_, _) => ApplyResponsiveLayout(Bounds.Width);
    }

    public event EventHandler? CloseRequested;

    public void FocusSearchInput()
    {
        SearchInput.Focus();
        SearchInput.SelectAll();
    }

    private void ApplyResponsiveLayout(double width)
    {
        if (SceneColumns.ColumnDefinitions.Count < 3) return;
        var compact = width < 840;
        SceneColumns.ColumnDefinitions[0].Width = compact ? new GridLength(0) : new GridLength(230);
        SceneColumns.ColumnDefinitions[2].Width = width < 1200 ? new GridLength(0) : new GridLength(310);
        FilterPanel.IsVisible = !compact;
        IndexPanel.IsVisible = width >= 1200;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
    private void OnFileNameModeClick(object? sender, RoutedEventArgs e) => _viewModel.SearchMode = "文件名";
    private void OnContentModeClick(object? sender, RoutedEventArgs e) => _viewModel.SearchMode = "正文";
    private void OnAllTypesClick(object? sender, RoutedEventArgs e) => _viewModel.SetType("全部类型");
    private void OnImagesClick(object? sender, RoutedEventArgs e) => _viewModel.SetType("图片");
    private void OnDocumentsClick(object? sender, RoutedEventArgs e) => _viewModel.SetType("文档");
    private void OnAddRootClick(object? sender, RoutedEventArgs e) => _viewModel.StatusText = "演示：添加索引根目录需要在正式索引服务接入后启用";
    private void OnPauseClick(object? sender, RoutedEventArgs e) => _viewModel.TogglePause();
    private void OnRebuildClick(object? sender, RoutedEventArgs e) => _viewModel.RebuildIndex();
    private void OnClearClick(object? sender, RoutedEventArgs e) => _viewModel.RequestClear();
    private void OnCancelClearClick(object? sender, RoutedEventArgs e) => _viewModel.CancelClear();
    private void OnConfirmClearClick(object? sender, RoutedEventArgs e) => _viewModel.ConfirmClear();

    private void OnResultOpenClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path })
            _viewModel.StatusText = $"演示：定位结果“{path}”；样例位置不可访问，也未扫描真实磁盘";
    }
}
