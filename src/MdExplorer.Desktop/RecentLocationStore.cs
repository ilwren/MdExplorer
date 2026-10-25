using System.Text.Json;

namespace MdExplorer.Desktop;

internal static class RecentLocationStore
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MdExplorer", "recent-locations.json");

    public static List<string> Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return new List<string>();
            return (JsonSerializer.Deserialize<List<string>>(File.ReadAllText(StorePath)) ?? new List<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Take(12).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new List<string>();
        }
    }

    public static void Save(IEnumerable<string> paths)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        var temporaryPath = StorePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(paths.Take(12)));
        File.Move(temporaryPath, StorePath, true);
    }
}
