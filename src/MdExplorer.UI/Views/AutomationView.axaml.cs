using Avalonia.Controls;

namespace MdExplorer.UI.Views;

/// <summary>
/// 自动化节点编辑器。响应式只重排画布/检查器：宽屏并排以便边连线边改参数，Compact 时
/// 画布在上、检查器在下且页面整体可拖动滚动；不按窗口等比缩放整页。
/// </summary>
public partial class AutomationView : UserControl
{
    private bool _isStacked;

    public AutomationView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width);
    }

    private void ApplyLayout(double width)
    {
        var stacked = width > 0 && width < 900;
        if (stacked == _isStacked && width > 0)
            return;
        _isStacked = stacked;

        AutomationLayoutGrid.ColumnDefinitions.Clear();
        AutomationLayoutGrid.RowDefinitions.Clear();
        if (stacked)
        {
            AutomationLayoutGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            AutomationLayoutGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            AutomationLayoutGrid.RowDefinitions.Add(new RowDefinition(new GridLength(14)));
            AutomationLayoutGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grid.SetRow(AutomationCanvasCard, 0);
            Grid.SetColumn(AutomationCanvasCard, 0);
            Grid.SetRow(AutomationInspectorCard, 2);
            Grid.SetColumn(AutomationInspectorCard, 0);
            AutomationEditorFrame.Height = 440;
        }
        else
        {
            AutomationLayoutGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            AutomationLayoutGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(16)));
            AutomationLayoutGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(340)));
            AutomationLayoutGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grid.SetRow(AutomationCanvasCard, 0);
            Grid.SetColumn(AutomationCanvasCard, 0);
            Grid.SetRow(AutomationInspectorCard, 0);
            Grid.SetColumn(AutomationInspectorCard, 2);
            AutomationEditorFrame.Height = 520;
        }
    }
}
