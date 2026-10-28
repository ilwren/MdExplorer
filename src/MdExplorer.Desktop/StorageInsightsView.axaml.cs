using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MdExplorer.Desktop;

public partial class StorageInsightsView : UserControl
{
    private readonly StorageInsightsViewModel _viewModel = new();

    public StorageInsightsView()
    {
        InitializeComponent();
        DataContext = _viewModel;
        SizeChanged += (_, args) => ApplyResponsiveLayout(args.NewSize.Width);
        AttachedToVisualTree += (_, _) => ApplyResponsiveLayout(Bounds.Width);
    }

    public event EventHandler? CloseRequested;
    public event EventHandler? SearchPreviewRequested;

    private void ApplyResponsiveLayout(double width)
    {
        if (InsightsColumns.ColumnDefinitions.Count < 2) return;
        var compact = width < 840;
        InsightsColumns.ColumnDefinitions[0].Width = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(1.45, GridUnitType.Star);
        InsightsColumns.ColumnDefinitions[1].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        ReviewColumn.IsVisible = !compact;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
    private void OnSearchPreviewClick(object? sender, RoutedEventArgs e) => SearchPreviewRequested?.Invoke(this, EventArgs.Empty);

    private void OnCategoryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: StorageCategory category }) _viewModel.SelectCategory(category);
    }

    private void OnTreemapImageClick(object? sender, RoutedEventArgs e) => SelectCategory("图片与视频");
    private void OnTreemapDocumentsClick(object? sender, RoutedEventArgs e) => SelectCategory("文档");
    private void OnTreemapAppsClick(object? sender, RoutedEventArgs e) => SelectCategory("应用与缓存");
    private void OnTreemapArchiveClick(object? sender, RoutedEventArgs e) => SelectCategory("压缩包");
    private void OnTreemapOtherClick(object? sender, RoutedEventArgs e) => SelectCategory("其他");

    private void SelectCategory(string name)
    {
        var category = _viewModel.Categories.FirstOrDefault(item => item.Name == name);
        if (category is not null) _viewModel.SelectCategory(category);
    }

    private void OnLocateClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string fileName } && _viewModel.LargeFiles.FirstOrDefault(item => item.FileName == fileName) is { } file)
            _viewModel.Locate(file);
    }

    private void OnPreviewClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string fileName } && _viewModel.LargeFiles.FirstOrDefault(item => item.FileName == fileName) is { } file)
            _viewModel.Preview(file);
    }

    private void OnReviewClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DuplicateGroup group }) _viewModel.AddToReview(group);
    }

    private void OnReviewAllClick(object? sender, RoutedEventArgs e)
    {
        foreach (var group in _viewModel.Duplicates) _viewModel.AddToReview(group);
    }
}
