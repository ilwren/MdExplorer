using System.Text;
using Avalonia.Media.Imaging;
using MdExplorer.Domain;
using MdExplorer.Platform.Abstractions;
using MdExplorer.UI.ViewModels;

namespace MdExplorer.UI.Preview;

/// <summary>
/// 预览加载结果（数据进宿主、宿主渲染——插件系统一贯边界，见 docs/PLUGIN-SYSTEM.md §2/§6）。
/// 至多一个负载字段非空：位图 / 文本 / 压缩包条目 / 说明消息。
/// </summary>
public sealed record PreviewContent(
    Bitmap? Image = null,
    string? Text = null,
    IReadOnlyList<ArchiveEntryRowModel>? Entries = null,
    string? Message = null);

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
    public static PreviewProviderRegistry Create(IArchiveService archive) => new(
    [
        new ImagePreviewProvider(),
        new TextPreviewProvider(),
        new HexPreviewProvider(),
        new ArchivePreviewProvider(archive),
        new FallbackPreviewProvider(),
    ]);
}

internal sealed class ImagePreviewProvider : IPreviewProvider
{
    public string Id => "builtin.image";
    public int Priority => 100;
    public bool CanHandle(FileItem item) => item.Kind == FileKind.Image;

    public async Task<PreviewContent> LoadAsync(FileItem item, CancellationToken cancellationToken)
    {
        var bitmap = await Task.Run(() =>
        {
            using var stream = File.OpenRead(item.FullPath);
            return Bitmap.DecodeToWidth(stream, 1600);
        }, cancellationToken).ConfigureAwait(true);
        return new PreviewContent(Image: bitmap);
    }
}

internal sealed class TextPreviewProvider : IPreviewProvider
{
    private const long TextLimit = 512 * 1024;

    public string Id => "builtin.text";
    public int Priority => 100;
    public bool CanHandle(FileItem item) => item.Kind is FileKind.Text or FileKind.Code or FileKind.Data;

    public async Task<PreviewContent> LoadAsync(FileItem item, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(item.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var buffer = new byte[Math.Min(TextLimit, Math.Max(0, stream.Length))];
        var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(true);
        var text = Encoding.UTF8.GetString(buffer, 0, read);
        if (stream.Length > TextLimit)
            text += $"\n\n…（仅预览前 {Formatting.Bytes(TextLimit)}，文件共 {Formatting.Bytes(item.Length)}）";
        return new PreviewContent(Text: text);
    }
}

internal sealed class HexPreviewProvider : IPreviewProvider
{
    public string Id => "builtin.hex";
    public int Priority => 100;
    public bool CanHandle(FileItem item) => item.Kind == FileKind.Unknown;

    public async Task<PreviewContent> LoadAsync(FileItem item, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(item.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var buffer = new byte[Math.Min(512, Math.Max(0, stream.Length))];
        var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(true);
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
    public bool CanHandle(FileItem item) => item.Kind == FileKind.Archive && archive.CanOpen(item.FullPath);

    public async Task<PreviewContent> LoadAsync(FileItem item, CancellationToken cancellationToken)
    {
        var entries = await archive.ListEntriesAsync(item.FullPath, 500, cancellationToken).ConfigureAwait(true);
        var rows = entries
            .Select(e => new ArchiveEntryRowModel(e.FullName, e.IsFolder ? "" : Formatting.Bytes(e.Length)))
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
            FileKind.Document when FileKindClassifier.IsPdf(item) => "PDF 内置预览将在后续版本提供，可先用系统应用打开。",
            FileKind.Video or FileKind.Audio => "内置播放将在后续版本提供，可先用系统应用打开。",
            _ => "此类型暂不支持内置预览，可先用系统应用打开。",
        }));
}
