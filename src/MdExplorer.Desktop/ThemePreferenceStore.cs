namespace MdExplorer.Desktop;

/// <summary>Small, dependency-free persistence for the user's appearance preference.</summary>
internal static class ThemePreferenceStore
{
    private static readonly string PreferencePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MdExplorer",
        "theme-mode.txt");

    public static Md3.Avalonia.Themes.Dynamic.MdThemeMode Load()
    {
        try
        {
            return Enum.TryParse<Md3.Avalonia.Themes.Dynamic.MdThemeMode>(
                File.ReadAllText(PreferencePath).Trim(), ignoreCase: true, out var mode)
                ? mode
                : Md3.Avalonia.Themes.Dynamic.MdThemeMode.System;
        }
        catch (IOException) { return Md3.Avalonia.Themes.Dynamic.MdThemeMode.System; }
        catch (UnauthorizedAccessException) { return Md3.Avalonia.Themes.Dynamic.MdThemeMode.System; }
    }

    public static void Save(Md3.Avalonia.Themes.Dynamic.MdThemeMode mode)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PreferencePath)!);
            File.WriteAllText(PreferencePath, mode.ToString());
        }
        catch (IOException) { /* Theme preference is best-effort; never block file browsing. */ }
        catch (UnauthorizedAccessException) { /* Use the current-session choice if settings are read-only. */ }
    }
}
