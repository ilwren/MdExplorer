using MdExplorer.Domain.Files;

namespace MdExplorer.Domain.Search;

/// 搜索查询与过滤器（§5.6）。
public sealed record SearchFilters(
    IReadOnlySet<FileKind>? Kinds = null,
    long? MinSize = null,
    long? MaxSize = null,
    DateTimeOffset? ModifiedAfter = null,
    DateTimeOffset? ModifiedBefore = null,
    string? Extension = null,
    bool IncludeHidden = false);

public sealed record SearchQuery(
    string Text,
    FileLocator Scope,
    bool Recursive = true,
    SearchFilters? Filters = null,
    bool SearchContents = false);

/// 命中：条目 + 可选正文上下文片段 + 父路径。
public sealed record SearchHit(FileItem Item, string? Snippet = null, string? ParentPath = null);

public sealed record IndexStatus(
    IReadOnlyList<FileLocator> Roots,
    bool Paused,
    bool ContentEnabled,
    DateTimeOffset? UpdatedAt,
    long FileCount,
    long SizeBytes,
    long StaleCount);
