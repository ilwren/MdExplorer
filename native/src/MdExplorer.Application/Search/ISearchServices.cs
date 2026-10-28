using MdExplorer.Domain.Search;

namespace MdExplorer.Application.Search;

/// 搜索编排入口（§5.6）：当前文件夹过滤 → 实时递归遍历 → 已建索引（启用时优先）。
public interface ISearchService
{
    IAsyncEnumerable<SearchHit> SearchAsync(SearchQuery query, CancellationToken ct);
}

/// 索引是可选增强：仅索引用户勾选纳入的根（默认无）（§5.6）。
public interface ISearchIndexService
{
    ValueTask<IndexStatus> GetStatusAsync(CancellationToken ct);
    ValueTask SetIndexedRootsAsync(IReadOnlyList<Domain.Files.FileLocator> roots, bool includeContents, CancellationToken ct);
    ValueTask<IndexStatus> RebuildAsync(CancellationToken ct);
    ValueTask<IndexStatus> ClearAsync(CancellationToken ct);
    ValueTask<IndexStatus> SetPausedAsync(bool paused, CancellationToken ct);
    ValueTask<IndexStatus> SetContentEnabledAsync(bool enabled, CancellationToken ct);
    IAsyncEnumerable<SearchHit> QueryAsync(SearchQuery query, CancellationToken ct);
}
