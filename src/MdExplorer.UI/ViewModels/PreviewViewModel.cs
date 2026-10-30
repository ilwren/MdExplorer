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
    Func<IFileLauncherService> launcherRefresh,
    IMediaPlaybackService? mediaPlayback = null) : ObservableObject
{
    private readonly IMediaPlaybackService? _media = mediaPlayback;
    private string _mediaProbeText = string.Empty;
    private string? _mediaProbeReason;
    // 探测说“系统接不了”时，能力位再好看也不能播——否则按钮按下去只会失败。
    private bool _mediaProbeAllowsPlayback = true;
    private Avalonia.Threading.DispatcherTimer? _mediaTimer;
    private string? _mediaPath;
    private bool _mediaSubscribed;
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

    // ---------- 内嵌音视频（v0.30 第一切片） ----------
    /// <summary>当前选中项是音频或视频。</summary>
    public bool IsMedia => CurrentItem is { IsFolder: false, Kind: FileKind.Audio or FileKind.Video };
    public bool IsAudioMedia => CurrentItem is { IsFolder: false, Kind: FileKind.Audio };
    public bool IsVideoMedia => CurrentItem is { IsFolder: false, Kind: FileKind.Video };
    /// <summary>
    /// 当前后端能否在应用内播放这个媒体。音频只看 CanPlayAudio；
    /// 视频还要求后端真的能交付画面（<see cref="MediaRendererKind.None"/> 时不能播，
    /// 否则会出现“有声音没画面”的假播放）。
    /// </summary>
    public bool CanPlayMediaInApp =>
        _media is { } media && IsMedia && _mediaProbeAllowsPlayback &&
        (IsAudioMedia
            ? media.Capabilities.CanPlayAudio
            : media.Capabilities.CanPlayVideo && media.RendererKind != MediaRendererKind.None);
    /// <summary>后端需要宿主的原生画面窗口（Android TextureView / Windows HWND）。</summary>
    public IMediaVideoRenderer? VideoRenderer => _media as IMediaVideoRenderer;
    public bool ShowNativeVideoSurface =>
        IsVideoMedia && CanPlayMediaInApp && _media?.RendererKind == MediaRendererKind.NativeSurface;
    public bool IsMediaPlaying => _media?.State == MediaPlaybackState.Playing;
    public string MediaBackendName => _media?.Capabilities.BackendName ?? "none";
    /// <summary>系统解码器对这个文件的预判（事实来自平台，不是按扩展名猜）。</summary>
    public string MediaProbeText => _mediaProbeText;
    public bool HasMediaProbe => IsMedia && !string.IsNullOrWhiteSpace(_mediaProbeText) && !CanPlayMediaInApp;
    public string MediaProbeReasonText => _mediaProbeReason ?? string.Empty;
    public bool HasMediaProbeReason => IsMedia && !string.IsNullOrWhiteSpace(_mediaProbeReason);
    /// <summary>画面交付方式：纯音频后端或未接视频时为“无画面输出”。</summary>
    public string MediaRendererText => _media?.RendererKind switch
    {
        MediaRendererKind.NativeSurface => "画面：平台原生 Surface",
        MediaRendererKind.FramePush => "画面：宿主帧呈现",
        _ => "画面：未接入（用系统应用打开）",
    };
    public string MediaBackendDiagnostic => _media?.Capabilities.Diagnostic ?? "未配置播放后端。";
    public string MediaStateText => _media?.State switch
    {
        MediaPlaybackState.Opening => "正在打开…",
        MediaPlaybackState.Playing => "正在播放",
        MediaPlaybackState.Paused => "已暂停",
        MediaPlaybackState.Stopped => "已停止",
        MediaPlaybackState.Ended => "播放结束",
        MediaPlaybackState.Failed => _media.LastError ?? "播放失败",
        _ => CanPlayMediaInApp ? "可以播放" : "此平台暂不支持内嵌播放",
    };
    public string MediaPositionText => FormatTime(_media?.Position ?? TimeSpan.Zero);
    public string MediaDurationText => FormatTime(_media?.Duration ?? TimeSpan.Zero);
    public double MediaProgressPercent
    {
        get
        {
            if (_media is not { Duration.TotalMilliseconds: > 0 } media)
                return 0;
            return Math.Clamp(media.Position.TotalMilliseconds * 100 / media.Duration.TotalMilliseconds, 0, 100);
        }
    }
    public int MediaVolumePercent
    {
        get => _media?.VolumePercent ?? 100;
        set
        {
            _media?.SetVolume(value);
            OnPropertyChanged();
        }
    }

    private static string FormatTime(TimeSpan value) =>
        value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{value.Minutes:00}:{value.Seconds:00}";

    [RelayCommand]
    private async Task ToggleMediaPlaybackAsync()
    {
        if (_media is not { } media || !CanPlayMediaInApp)
            return;
        if (CurrentItem is not { } item)
            return;
        if (_mediaPath is null || !string.Equals(_mediaPath, item.FullPath, StringComparison.Ordinal))
        {
            if (!await media.OpenAsync(item.FullPath, CancellationToken.None).ConfigureAwait(true))
            {
                RaiseMediaState();
                return;
            }
            _mediaPath = item.FullPath;
        }

        if (media.State == MediaPlaybackState.Playing)
            await media.PauseAsync().ConfigureAwait(true);
        else
            await media.PlayAsync(CancellationToken.None).ConfigureAwait(true);
        RaiseMediaState();
        UpdateMediaTimer();
    }

    [RelayCommand]
    private async Task StopMediaPlaybackAsync()
    {
        if (_media is not { } media)
            return;
        await media.StopAsync().ConfigureAwait(true);
        RaiseMediaState();
        UpdateMediaTimer();
    }

    [RelayCommand]
    private async Task SeekMediaForwardAsync()
    {
        if (_media is not { } media)
            return;
        await media.SeekAsync(media.Position + TimeSpan.FromSeconds(10)).ConfigureAwait(true);
        RaiseMediaState();
    }

    [RelayCommand]
    private async Task SeekMediaBackwardAsync()
    {
        if (_media is not { } media)
            return;
        var target = media.Position - TimeSpan.FromSeconds(10);
        await media.SeekAsync(target < TimeSpan.Zero ? TimeSpan.Zero : target).ConfigureAwait(true);
        RaiseMediaState();
    }

    /// <summary>
    /// 打开前先问平台「认不认这个文件」。只有实现了 <see cref="IMediaProbe"/> 的后端才答得上来，
    /// 答不上来就明说未知——绝不用扩展名猜出一个“应该能播”。
    /// </summary>
    private async Task ProbeMediaAsync(FileItem item)
    {
        _mediaProbeText = string.Empty;
        _mediaProbeReason = null;
        _mediaProbeAllowsPlayback = true;
        if (_media is IMediaProbe probe)
        {
            try
            {
                var result = await probe.ProbeAsync(item.FullPath, CancellationToken.None).ConfigureAwait(true);
                _mediaProbeText = result.Summary;
                _mediaProbeReason = result.Reason;
                _mediaProbeAllowsPlayback = result.CanPlay;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or InvalidOperationException or NotSupportedException or ArgumentException)
            {
                _mediaProbeText = "系统解码器探测失败";
                _mediaProbeReason = ex.Message;
            }
        }
        else if (_media is not null)
        {
            _mediaProbeText = "系统解码器能力未知（尝试播放后才知道）";
        }
        OnPropertyChanged(nameof(CanPlayMediaInApp));
        OnPropertyChanged(nameof(ShowNativeVideoSurface));
        OnPropertyChanged(nameof(MediaProbeText));
        OnPropertyChanged(nameof(HasMediaProbe));
        OnPropertyChanged(nameof(MediaProbeReasonText));
        OnPropertyChanged(nameof(HasMediaProbeReason));
    }

    private void RaiseMediaState()
    {
        OnPropertyChanged(nameof(IsMedia));
        OnPropertyChanged(nameof(IsAudioMedia));
        OnPropertyChanged(nameof(IsVideoMedia));
        OnPropertyChanged(nameof(CanPlayMediaInApp));
        OnPropertyChanged(nameof(ShowNativeVideoSurface));
        OnPropertyChanged(nameof(VideoRenderer));
        OnPropertyChanged(nameof(IsMediaPlaying));
        OnPropertyChanged(nameof(MediaStateText));
        OnPropertyChanged(nameof(MediaPositionText));
        OnPropertyChanged(nameof(MediaDurationText));
        OnPropertyChanged(nameof(MediaProgressPercent));
        OnPropertyChanged(nameof(MediaVolumePercent));
        OnPropertyChanged(nameof(MediaBackendName));
        OnPropertyChanged(nameof(MediaBackendDiagnostic));
        OnPropertyChanged(nameof(MediaRendererText));
        OnPropertyChanged(nameof(MediaProbeText));
        OnPropertyChanged(nameof(HasMediaProbe));
        OnPropertyChanged(nameof(MediaProbeReasonText));
        OnPropertyChanged(nameof(HasMediaProbeReason));
    }

    private void SubscribeMedia()
    {
        if (_media is null || _mediaSubscribed)
            return;
        _media.PlaybackChanged += OnMediaPlaybackChanged;
        _mediaSubscribed = true;
    }

    private void OnMediaPlaybackChanged(object? sender, EventArgs e)
    {
        // 后端可能从任意线程回调：有 Avalonia 应用时切回 UI 线程，测试环境直接刷新。
        if (global::Avalonia.Application.Current is not null && !Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(RaiseMediaState);
            return;
        }
        RaiseMediaState();
    }

    /// <summary>播放中每 500 ms 刷新一次进度；没有 Avalonia 消息泵（普通单元测试）时不建定时器。</summary>
    private void UpdateMediaTimer()
    {
        if (_media is null || global::Avalonia.Application.Current is null)
            return;
        if (IsMediaPlaying)
        {
            if (_mediaTimer is not null)
                return;
            _mediaTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _mediaTimer.Tick += (_, _) =>
            {
                if (_media is IMediaPlaybackPoller poll)
                    poll.PollPosition();
                RaiseMediaState();
            };
            _mediaTimer.Start();
            return;
        }
        StopMediaTimer();
    }

    private void StopMediaTimer()
    {
        if (_mediaTimer is null)
            return;
        _mediaTimer.Stop();
        _mediaTimer = null;
    }

    private async Task StopMediaIfAnyAsync()
    {
        StopMediaTimer();
        _mediaPath = null;
        if (_media is { } media && media.State is MediaPlaybackState.Playing or MediaPlaybackState.Paused
            or MediaPlaybackState.Opening or MediaPlaybackState.Stopped)
        {
            await media.StopAsync().ConfigureAwait(true);
        }
    }
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
        await StopMediaIfAnyAsync().ConfigureAwait(true);
        // 停媒体是异步的：等它返回时可能已经有更新的一次 LoadAsync 接管了预览。
        // 旧代次若继续往下写状态，就会把新选中的文件清成空白（v0.32 反馈的浮窗空内容）。
        if (generation != _generation)
            return;
        SubscribeMedia();
        CurrentItem = item;
        OnPropertyChanged(nameof(CurrentItem));
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
        RaiseMediaState();
        if (IsMedia)
        {
            await ProbeMediaAsync(item).ConfigureAwait(true);
            if (generation != _generation)
                return;
        }
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
            // 媒体项的可用性/失败原因由媒体面板逐条说明；再叠一句兜底文案只会自相矛盾。
            FailureText = IsMedia ? null : content.Message;
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
