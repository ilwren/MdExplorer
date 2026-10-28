using MdExplorer.Domain.Files;
using MdExplorer.Platform.Abstractions.Preview;

namespace MdExplorer.Infrastructure.PreviewProviders;

/// 文本/代码/Markdown 预览（§5.4：TextContent）。
public sealed class TextPreviewProvider : IPreviewProvider
{
    public int Priority => 10;

    public bool CanPreview(FileItem item)
        => item.Kind is FileKind.Text or FileKind.Code ||
           (item.Kind == FileKind.Document && item.Extension is ".md" or ".mdx");

    public async ValueTask<PreviewContent> LoadAsync(FileItem item, PreviewOptions options, CancellationToken ct)
    {
        var path = item.Locator.ToLocalPath();
        await using var stream = File.OpenRead(path);
        var len = (int)Math.Min(stream.Length, options.MaxTextBytes);
        var buffer = new byte[len];
        var read = await stream.ReadAsync(buffer.AsMemory(0, len), ct);
        var text = System.Text.Encoding.UTF8.GetString(buffer, 0, read);
        var isMd = item.Extension is ".md" or ".mdx";
        return new TextContent(text, item.Extension?.TrimStart('.'), isMd);
    }
}

/// 图片预览（§5.4：ImageContent 交给 Avalonia Image 从路径加载）。
public sealed class ImagePreviewProvider : IPreviewProvider
{
    public int Priority => 20;
    public bool CanPreview(FileItem item) => item.Kind == FileKind.Image;
    public ValueTask<PreviewContent> LoadAsync(FileItem item, PreviewOptions options, CancellationToken ct)
        => ValueTask.FromResult<PreviewContent>(new ImageContent(item.Locator.ToLocalPath()));
}

/// 压缩包预览（§5.4：ArchiveContent 条目树）。
public sealed class ArchivePreviewProvider : IPreviewProvider
{
    private readonly MdExplorer.Platform.Abstractions.Archives.IArchiveService _zip;
    public ArchivePreviewProvider(MdExplorer.Platform.Abstractions.Archives.IArchiveService zip) => _zip = zip;
    public int Priority => 15;
    public bool CanPreview(FileItem item) => item.Kind == FileKind.Archive && item.Extension == ".zip";

    public ValueTask<PreviewContent> LoadAsync(FileItem item, PreviewOptions options, CancellationToken ct)
    {
        var entries = _zip.ListAll(item.Locator.ToLocalPath());
        IReadOnlyList<ArchiveEntry> list = entries.IsSuccess
            ? entries.Value
            : [];
        return ValueTask.FromResult<PreviewContent>(new ArchiveContent(list));
    }
}

/// 媒体与 PDF：内容模型 + 外部打开兜底（§6.5 降级策略；PDFium 渲染为隔离 Spike）。
public sealed class MediaPreviewProvider : IPreviewProvider
{
    public int Priority => 5;

    public bool CanPreview(FileItem item)
        => item.Kind is FileKind.Video or FileKind.Audio || item.Extension == ".pdf";

    public ValueTask<PreviewContent> LoadAsync(FileItem item, PreviewOptions options, CancellationToken ct)
    {
        PreviewContent content = item.Kind switch
        {
            FileKind.Video => new VideoContent(item.Locator.ToLocalPath(), null),
            FileKind.Audio => new AudioContent(item.Locator.ToLocalPath(), item.Name),
            _ when item.Extension == ".pdf" => new PdfContent(item.Locator.ToLocalPath(), -1),
            _ => new MetadataOnlyContent(item.Name),
        };
        return ValueTask.FromResult(content);
    }
}

/// 最终兜底（§5.4）。
public sealed class MetadataPreviewProvider : IPreviewProvider
{
    public int Priority => 0;
    public bool CanPreview(FileItem item) => true;

    public ValueTask<PreviewContent> LoadAsync(FileItem item, PreviewOptions options, CancellationToken ct)
        => ValueTask.FromResult<PreviewContent>(
            new MetadataOnlyContent($"{item.Kind} · {(item.Length is { } l ? $"{l} 字节" : "文件夹")}"));
}

/// 预览编排：按 Priority 从高到低首个 CanPreview 者胜出（§5.4）。
public sealed class PreviewProviderChain : IPreviewService
{
    private readonly IReadOnlyList<IPreviewProvider> _providers;

    public PreviewProviderChain(IEnumerable<IPreviewProvider> providers)
        => _providers = providers.OrderByDescending(p => p.Priority).ToList();

    public async ValueTask<PreviewContent> LoadAsync(FileItem item, PreviewOptions options, CancellationToken ct)
    {
        foreach (var provider in _providers)
        {
            if (provider.CanPreview(item))
                return await provider.LoadAsync(item, options, ct);
        }
        return new MetadataOnlyContent(item.Name);
    }
}
