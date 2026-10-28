using MdExplorer.Domain.Search;

namespace MdExplorer.Application.Search;

/// 遍历式搜索（无索引兜底）：预算时间内流式产出命中（§5.6）。
public sealed class TraversalSearchService : ISearchService
{
    private readonly Platform.Abstractions.FileSystem.IFileSystemService _files;

    public TraversalSearchService(Platform.Abstractions.FileSystem.IFileSystemService files)
        => _files = files;

    public async IAsyncEnumerable<SearchHit> SearchAsync(SearchQuery query, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var stack = new Stack<Domain.Files.FileLocator>();
        stack.Push(query.Scope);
        var text = query.Text.Trim();
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var folder = stack.Pop();
            await foreach (var item in _files.ListAsync(folder, new Platform.Abstractions.FileSystem.ListOptions(query.Filters?.IncludeHidden ?? false), ct))
            {
                ct.ThrowIfCancellationRequested();
                if (item.Kind == Domain.Files.FileKind.Folder)
                {
                    if (query.Recursive) stack.Push(item.Locator);
                    continue;
                }
                if (text.Length > 0 && !item.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (query.Filters is { } f)
                {
                    if (f.Kinds is { Count: > 0 } kinds && !kinds.Contains(item.Kind)) continue;
                    if (f.MinSize is { } min && (item.Length ?? 0) < min) continue;
                    if (f.MaxSize is { } max && (item.Length ?? long.MaxValue) > max) continue;
                    if (f.ModifiedAfter is { } after && item.LastWriteTime < after) continue;
                    if (f.ModifiedBefore is { } before && item.LastWriteTime > before) continue;
                    if (f.Extension is { Length: > 0 } ext &&
                        !string.Equals(item.Extension?.TrimStart('.'), ext.TrimStart('.'), StringComparison.OrdinalIgnoreCase))
                        continue;
                }
                yield return new SearchHit(item, null, DescribeParent(item));
            }
        }
    }

    private static string DescribeParent(Domain.Files.FileItem item)
    {
        if (!item.Locator.IsLocalPath) return string.Empty;
        var path = item.Locator.ToLocalPath();
        var idx = path.Replace('\\', '/').LastIndexOf('/');
        return idx > 0 ? path[..idx] : string.Empty;
    }
}
