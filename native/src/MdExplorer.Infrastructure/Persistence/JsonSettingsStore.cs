using System.Text.Json;
using System.Text.Json.Serialization;
using MdExplorer.Application.Settings;

namespace MdExplorer.Infrastructure.Persistence;

/// STJ 源生成上下文（P2：AOT/裁剪安全）。
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AppSettings))]
public sealed partial class SettingsJsonContext : JsonSerializerContext
{
}
/// 设置持久化：System.Text.Json 源生成（AOT 安全）。
public sealed class JsonSettingsStore : ISettingsService
{
    private readonly string _path;

    public AppSettings Current { get; }
    public event EventHandler? Changed;

    public JsonSettingsStore(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _path = Path.Combine(dataDirectory, "settings.json");
        Current = Load();
        Current.PropertyChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    private AppSettings Load()
    {
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(_path), SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public ValueTask SaveAsync(CancellationToken ct)
    {
        File.WriteAllText(_path, JsonSerializer.Serialize(Current, SettingsJsonContext.Default.AppSettings));
        return ValueTask.CompletedTask;
    }
}
