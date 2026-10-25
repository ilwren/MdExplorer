namespace MdExplorer.Desktop;

internal enum SortMode { Name, Modified, Size, Type }

/// <summary>Desktop-independent filesystem rules for local directory browsing and search.</summary>
internal sealed class LocalFileSystemService
{
    private const int MaximumResults = 1000;
    private const int MaximumDepth = 32;

    public List<FileSystemInfo> ListDirectory(
        string path, string query, bool showHidden, bool searchSubfolders,
        SortMode sortMode, bool descending, CancellationToken cancellationToken)
    {
        List<FileSystemInfo> entries;
        if (searchSubfolders && !string.IsNullOrWhiteSpace(query))
            entries = SearchSubtree(path, query, showHidden, cancellationToken);
        else
            entries = Directory.EnumerateFileSystemEntries(path)
                .Select(itemPath => (FileSystemInfo)(Directory.Exists(itemPath) ? new DirectoryInfo(itemPath) : new FileInfo(itemPath)))
                .Where(entry => showHidden || !IsHidden(entry))
                .Where(entry => string.IsNullOrEmpty(query) || entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(MaximumResults).ToList();

        return SortEntries(entries, sortMode, descending);
    }

    private static List<FileSystemInfo> SearchSubtree(string root, string query, bool showHidden, CancellationToken cancellationToken)
    {
        var found = new List<FileSystemInfo>();
        var pending = new Stack<(string Path, int Depth)>();
        var visited = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        pending.Push((root, 0));
        while (pending.Count > 0 && found.Count < MaximumResults)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (directory, depth) = pending.Pop();
            string canonical;
            try { canonical = Path.GetFullPath(directory); }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { continue; }
            if (!visited.Add(canonical)) continue;
            try
            {
                foreach (var child in Directory.EnumerateFileSystemEntries(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var attributes = File.GetAttributes(child);
                        if (!showHidden && ((attributes & FileAttributes.Hidden) != 0 || Path.GetFileName(child).StartsWith(".", StringComparison.Ordinal))) continue;
                        var isDirectory = (attributes & FileAttributes.Directory) != 0;
                        FileSystemInfo entry = isDirectory ? new DirectoryInfo(child) : new FileInfo(child);
                        if (entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) found.Add(entry);
                        if (isDirectory && depth < MaximumDepth - 1 && (attributes & FileAttributes.ReparsePoint) == 0) pending.Push((child, depth + 1));
                        if (found.Count >= MaximumResults) break;
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return found;
    }

    private static List<FileSystemInfo> SortEntries(List<FileSystemInfo> entries, SortMode mode, bool descending)
    {
        static IOrderedEnumerable<FileSystemInfo> SortGroup(IEnumerable<FileSystemInfo> group, SortMode current, bool reverse) => current switch
        {
            SortMode.Modified => reverse ? group.OrderByDescending(entry => entry.LastWriteTimeUtc).ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase) : group.OrderBy(entry => entry.LastWriteTimeUtc).ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase),
            SortMode.Size => reverse ? group.OrderByDescending(entry => entry is FileInfo file ? file.Length : -1L).ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase) : group.OrderBy(entry => entry is FileInfo file ? file.Length : -1L).ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase),
            SortMode.Type => reverse ? group.OrderByDescending(entry => entry is DirectoryInfo ? "" : entry.Extension, StringComparer.CurrentCultureIgnoreCase).ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase) : group.OrderBy(entry => entry is DirectoryInfo ? "" : entry.Extension, StringComparer.CurrentCultureIgnoreCase).ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => reverse ? group.OrderByDescending(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase) : group.OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
        };
        return SortGroup(entries.Where(entry => entry is DirectoryInfo), mode, descending)
            .Concat(SortGroup(entries.Where(entry => entry is not DirectoryInfo), mode, descending)).ToList();
    }

    private static bool IsHidden(FileSystemInfo entry)
    {
        if (entry.Name.StartsWith(".", StringComparison.Ordinal)) return true;
        try { return (File.GetAttributes(entry.FullName) & FileAttributes.Hidden) != 0; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
