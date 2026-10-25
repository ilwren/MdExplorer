using System.Text.Json;

namespace MdExplorer.Desktop;

internal static class FavoriteStore
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MdExplorer", "favorites.json");

    public static List<string> Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return new List<string>();
            var savedPaths = JsonSerializer.Deserialize<List<string?>>(File.ReadAllText(StorePath)) ?? new List<string?>();
            var paths = new List<string>();
            foreach (var path in savedPaths)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                try { paths.Add(Path.GetFullPath(path)); }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { }
            }
            return paths.Distinct(PathComparer).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new List<string>();
        }
    }

    public static void Save(IEnumerable<string> paths)
    {
        var directory = Path.GetDirectoryName(StorePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = StorePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(paths.Distinct(PathComparer)));
        File.Move(temporaryPath, StorePath, true);
    }
}
