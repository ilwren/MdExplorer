using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using MdExplorer.UI.Preview;
using DocxViewerControl = OfficeViewer.Avalonia.Docx.DocxViewer;
using PdfViewerControl = OfficeViewer.Avalonia.Pdf.PdfViewer;
using PptxViewerControl = OfficeViewer.Avalonia.Pptx.PptxViewer;
using XlsxViewerControl = OfficeViewer.Avalonia.Xlsx.XlsxViewer;

namespace MdExplorer.UI.Views;

/// <summary>
/// OfficeViewer.Avalonia 的唯一宿主适配层：按格式只实例化一个第三方控件，
/// 同步页数/工作表状态，并在窄视口把其固定纸面宽度整体缩放到可用宽度。
/// 控件内部仍负责连续滚动、Ctrl+滚轮及触摸缩放，离开视觉树时释放文档资源。
/// </summary>
public sealed partial class OfficeDocumentViewer : UserControl
{
    public static readonly StyledProperty<OfficePreviewDocument?> DocumentProperty =
        AvaloniaProperty.Register<OfficeDocumentViewer, OfficePreviewDocument?>(nameof(Document));

    public static readonly StyledProperty<int> SelectedSheetIndexProperty =
        AvaloniaProperty.Register<OfficeDocumentViewer, int>(
            nameof(SelectedSheetIndex), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<int> PageCountProperty =
        AvaloniaProperty.Register<OfficeDocumentViewer, int>(
            nameof(PageCount), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<int> SheetCountProperty =
        AvaloniaProperty.Register<OfficeDocumentViewer, int>(
            nameof(SheetCount), defaultBindingMode: BindingMode.TwoWay);

    private readonly ScaleTransform _scaleTransform = new();
    private Control? _viewer;
    private OfficePreviewFormat? _format;
    private bool _syncingSheetIndex;

    public OfficeDocumentViewer()
    {
        InitializeComponent();
        ScaleHost.LayoutTransform = _scaleTransform;
    }

    public OfficePreviewDocument? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public int SelectedSheetIndex
    {
        get => GetValue(SelectedSheetIndexProperty);
        set => SetValue(SelectedSheetIndexProperty, value);
    }

    public int PageCount
    {
        get => GetValue(PageCountProperty);
        set => SetValue(PageCountProperty, value);
    }

    public int SheetCount
    {
        get => GetValue(SheetCountProperty);
        set => SetValue(SheetCountProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DocumentProperty)
        {
            ReplaceViewer(change.GetNewValue<OfficePreviewDocument?>());
        }
        else if (change.Property == SelectedSheetIndexProperty && !_syncingSheetIndex &&
                 _viewer is XlsxViewerControl xlsx)
        {
            _syncingSheetIndex = true;
            try
            {
                xlsx.SetCurrentValue(
                    XlsxViewerControl.SelectedSheetIndexProperty,
                    change.GetNewValue<int>());
            }
            finally
            {
                _syncingSheetIndex = false;
            }
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateScale();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_viewer is null && Document is not null)
            ReplaceViewer(Document);
        else
            UpdateScale();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ReleaseViewer();
        base.OnDetachedFromVisualTree(e);
    }

    private void ReplaceViewer(OfficePreviewDocument? document)
    {
        ReleaseViewer();
        SetCurrentValue(PageCountProperty, 0);
        SetCurrentValue(SheetCountProperty, 0);
        if (document is null)
            return;

        _format = document.Format;
        Control viewer = document.Format switch
        {
            OfficePreviewFormat.Docx => new DocxViewerControl(),
            OfficePreviewFormat.Pptx => new PptxViewerControl(),
            OfficePreviewFormat.Xlsx => new XlsxViewerControl(),
            _ => new PdfViewerControl(),
        };
        viewer.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        viewer.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        viewer.PropertyChanged += OnViewerPropertyChanged;
        _viewer = viewer;
        ScaleHost.Child = viewer;
        UpdateScale();

        // 先订阅计数再赋字节，确保第三方控件异步完成时宿主状态不丢第一帧结果。
        switch (viewer)
        {
            case DocxViewerControl docx:
                docx.Document = document.Data;
                break;
            case PptxViewerControl pptx:
                pptx.Document = document.Data;
                break;
            case XlsxViewerControl xlsx:
                xlsx.SelectedSheetIndex = SelectedSheetIndex;
                xlsx.Document = document.Data;
                break;
            case PdfViewerControl pdf:
                pdf.Document = document.Data;
                break;
        }
    }

    private void ReleaseViewer()
    {
        if (_viewer is null)
            return;
        _viewer.PropertyChanged -= OnViewerPropertyChanged;
        if (ReferenceEquals(ScaleHost.Child, _viewer))
            ScaleHost.Child = null;
        (_viewer as IDisposable)?.Dispose();
        _viewer = null;
        _format = null;
    }

    private void OnViewerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, _viewer))
            return;

        if (sender is DocxViewerControl && e.Property == DocxViewerControl.PageCountProperty)
            SetCurrentValue(PageCountProperty, e.GetNewValue<int>());
        else if (sender is PptxViewerControl && e.Property == PptxViewerControl.PageCountProperty)
            SetCurrentValue(PageCountProperty, e.GetNewValue<int>());
        else if (sender is PdfViewerControl && e.Property == PdfViewerControl.PageCountProperty)
            SetCurrentValue(PageCountProperty, e.GetNewValue<int>());
        else if (sender is XlsxViewerControl && e.Property == XlsxViewerControl.SheetCountProperty)
            SetCurrentValue(SheetCountProperty, e.GetNewValue<int>());
        else if (sender is XlsxViewerControl && e.Property == XlsxViewerControl.SelectedSheetIndexProperty &&
                 !_syncingSheetIndex)
        {
            _syncingSheetIndex = true;
            try
            {
                SetCurrentValue(SelectedSheetIndexProperty, e.GetNewValue<int>());
            }
            finally
            {
                _syncingSheetIndex = false;
            }
        }
    }

    private void UpdateScale()
    {
        if (_viewer is null || _format is not { } format)
            return;
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0)
            return;

        // 第三方控件没有 FitWidth/Zoom 绑定：给它一个不小于纸面自然宽度的设计视口，
        // 再由布局变换等比落回真实视口，Compact 下第一屏仍能看到完整页宽。
        var nominalWidth = format switch
        {
            OfficePreviewFormat.Docx => 920d,
            OfficePreviewFormat.Pptx => 1380d,
            OfficePreviewFormat.Xlsx => 1220d,
            _ => 760d,
        };
        var designWidth = Math.Max(width, nominalWidth);
        var scale = Math.Clamp(width / designWidth, 0.1, 1);
        _viewer.Width = designWidth;
        _viewer.Height = height / scale;
        _scaleTransform.ScaleX = scale;
        _scaleTransform.ScaleY = scale;
    }
}
