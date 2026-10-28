using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using MdExplorer.Application.Search;
using MdExplorer.Domain.Files;
using MdExplorer.Domain.Search;

namespace MdExplorer.Infrastructure.Persistence;

/// 本地搜索索引（§5.6）：SQLite files 表 + 正文片段（内容索引默认关闭）。
/// 索引只保存在本机；清空/暂停/根目录管理对 UI 同屏可达（概念稿 §4.1 隐私卡）。
public sealed class SqliteSearchIndexService : ISearchIndexService
{
    private readonly string _dbPath;
    private readonly object _gate = new();

    public SqliteSearchIndexService(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _dbPath = Path.Combine(dataDirectory, "index.db");
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS files (
                path TEXT PRIMARY KEY, name TEXT NOT NULL, size INTEGER NOT NULL,
                mtime INTEGER NOT NULL, kind TEXT NOT NULL, ext TEXT,
                root TEXT NOT NULL, snippet TEXT);
            """;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return conn;
    }

    private string GetMeta(string key, string fallback)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string ?? fallback;
    }

    private void SetMeta(string key, string value)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO meta (key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = $v";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    private IReadOnlyList<FileLocator> LoadRoots()
        => GetMeta("roots", string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(FileLocator.FromPath)
            .ToList();

    public ValueTask<IndexStatus> GetStatusAsync(CancellationToken ct) => ValueTask.FromResult(BuildStatus());

    private IndexStatus BuildStatus()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM files";
        var count = Convert.ToInt64(cmd.ExecuteScalar());
        var updatedRaw = GetMeta("updatedAt", "0");
        var updated = long.TryParse(updatedRaw, out var ts) && ts > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(ts)
            : (DateTimeOffset?)null;
        long size = 0;
        try { size = new FileInfo(_dbPath).Length; } catch { }
        return new IndexStatus(
            LoadRoots(),
            GetMeta("paused", "0") == "1",
            GetMeta("content", "0") == "1",
            updated,
            count,
            size,
            StaleCount: 0);
    }

    public async ValueTask SetIndexedRootsAsync(IReadOnlyList<FileLocator> roots, bool includeContents, CancellationToken ct)
    {
        SetMeta("roots", string.Join('\n', roots.Select(r => r.ToLocalPath())));
        SetMeta("content", includeContents ? "1" : "0");
        await RebuildAsync(ct);
    }

    public async ValueTask<IndexStatus> RebuildAsync(CancellationToken ct)
    {
        if (GetMeta("paused", "0") == "1") return BuildStatus();
        var roots = LoadRoots();
        var contentEnabled = GetMeta("content", "0") == "1";
        var mime = new FileSystem.MimeTypeService();
        var files = new List<(string Path, string Name, long Size, long Mtime, string Kind, string? Ext, string Root, string? Snippet)>();
        var deadline = DateTime.UtcNow.AddSeconds(15);
        foreach (var root in roots)
        {
            var rootPath = root.ToLocalPath();
            if (!Directory.Exists(rootPath)) continue;
            var stack = new Stack<string>();
            stack.Push(rootPath);
            while (stack.Count > 0 && DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                var dir = stack.Pop();
                IEnumerable<string> entries;
                try { entries = Directory.EnumerateFileSystemEntries(dir); } catch { continue; }
                foreach (var full in entries)
                {
                    if (DateTime.UtcNow >= deadline) break;
                    var name = Path.GetFileName(full);
                    if (name.StartsWith('.')) continue;
                    if (Directory.Exists(full))
                    {
                        if (name is "node_modules" or ".git" or "dist" or "build") continue;
                        stack.Push(full);
                        continue;
                    }
                    FileInfo info;
                    try { info = new FileInfo(full); } catch { continue; }
                    string? snippet = null;
                    if (contentEnabled && info.Length <= 512 * 1024 &&
                        mime.GetKind(name, false) is FileKind.Text or FileKind.Code or FileKind.Document)
                    {
                        try
                        {
                            var body = await File.ReadAllTextAsync(full, ct).ConfigureAwait(false);
                            snippet = body.Replace('\n', ' ');
                            if (snippet.Length > 200) snippet = snippet[..200];
                        }
                        catch { }
                    }
                    files.Add((full, name, info.Length, info.LastWriteTimeUtc.Ticks,
                        mime.GetKind(name, false).ToString(), info.Extension, rootPath, snippet));
                }
                await Task.Yield();
            }
        }

        lock (_gate)
        {
            using var conn = Open();
            using var tx = conn.BeginTransaction();
            using (var clear = conn.CreateCommand())
            {
                clear.CommandText = "DELETE FROM files";
                clear.ExecuteNonQuery();
            }
            foreach (var f in files)
            {
                using var ins = conn.CreateCommand();
                ins.CommandText =
                    "INSERT INTO files (path, name, size, mtime, kind, ext, root, snippet) VALUES ($p,$n,$s,$m,$k,$e,$r,$c)";
                ins.Parameters.AddWithValue("$p", f.Path);
                ins.Parameters.AddWithValue("$n", f.Name);
                ins.Parameters.AddWithValue("$s", f.Size);
                ins.Parameters.AddWithValue("$m", f.Mtime);
                ins.Parameters.AddWithValue("$k", f.Kind);
                ins.Parameters.AddWithValue("$e", (object?)f.Ext ?? DBNull.Value);
                ins.Parameters.AddWithValue("$r", f.Root);
                ins.Parameters.AddWithValue("$c", (object?)f.Snippet ?? DBNull.Value);
                ins.ExecuteNonQuery();
            }
            tx.Commit();
            SetMeta("updatedAt", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());
        }
        return BuildStatus();
    }

    public ValueTask<IndexStatus> ClearAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM files";
            cmd.ExecuteNonQuery();
            SetMeta("updatedAt", "0");
        }
        return ValueTask.FromResult(BuildStatus());
    }

    public ValueTask<IndexStatus> SetPausedAsync(bool paused, CancellationToken ct)
    {
        SetMeta("paused", paused ? "1" : "0");
        return ValueTask.FromResult(BuildStatus());
    }

    public ValueTask<IndexStatus> SetContentEnabledAsync(bool enabled, CancellationToken ct)
    {
        SetMeta("content", enabled ? "1" : "0");
        return ValueTask.FromResult(BuildStatus());
    }

    public async IAsyncEnumerable<SearchHit> QueryAsync(SearchQuery query, [EnumeratorCancellation] CancellationToken ct)
    {
        var text = query.Text.Trim();
        var since = query.Filters?.ModifiedAfter?.UtcTicks ?? 0L;
        var kindFilter = query.Filters?.Kinds is { Count: > 0 } k
            ? k.Select(x => x.ToString()).ToHashSet(StringComparer.Ordinal)
            : null;
        var rows = new List<(string Path, string Name, long Size, long Mtime, string Kind, string? Ext, string? Snippet)>();
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT path, name, size, mtime, kind, ext, snippet FROM files WHERE ($t = '' OR lower(name) LIKE '%' || lower($t) || '%') AND mtime >= $since";
            cmd.Parameters.AddWithValue("$t", text);
            cmd.Parameters.AddWithValue("$since", since);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                rows.Add((reader.GetString(0), reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3),
                    reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6)));
            }
        }
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            if (kindFilter is not null && !kindFilter.Contains(row.Kind)) continue;
            if (query.Scope.Value.Length > 0 && query.Scope.IsLocalPath)
            {
                var scope = query.Scope.ToLocalPath();
                if (!row.Path.StartsWith(scope, StringComparison.Ordinal)) continue;
            }
            var item = new FileItem(
                FileLocator.FromPath(row.Path),
                row.Name,
                Enum.TryParse<FileKind>(row.Kind, out var k2) ? k2 : FileKind.Unknown,
                row.Size,
                new DateTimeOffset(row.Mtime, TimeSpan.Zero),
                false, false, false, row.Ext, null);
            yield return new SearchHit(item, row.Snippet, Path.GetDirectoryName(row.Path));
            await Task.Yield();
        }
    }
}
