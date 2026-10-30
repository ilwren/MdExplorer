using System.Text;
using Avalonia.Media.Imaging;
using MdExplorer.Domain;
using MdExplorer.Platform.Abstractions;
using MdExplorer.UI.ViewModels;
using PDFtoImage;
using PDFtoImage.Exceptions;

namespace MdExplorer.UI.Preview;

/// <summary>
/// 预览加载结果（数据进宿主、宿主渲染——插件系统一贯边界，见 docs/PLUGIN-SYSTEM.md §2/§6）。
/// 至多一个负载字段非空：位图 / 文本 / 压缩包条目 / 说明消息。
/// </summary>
public sealed record PreviewContent(
    Bitmap? Image = null,
    string? Text = null,
    IReadOnlyList<ArchiveEntryRowModel>? Entries = null,
    string? Message = null,
    string? Markdown = null,
    string? HighlightLanguage = null,
    PdfPreviewDocument? Pdf = null,
    OfficePreviewDocument? Office = null,
    KeePassUnlockRequest? KeePassUnlock = null,
    KeePassDatabase? KeePass = null);

/// <summary>PDF 预览负载：原始字节仅在内存中保留，翻页时按需光栅化，避免整本同时驻留位图。</summary>
public sealed record PdfPreviewDocument(
    byte[] Data,
    int PageCount,
    Bitmap FirstPage,
    IReadOnlyList<PdfPageSize>? PageSizes = null);

/// <summary>PDF 页面在 72dpi 下的逻辑尺寸，供工作台计算纸张宽高比。</summary>
public sealed record PdfPageSize(double Width, double Height);

/// <summary>
/// PDF 呈现引擎。Direct 是把 PDF 页面对象直接排成 Avalonia 控件的原生预览控件；
/// Raster 是 PDFium 整页光栅化，只在直接控件不适用时作为高保真兼容路径。
/// </summary>
public enum PdfViewEngine
{
    Direct,
    Raster,
}

/// <summary>
/// 预览提供者：插件体系 P0 的第一类扩展点。P1 起同名能力由声明式清单描述，
/// P2 起由进程外工作者以相同语义（返回数据而非界面）远程实现。
/// </summary>
public interface IPreviewProvider
{
    /// <summary>稳定标识（如 builtin.image）；插件清单以此为冲突键。</summary>
    string Id { get; }

    /// <summary>优先级（大者优先）；兜底提供者恒为负值。</summary>
    int Priority { get; }

    bool CanHandle(FileItem item);

    Task<PreviewContent> LoadAsync(FileItem item, CancellationToken cancellationToken);
}

/// <summary>提供者注册表：按优先级择一（与 v0.1 的 switch 分发语义等价，注册表非空时必有兜底命中）。</summary>
public sealed class PreviewProviderRegistry(IEnumerable<IPreviewProvider> providers)
{
    private readonly IPreviewProvider[] _providers = providers
        .OrderByDescending(p => p.Priority)
        .ToArray();

    public IPreviewProvider? Resolve(FileItem item)
    {
        foreach (var provider in _providers)
            if (provider.CanHandle(item))
                return provider;
        return null;
    }
}

/// <summary>内置提供者集合（P0）。未来插件提供者在此并列追加，宿主优先级裁决。</summary>
public static class BuiltInPreviewProviders
{
    public static PreviewProviderRegistry Create(IArchiveService archive, IFileSystemService fileSystem) => new(
    [
        new ImagePreviewProvider(fileSystem),
        new PdfPreviewProvider(fileSystem),
        new OfficePreviewProvider(fileSystem),
        new OpenDocumentPreviewProvider(fileSystem),
        new KeePassPreviewProvider(fileSystem),
        new MarkdownPreviewProvider(fileSystem),
        new TextPreviewProvider(fileSystem),
        new HexPreviewProvider(fileSystem),
        new ArchivePreviewProvider(archive),
        new FallbackPreviewProvider(),
    ]);
}

internal sealed class ImagePreviewProvider(IFileSystemService fileSystem) : IPreviewProvider
{
    public string Id => "builtin.image";
    public int Priority => 100;
    public bool CanHandle(FileItem item) => item.Kind == FileKind.Image;

    public async Task<PreviewContent> LoadAsync(FileItem item, CancellationToken cancellationToken)
    {
        await using var stream = await fileSystem.OpenReadAsync(item.FullPath, cancellationToken).ConfigureAwait(true);
        var bitmap = await Task.Run(() => Bitmap.DecodeToWidth(stream, 1600), cancellationToken).ConfigureAwait(true);
        return new PreviewContent(Image: bitmap);
    }
}

/// <summary>
/// PDFium 光栅化收口（PDFtoImage，MIT）。PDFium 本身不保证同进程并行安全，
/// 故全进程串行调用；输出经 PNG 中转成 Avalonia Bitmap，视图层不接触 Skia 类型。
/// </summary>
internal static class PdfPreviewRenderer
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    internal const int DefaultRenderWidth = 1200;

    private static RenderOptions Options(int width, PdfRotation rotation) => new(
        Dpi: 144,
        Width: Math.Clamp(width, 96, 3600),
        WithAnnotations: true,
        WithFormFill: true,
        WithAspectRatio: true,
        Rotation: rotation);

    /// <summary>
    /// 只数页数、不渲染。PDFium 的页树是惰性解析的，100+ 页文档也在毫秒级，
    /// 用它来决定「能不能把原始 PDF 交给直接控件」比打开后再后悔便宜得多。
    /// </summary>
    internal static int CountPages(byte[] data, string? password = null)
    {
        Gate.Wait();
        try
        {
            return Conversion.GetPageCount(data, password);
        }
        finally
        {
            Gate.Release();
        }
    }

    internal static async Task<PdfPreviewDocument> LoadAsync(
        byte[] data,
        CancellationToken cancellationToken,
        string? password = null,
        int renderWidth = DefaultRenderWidth,
        PdfRotation rotation = PdfRotation.Rotate0)
    {
        return await Task.Run(() =>
        {
            Gate.Wait(cancellationToken);
            try
            {
                var pageCount = Conversion.GetPageCount(data, password);
                if (pageCount <= 0)
                    throw new InvalidDataException("PDF 不包含任何页面。");
                var sizes = Conversion.GetPageSizes(data, password)
                    .Select(size => new PdfPageSize(size.Width, size.Height))
                    .ToArray();
                return new PdfPreviewDocument(
                    data,
                    pageCount,
                    RenderPageLocked(data, 0, password, renderWidth, rotation),
                    sizes);
            }
            finally
            {
                Gate.Release();
            }
        }, cancellationToken).ConfigureAwait(true);
    }

    internal static async Task<Bitmap> RenderPageAsync(
        byte[] data,
        int pageIndex,
        CancellationToken cancellationToken,
        string? password = null,
        int renderWidth = DefaultRenderWidth,
        PdfRotation rotation = PdfRotation.Rotate0)
    {
        return await Task.Run(() =>
        {
            Gate.Wait(cancellationToken);
            try
            {
                return RenderPageLocked(data, pageIndex, password, renderWidth, rotation);
            }
            finally
            {
                Gate.Release();
            }
        }, cancellationToken).ConfigureAwait(true);
    }

    private static Bitmap RenderPageLocked(
        byte[] data,
        int pageIndex,
        string? password,
        int renderWidth,
        PdfRotation rotation)
    {
        using var png = new MemoryStream();
        Conversion.SavePng(png, data, new Index(pageIndex), password, Options(renderWidth, rotation));
        png.Position = 0;
        return new Bitmap(png);
    }
}

internal sealed class PdfPreviewProvider(IFileSystemService fileSystem) : IPreviewProvider
{
    public string Id => "builtin.pdf";
    public int Priority => 120;
    public bool CanHandle(FileItem item) =>
        item.Kind == FileKind.Document && FileKindClassifier.IsPdf(item);

    public async Task<PreviewContent> LoadAsync(FileItem item, CancellationToken cancellationToken)
    {
        // PDFium 的 Android 资产要求 API 31+；低版本只有纯托管直接控件这一条内置路径。
        var rasterUnavailableOnPlatform = OperatingSystem.IsAndroid() && !OperatingSystem.IsAndroidVersionAtLeast(31);
        var limit = rasterUnavailableOnPlatform
            ? OfficeDocumentLoader.DirectPdfPreviewMaxBytes
            : (OperatingSystem.IsAndroid() ? 64L : 128L) * 1024 * 1024;
        if (item.Length is { } knownLength && knownLength > limit)
            return new PreviewContent(Message: $"此 PDF 为 {Formatting.Bytes(item.Length)}，超过内置预览的 {Formatting.Bytes(limit)} 安全上限；可先用系统应用打开。");

        byte[]? data = null;
        try
        {
            await using var source = await fileSystem.OpenReadAsync(item.FullPath, cancellationToken).ConfigureAwait(true);
            using var memory = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(true)) > 0)
            {
                if (memory.Length + read > limit)
                    return new PreviewContent(Message: $"此 PDF 超过内置预览的 {Formatting.Bytes(limit)} 安全上限；可先用系统应用打开。");
                await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(true);
            }

            data = memory.ToArray();
            // 默认先把原始 PDF 交给直接预览控件，由它把页面对象排成 Avalonia 控件；
            // 加密、无效文件头或超过直接控件上限时，才进入 PDFium 整页光栅化。
            if (rasterUnavailableOnPlatform)
            {
                // 低版本 Android 没有 PDFium 兼容视图，只能交给直接控件；但直接控件按页建控件树，
                // 页数太多会把内存吃满又不回落，所以这里仍然守住页数闸门，超限就如实说明。
                var pages = CountPagesOrUnknown(data);
                if (pages > OfficeDocumentLoader.DirectPdfMaxPages)
                    return new PreviewContent(Message:
                        $"此 PDF 有 {pages:N0} 页，超过当前系统版本内置直接预览的 " +
                        $"{OfficeDocumentLoader.DirectPdfMaxPages} 页上限（该版本没有 PDFium 兼容视图）；" +
                        "可先用系统应用打开。");
                return new PreviewContent(
                    Office: OfficeDocumentLoader.CreateDirectPdf(
                        data, OfficeDocumentLoader.DirectPdfPreviewMaxBytes));
            }
            if (TryUseDirectControl(data) &&
                OfficeDocumentLoader.TryCreateDirectPdf(
                    data, OfficeDocumentLoader.DirectPdfPreviewMaxBytes, out var directPdf))
                return new PreviewContent(Office: directPdf);

            var document = await PdfPreviewRenderer.LoadAsync(data, cancellationToken).ConfigureAwait(true);
            return new PreviewContent(Pdf: document);
        }
        catch (PdfPasswordProtectedException)
        {
            return new PreviewContent(Message: "此 PDF 已加密或需要密码，内置预览暂不支持输入密码；可先用系统应用打开。");
        }
        catch (PdfException ex)
        {
            return new PreviewContent(Message: $"无法读取此 PDF：{ex.Message} 可先用系统应用打开。");
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            if (data is not null)
            {
                try
                {
                    return new PreviewContent(
                        Office: OfficeDocumentLoader.CreateDirectPdf(
                            data, OfficeDocumentLoader.DirectPdfPreviewMaxBytes));
                }
                catch (InvalidDataException)
                {
                    // 直接控件也有字节上限；超限时仍回原平台不可用说明。
                }
            }
            return new PreviewContent(Message: "当前平台的 PDF 渲染组件不可用；可先用系统应用打开。");
        }
        catch (InvalidDataException ex) when (rasterUnavailableOnPlatform)
        {
            return new PreviewContent(Message: $"无法以直接预览读取此 PDF：{ex.Message} 可先用系统应用打开。");
        }
    }

    /// <summary>
    /// 直接 PDF 控件按页排控件树：页数一多内存就失控。这里先用 PDFium 数一遍页数，
    /// 只有上限内的文档才交给它；数不出来（加密/损坏）也走 PDFium 路径去拿真实原因。
    /// </summary>
    private static bool TryUseDirectControl(byte[] data) =>
        OfficeDocumentLoader.IsDirectPdfCandidate(data) &&
        OfficeDocumentLoader.CanUseDirectPdf(
            CountPagesOrUnknown(data), data.LongLength, OfficeDocumentLoader.DirectPdfPreviewMaxBytes);

    /// <summary>
    /// 数页数；数不出来（损坏、加密、本机没有 PDFium）时返回 0，由调用方按旧行为放行——
    /// 直接控件会自己报错，宿主再按 OfficeViewerLoadFailed 回落到 PDFium。
    /// </summary>
    private static int CountPagesOrUnknown(byte[] data)
    {
        try
        {
            return PdfPreviewRenderer.CountPages(data);
        }
        catch (Exception ex) when (ex is PdfException or PdfPasswordProtectedException
            or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException
            or InvalidDataException or IOException)
        {
            return 0;
        }
    }
}

/// <summary>文本类预览的统一读取（前 512KB + 截断提示，与既有 UX 一致）。</summary>
internal static class PreviewTextReader
{
    internal const long TextLimit = 512 * 1024;

    internal static async Task<string> ReadAsync(IFileSystemService fileSystem, FileItem item, CancellationToken cancellationToken)
    {
        await using var stream = await fileSystem.OpenReadAsync(item.FullPath, cancellationToken).ConfigureAwait(true);
        // 存储提供者流可能不可寻址：长度以条目元数据为准，读取循环填满（单次 Read 不保证填满）
        var total = item.Length ?? (stream.CanSeek ? stream.Length : -1);
        var buffer = new byte[total >= 0 ? Math.Min(TextLimit, total) : TextLimit];
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read, buffer.Length - read), cancellationToken).ConfigureAwait(true);
            if (n == 0) break;
            read += n;
        }
        var text = Encoding.UTF8.GetString(buffer, 0, read);
        if (total > TextLimit)
            text += $"\n\n…（仅预览前 {Formatting.Bytes(TextLimit)}，文件共 {Formatting.Bytes(item.Length)}）";
        return text;
    }
}

internal sealed class MarkdownPreviewProvider(IFileSystemService fileSystem) : IPreviewProvider
{
    public string Id => "builtin.markdown";
    public int Priority => 110; // .md 先于纯文本提供者（渲染而非原文）
    public bool CanHandle(FileItem item) =>
        string.Equals(item.Extension, ".md", StringComparison.OrdinalIgnoreCase)
        || string.Equals(item.Extension, ".markdown", StringComparison.OrdinalIgnoreCase);

    public async Task<PreviewContent> LoadAsync(FileItem item, CancellationToken cancellationToken) =>
        new(Markdown: await PreviewTextReader.ReadAsync(fileSystem, item, cancellationToken).ConfigureAwait(true));
}

internal sealed class TextPreviewProvider(IFileSystemService fileSystem) : IPreviewProvider
{
    public string Id => "builtin.text";
    public int Priority => 100;
    public bool CanHandle(FileItem item) => item.Kind is FileKind.Text or FileKind.Code or FileKind.Data;

    public async Task<PreviewContent> LoadAsync(FileItem item, CancellationToken cancellationToken)
    {
        var text = await PreviewTextReader.ReadAsync(fileSystem, item, cancellationToken).ConfigureAwait(true);
        // 代码走编辑器呈现（行号+高亮）；HighlightLanguage 为 "" 表示无对应高亮定义时纯文本编辑器
        var language = item.Kind == FileKind.Code ? SourceLanguages.ForExtension(item.Extension) ?? "" : null;
        return new PreviewContent(Text: text, HighlightLanguage: language);
    }
}

/// <summary>扩展名 → AvaloniaEdit 内置高亮定义名（未知返回 null，呈现层回退纯文本）。</summary>
internal static class SourceLanguages
{
    internal static string? ForExtension(string? extension) => extension?.ToLowerInvariant() switch
    {
        ".cs" => "C#",
        ".js" or ".mjs" or ".cjs" or ".ts" or ".tsx" or ".jsx" => "JavaScript",
        ".json" => "JavaScript",
        ".xml" or ".xaml" or ".axaml" or ".csproj" or ".props" or ".targets" or ".svg" or ".xshd" => "XML",
        ".html" or ".htm" => "HTML",
        ".css" or ".scss" or ".less" => "CSS",
        ".py" => "Python",
        ".java" => "Java",
        ".c" or ".h" or ".cpp" or ".hpp" or ".cc" or ".cxx" => "C++",
        ".ps1" or ".psm1" => "PowerShell",
        ".sql" => "SQL",
        ".php" => "PHP",
        ".rb" => "Ruby",
        _ => null,
    };
}

internal sealed class HexPreviewProvider(IFileSystemService fileSystem) : IPreviewProvider
{
    public string Id => "builtin.hex";
    public int Priority => 100;
    public bool CanHandle(FileItem item) => item.Kind == FileKind.Unknown;

    public async Task<PreviewContent> LoadAsync(FileItem item, CancellationToken cancellationToken)
    {
        await using var stream = await fileSystem.OpenReadAsync(item.FullPath, cancellationToken).ConfigureAwait(true);
        var buffer = new byte[512];
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read, buffer.Length - read), cancellationToken).ConfigureAwait(true);
            if (n == 0) break;
            read += n;
        }
        var builder = new StringBuilder();
        for (int offset = 0; offset < read; offset += 16)
        {
            builder.Append(offset.ToString("X8")).Append("  ");
            for (int i = 0; i < 16; i++)
                builder.Append(offset + i < read ? buffer[offset + i].ToString("X2") + ' ' : "   ");
            builder.Append(" |");
            for (int i = 0; i < 16 && offset + i < read; i++)
            {
                var b = buffer[offset + i];
                builder.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
            }
            builder.Append("|\n");
        }
        builder.Append($"\n（十六进制预览 · 前 {read} 字节）");
        return new PreviewContent(Text: builder.ToString());
    }
}

internal sealed class ArchivePreviewProvider(IArchiveService archive) : IPreviewProvider
{
    public string Id => "builtin.archive";
    public int Priority => 100;
    public bool CanHandle(FileItem item) =>
        item.Kind == FileKind.Archive && !item.Locator.IsStorage && archive.CanOpen(item.FullPath);

    public async Task<PreviewContent> LoadAsync(FileItem item, CancellationToken cancellationToken)
    {
        var entries = await archive.ListEntriesAsync(item.FullPath, 500, cancellationToken).ConfigureAwait(true);
        var rows = entries
            .Select(e => new ArchiveEntryRowModel(
                e.FullName,
                e.IsFolder ? "" : $"{Formatting.Bytes(e.Length)} → {Formatting.Bytes(e.CompressedLength)}"))
            .ToArray();
        return new PreviewContent(Entries: rows);
    }
}

/// <summary>兜底：给出来源明确的"暂不支持"文案，等 PDF/播放等里程碑以更高优先级提供者取代。</summary>
internal sealed class FallbackPreviewProvider : IPreviewProvider
{
    public string Id => "builtin.fallback";
    public int Priority => -100;
    public bool CanHandle(FileItem item) => true;

    public Task<PreviewContent> LoadAsync(FileItem item, CancellationToken cancellationToken) =>
        Task.FromResult(new PreviewContent(Message: item.Kind switch
        {
            FileKind.Document when FileKindClassifier.IsPdf(item) => "PDF 内置预览当前不可用，可先用系统应用打开。",
            FileKind.Video or FileKind.Audio => "媒体文件不在预览区内解码，播放控件见预览页上方；也可右键用系统应用打开。",
            _ => "此类型暂不支持内置预览，可先用系统应用打开。",
        }));
}
