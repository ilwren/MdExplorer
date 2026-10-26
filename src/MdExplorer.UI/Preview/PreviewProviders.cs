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
    string? Message = null,
    string? Markdown = null,
    string? HighlightLanguage = null);

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
