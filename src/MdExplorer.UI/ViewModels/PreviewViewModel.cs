using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MdExplorer.Domain;
using MdExplorer.Platform.Abstractions;
using MdExplorer.UI.Preview;

namespace MdExplorer.UI.ViewModels;

/// <summary>
/// 预览状态机：图片位图 / 文本（含代码、数据、未知二进制 Hex 摘要）/ ZIP 条目 / 纯元数据卡。
/// 内容生产已插件化（P0，docs/PLUGIN-SYSTEM.md §6）：经 <see cref="PreviewProviderRegistry"/>
/// 按优先级择一提供者，本类只管呈现状态与代次防串。每次 Load 先递增代次作废上一轮。
/// </summary>
public sealed partial class PreviewViewModel(
    PreviewProviderRegistry previews,
    Func<IFileLauncherService> launcherRefresh) : ObservableObject
{
    private int _generation;
    private int _pdfRenderGeneration;
    private byte[]? _pdfData;
    private bool _switchingPdfEngine;

    [ObservableProperty] private bool _hasItem;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private Bitmap? _image;
    [ObservableProperty] private string? _textContent;
    [ObservableProperty] private string? _markdownText;
    [ObservableProperty] private string? _highlightLanguage;
    [ObservableProperty] private string? _failureText;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _canOpen;
    /// <summary>预览区页签：0=内容，1=属性。切换文件时回到内容，文件夹则直接落到属性。</summary>
    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private int _pdfPageIndex;
    [ObservableProperty] private int _pdfPageCount;
    [ObservableProperty] private OfficePreviewDocument? _officeDocument;
    [ObservableProperty] private int _officePageCount;
    [ObservableProperty] private int _officeSheetCount;
    [ObservableProperty] private int _officeSheetIndex;
    [ObservableProperty] private bool _officeViewerLoadFailed;

    public bool HasImage => Image is not null;
    public bool HasText => TextContent is not null;
    public bool HasMarkdown => MarkdownText is not null;
    /// <summary>代码呈现（编辑器：行号/高亮）——HighlightLanguage 非空即进入，包括 ""（无高亮定义）。</summary>
    public bool ShowSource => HighlightLanguage is not null;
    public bool ShowPlainText => TextContent is not null && HighlightLanguage is null;
    public bool HasArchive => ArchiveEntries.Count > 0;
    /// <summary>轻编辑/PDF 文档可从只读预览进入独立工作台。</summary>
    public bool CanOpenDocumentWorkbench =>
        CurrentItem is { } item && DocumentWorkbenchViewModel.CanOpen(item);
    /// <summary>仅本地 ZIP 家族文件可进入工作台；SAF 定位串不伪装成本地路径。</summary>
    public bool CanOpenArchiveWorkbench =>
        CurrentItem is { IsFolder: false, Kind: FileKind.Archive } item &&
        !FileLocator.IsStorageUri(item.FullPath);
    public bool HasFailure => FailureText is not null;
    public bool HasPdf => PdfPageCount > 0;
    public bool HasOfficeDocument => OfficeDocument is not null;
    public bool IsOfficeExcel => OfficeDocument?.Format == OfficePreviewFormat.Xlsx;
    public bool IsDirectPdf => OfficeDocument?.Format == OfficePreviewFormat.Pdf;
    public PdfViewEngine PdfEngine => IsDirectPdf ? PdfViewEngine.Direct : PdfViewEngine.Raster;
    public bool CanUseRasterPdf =>
        !OperatingSystem.IsAndroid() || OperatingSystem.IsAndroidVersionAtLeast(31);
    public bool CanSwitchToRasterPdf => IsDirectPdf && _pdfData is not null && CanUseRasterPdf;
    public bool CanPreviousOfficeSheet => IsOfficeExcel && OfficeSheetIndex > 0;
    public bool CanNextOfficeSheet => IsOfficeExcel && OfficeSheetIndex < OfficeSheetCount - 1;
    public string OfficeStatusText
    {
        get
        {
            if (OfficeDocument is not { } document)
                return string.Empty;
            return document.Format switch
            {
                OfficePreviewFormat.Docx => OfficePageCount > 0
                    ? $"Word · 共 {OfficePageCount:N0} 页 · 只读兼容预览"
                    : $"Word · {document.Facts.Summary} · 正在排版",
                OfficePreviewFormat.Pptx => $"PowerPoint · 共 {(OfficePageCount > 0 ? OfficePageCount : document.Facts.SlideCount ?? 0):N0} 张 · 只读兼容预览",
                OfficePreviewFormat.Xlsx => OfficeSheetCount > 0
                    ? $"Excel · 工作表 {OfficeSheetIndex + 1:N0}/{OfficeSheetCount:N0} · {CurrentOfficeSheetName}"
                    : $"Excel · {document.Facts.Summary} · 正在读取",
                _ => OfficePageCount > 0
                    ? $"PDF 直接预览 · 共 {OfficePageCount:N0} 页"
                    : "PDF 直接预览 · 正在读取",
            };
        }
    }
    private string CurrentOfficeSheetName =>
        OfficeDocument is { } document && OfficeSheetIndex >= 0 && OfficeSheetIndex < document.Facts.SheetNames.Count
            ? document.Facts.SheetNames[OfficeSheetIndex]
            : string.Empty;
    /// <summary>内容页下方的图标事实条只承载数值；带字段名的完整事实仍在属性页。</summary>
    public bool HasQuickFacts => CurrentItem is { IsFolder: false };
    public string QuickSizeText => CurrentItem is { IsFolder: false } item ? Formatting.Bytes(item.Length) : string.Empty;
    public string QuickModifiedText => CurrentItem is { } item
        ? Formatting.DateTime(item.LastWriteTime, DateTimeOffset.Now)
        : string.Empty;
    public bool HasImageDimensions => HasImage && !HasPdf && Image is { PixelSize.Width: > 0, PixelSize.Height: > 0 };
    public string ImageDimensionsText => HasImageDimensions ? $"{Image!.PixelSize.Width}×{Image.PixelSize.Height}" : string.Empty;
    public string PdfPageText => HasPdf ? $"第 {PdfPageIndex + 1} / {PdfPageCount} 页" : string.Empty;
    public bool CanPreviousPdfPage => HasPdf && PdfPageIndex > 0;
    public bool CanNextPdfPage => HasPdf && PdfPageIndex < PdfPageCount - 1;

    public ObservableCollection<MetadataRowModel> MetadataRows { get; } = [];
    public ObservableCollection<ArchiveEntryRowModel> ArchiveEntries { get; } = [];

    public FileItem? CurrentItem { get; private set; }

    /// <summary>请求 Shell 打开当前本地压缩包的工作台。</summary>
    public event EventHandler<string>? ArchiveWorkbenchRequested;

    /// <summary>请求 Shell 打开当前文本/PDF 文档工作台。</summary>
    public event EventHandler<FileItem>? DocumentWorkbenchRequested;

    partial void OnImageChanged(Bitmap? value)
    {
        OnPropertyChanged(nameof(HasImage));
        OnPropertyChanged(nameof(HasImageDimensions));
        OnPropertyChanged(nameof(ImageDimensionsText));
    }

    private void RaiseQuickFacts()
    {
        OnPropertyChanged(nameof(HasQuickFacts));
        OnPropertyChanged(nameof(QuickSizeText));
        OnPropertyChanged(nameof(QuickModifiedText));
        OnPropertyChanged(nameof(HasImageDimensions));
        OnPropertyChanged(nameof(ImageDimensionsText));
    }
    partial void OnTextContentChanged(string? value)
    {
        OnPropertyChanged(nameof(HasText));
        OnPropertyChanged(nameof(ShowPlainText));
    }

    partial void OnMarkdownTextChanged(string? value) => OnPropertyChanged(nameof(HasMarkdown));

    partial void OnHighlightLanguageChanged(string? value)
    {
        OnPropertyChanged(nameof(ShowSource));
        OnPropertyChanged(nameof(ShowPlainText));
    }
    partial void OnFailureTextChanged(string? value) => OnPropertyChanged(nameof(HasFailure));

    partial void OnPdfPageIndexChanged(int value) => RaisePdfState();

    partial void OnPdfPageCountChanged(int value) => RaisePdfState();

    partial void OnOfficeDocumentChanged(OfficePreviewDocument? value) => RaiseOfficeState();

    partial void OnOfficePageCountChanged(int value) => RaiseOfficeState();

    partial void OnOfficeSheetCountChanged(int value)
    {
        if (value > 0 && OfficeSheetIndex >= value)
            OfficeSheetIndex = value - 1;
        RaiseOfficeState();
    }

    partial void OnOfficeSheetIndexChanged(int value) => RaiseOfficeState();

    partial void OnOfficeViewerLoadFailedChanged(bool value)
    {
        if (value && IsDirectPdf && CanUseRasterPdf)
            _ = SwitchToRasterPdfAsync();
    }

    private void RaiseOfficeState()
    {
        OnPropertyChanged(nameof(HasOfficeDocument));
        OnPropertyChanged(nameof(IsOfficeExcel));
        OnPropertyChanged(nameof(IsDirectPdf));
        OnPropertyChanged(nameof(PdfEngine));
        OnPropertyChanged(nameof(CanSwitchToRasterPdf));
        OnPropertyChanged(nameof(CanPreviousOfficeSheet));
        OnPropertyChanged(nameof(CanNextOfficeSheet));
        OnPropertyChanged(nameof(OfficeStatusText));
    }

    private void RaisePdfState()
    {
        OnPropertyChanged(nameof(HasPdf));
        OnPropertyChanged(nameof(PdfPageText));
        OnPropertyChanged(nameof(CanPreviousPdfPage));
        OnPropertyChanged(nameof(CanNextPdfPage));
        OnPropertyChanged(nameof(HasImageDimensions));
        OnPropertyChanged(nameof(ImageDimensionsText));
    }


    public async Task LoadAsync(FileItem? item)
    {
        var generation = ++_generation;
        CurrentItem = item;
        OnPropertyChanged(nameof(CanOpenArchiveWorkbench));
        OnPropertyChanged(nameof(CanOpenDocumentWorkbench));
        RaiseQuickFacts();
        Image = null;
        TextContent = null;
        MarkdownText = null;
        HighlightLanguage = null;
        FailureText = null;
        _pdfData = null;
        _switchingPdfEngine = false;
        PdfPageIndex = 0;
        PdfPageCount = 0;
        OfficeDocument = null;
        OfficeViewerLoadFailed = false;
        OfficePageCount = 0;
        OfficeSheetCount = 0;
        OfficeSheetIndex = 0;
        ArchiveEntries.Clear();
        MetadataRows.Clear();
        OnPropertyChanged(nameof(HasArchive));

        if (item is null)
        {
            HasItem = false;
            CanOpen = false;
            Title = string.Empty;
            Subtitle = string.Empty;
            IsLoading = false;
            return;
        }

        HasItem = true;
        CanOpen = !item.IsFolder;
        SelectedTabIndex = item.IsFolder ? 1 : 0;
        Title = item.Name;
        Subtitle = FileKindClassifier.Describe(item);
        BuildMetadata(item);
        if (item.IsFolder)
        {
            IsLoading = false;
            return;
        }

        IsLoading = true;
        try
        {
            var provider = previews.Resolve(item);
            if (provider is null)
            {
                FailureText = "此类型暂不支持内置预览，可先用系统应用打开。";
                return;
            }

            var content = await provider.LoadAsync(item, CancellationToken.None).ConfigureAwait(true);
            if (generation != _generation) return;
            Image = content.Image;
            TextContent = content.Text;
            MarkdownText = content.Markdown;
            HighlightLanguage = content.HighlightLanguage;
            if (content.Entries is not null)
            {
                foreach (var entry in content.Entries)
                    ArchiveEntries.Add(entry);
                OnPropertyChanged(nameof(HasArchive));
            }
            if (content.Pdf is { } pdf)
            {
                _pdfData = pdf.Data;
                PdfPageCount = pdf.PageCount;
                PdfPageIndex = 0;
                Image = pdf.FirstPage;
                MetadataRows.Add(new MetadataRowModel("页数", $"{pdf.PageCount:N0} 页"));
            }
            if (content.Office is { } office)
            {
                if (office.Format == OfficePreviewFormat.Pdf)
                    _pdfData = office.Data;
                OfficeDocument = office;
                OfficeSheetIndex = 0;
                OfficeSheetCount = office.Facts.SheetNames.Count;
                OfficePageCount = office.Facts.SlideCount ?? 0;
                MetadataRows.Add(new MetadataRowModel("结构", office.Facts.Summary));
                MetadataRows.Add(new MetadataRowModel("渲染", OfficeDocumentLoader.EngineDescription));
                if (office.Facts.SheetNames.Count > 0)
                    MetadataRows.Add(new MetadataRowModel("工作表", string.Join("、", office.Facts.SheetNames)));
            }
            FailureText = content.Message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or ArgumentException)
        {
            if (generation == _generation)
                FailureText = $"预览失败：{ex.Message}";
        }
        finally
        {
            if (generation == _generation)
                IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task PreviousPdfPageAsync() => await GoToPdfPageAsync(PdfPageIndex - 1).ConfigureAwait(true);

    [RelayCommand]
    private async Task NextPdfPageAsync() => await GoToPdfPageAsync(PdfPageIndex + 1).ConfigureAwait(true);

    [RelayCommand]
    private async Task FirstPdfPageAsync() => await GoToPdfPageAsync(0).ConfigureAwait(true);

    [RelayCommand]
    private async Task LastPdfPageAsync() => await GoToPdfPageAsync(PdfPageCount - 1).ConfigureAwait(true);

    [RelayCommand]
    private void PreviousOfficeSheet() => OfficeSheetIndex = Math.Max(0, OfficeSheetIndex - 1);

    [RelayCommand]
    private void NextOfficeSheet() =>
        OfficeSheetIndex = Math.Min(Math.Max(0, OfficeSheetCount - 1), OfficeSheetIndex + 1);

    [RelayCommand]
    private async Task UseRasterPdfAsync() => await SwitchToRasterPdfAsync().ConfigureAwait(true);

    private async Task SwitchToRasterPdfAsync()
    {
        if (_switchingPdfEngine || !IsDirectPdf || _pdfData is null || !CanUseRasterPdf)
            return;

        _switchingPdfEngine = true;
        var generation = _generation;
        var renderGeneration = ++_pdfRenderGeneration;
        var data = _pdfData;
        OfficeDocument = null;
        OfficePageCount = 0;
        OfficeViewerLoadFailed = false;
        FailureText = null;
        IsLoading = true;
        try
        {
            var pdf = await PdfPreviewRenderer.LoadAsync(data, CancellationToken.None).ConfigureAwait(true);
            if (generation != _generation || renderGeneration != _pdfRenderGeneration)
            {
                pdf.FirstPage.Dispose();
                return;
            }

            PdfPageCount = pdf.PageCount;
            PdfPageIndex = 0;
            Image = pdf.FirstPage;
            for (var i = MetadataRows.Count - 1; i >= 0; i--)
            {
                if (MetadataRows[i].Key is "页数" or "结构" or "渲染")
                    MetadataRows.RemoveAt(i);
            }
            MetadataRows.Add(new MetadataRowModel("页数", $"{pdf.PageCount:N0} 页"));
            MetadataRows.Add(new MetadataRowModel("渲染", "PDFtoImage/PDFium（MIT）· 光栅兼容视图"));
            RaiseOfficeState();
            RaisePdfState();
        }
        catch (Exception ex)
        {
            if (generation == _generation && renderGeneration == _pdfRenderGeneration)
                FailureText = $"直接 PDF 预览不可用，兼容视图也无法读取：{ex.Message}";
        }
        finally
        {
            if (generation == _generation && renderGeneration == _pdfRenderGeneration)
                IsLoading = false;
            _switchingPdfEngine = false;
        }
    }

    private async Task GoToPdfPageAsync(int pageIndex)
    {
        if (_pdfData is null || PdfPageCount <= 0)
            return;
        var target = Math.Clamp(pageIndex, 0, PdfPageCount - 1);
        if (target == PdfPageIndex && Image is not null)
            return;

        var documentGeneration = _generation;
        var renderGeneration = ++_pdfRenderGeneration;
        PdfPageIndex = target;
        FailureText = null;
        IsLoading = true;
        try
        {
            var bitmap = await PdfPreviewRenderer
                .RenderPageAsync(_pdfData, target, CancellationToken.None)
                .ConfigureAwait(true);
            if (documentGeneration != _generation || renderGeneration != _pdfRenderGeneration)
            {
                bitmap.Dispose();
                return;
            }

            var previous = Image;
            Image = bitmap;
            if (previous is not null && !ReferenceEquals(previous, bitmap))
                previous.Dispose();
        }
        catch (Exception ex)
        {
            if (documentGeneration == _generation && renderGeneration == _pdfRenderGeneration)
                FailureText = $"无法渲染此 PDF 页面：{ex.Message}";
        }
        finally
        {
            if (documentGeneration == _generation && renderGeneration == _pdfRenderGeneration)
                IsLoading = false;
        }
    }

    [RelayCommand]
    private void OpenDocumentWorkbench()
    {
        if (CanOpenDocumentWorkbench && CurrentItem is { } item)
            DocumentWorkbenchRequested?.Invoke(this, item);
    }

    [RelayCommand]
    private void OpenArchiveWorkbench()
    {
        if (CanOpenArchiveWorkbench && CurrentItem is { } item)
            ArchiveWorkbenchRequested?.Invoke(this, item.FullPath);
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        if (CurrentItem is null) return;
        // Android 上启动器在 MainView 挂载后替换，故每次取最新实例
        await launcherRefresh().OpenAsync(CurrentItem.FullPath, CancellationToken.None).ConfigureAwait(true);
    }

    private void BuildMetadata(FileItem item)
    {
        MetadataRows.Add(new MetadataRowModel("类型", FileKindClassifier.Describe(item)));
        if (!item.IsFolder)
            MetadataRows.Add(new MetadataRowModel("大小", $"{Formatting.Bytes(item.Length)}（{item.Length:N0} 字节）"));
        MetadataRows.Add(new MetadataRowModel("修改时间", Formatting.FullDateTime(item.LastWriteTime)));
        MetadataRows.Add(new MetadataRowModel("位置", Path.GetDirectoryName(item.FullPath) ?? item.FullPath));
        if (item.IsSymlink)
            MetadataRows.Add(new MetadataRowModel("链接", "符号链接"));
        if (item.IsHidden)
            MetadataRows.Add(new MetadataRowModel("可见性", "隐藏"));
    }
}
