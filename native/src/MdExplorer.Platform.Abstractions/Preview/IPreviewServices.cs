using MdExplorer.Domain.Files;

namespace MdExplorer.Platform.Abstractions.Preview;

/// 与 UI 无关的中立像素载体（§5.4）。
public sealed record DecodedImage(int Width, int Height, byte[] BgraPixels)
{
    public static DecodedImage? TryCreate(int width, int height, byte[] bgra)
        => width <= 0 || height <= 0 || bgra.Length < width * height * 4 ? null : new DecodedImage(width, height, bgra);
}

public sealed record ThumbnailResult(DecodedImage Image, bool FromCache);

public interface IThumbnailService
{
    ValueTask<ThumbnailResult?> GetAsync(FileItem item, int targetPx, CancellationToken ct);
    ValueTask ClearCacheAsync(CancellationToken ct);
    ValueTask<long> GetCacheSizeAsync(CancellationToken ct);
}

public interface IThumbnailGenerator
{
    bool CanHandle(FileItem item);
    ValueTask<DecodedImage?> GenerateAsync(FileItem item, int targetPx, CancellationToken ct);
}

/// 预览内容模型（可辨识联合，§5.4）：View 按类型选模板渲染，保 AOT、保断点切换重排。
public abstract record PreviewContent;

public sealed record ImageContent(string SourcePath) : PreviewContent;
public sealed record TextContent(string Text, string? Language, bool IsMarkdown) : PreviewContent;
public sealed record PdfContent(string SourcePath, int PageCount) : PreviewContent;
public sealed record AudioContent(string SourcePath, string? Title) : PreviewContent;
public sealed record VideoContent(string SourcePath, string? PosterPath) : PreviewContent;
public sealed record ArchiveContent(IReadOnlyList<ArchiveEntry> Entries) : PreviewContent;
public sealed record TableContent(IReadOnlyList<IReadOnlyList<string>> Rows) : PreviewContent;
public sealed record HexContent(string SourcePath, long Length) : PreviewContent;
public sealed record MetadataOnlyContent(string Summary) : PreviewContent;

public sealed record ArchiveEntry(string Name, bool IsDirectory, long? Length, long? CompressedLength);

public sealed record PreviewOptions(int MaxTextBytes = 512 * 1024);

/// 预览服务：UI 侧入口（由提供者链实现）。
public interface IPreviewService
{
    ValueTask<PreviewContent> LoadAsync(FileItem item, PreviewOptions options, CancellationToken ct);
}

/// 预览提供者链：按 Priority 从高到低首个 CanPreview 者胜出（§5.4）。
public interface IPreviewProvider
{
    int Priority { get; }
    bool CanPreview(FileItem item);
    ValueTask<PreviewContent> LoadAsync(FileItem item, PreviewOptions options, CancellationToken ct);
}

/// PDF 渲染：桌面 PDFium、Android PdfRenderer 各一实现（隔离，§5.4）。
public interface IPdfRenderer : IAsyncDisposable
{
    ValueTask<int> GetPageCountAsync(FileLocator pdf, string? password, CancellationToken ct);
    ValueTask<DecodedImage?> RenderPageAsync(FileLocator pdf, int pageIndex, int targetWidthPx, string? password, CancellationToken ct);
}
