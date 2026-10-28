using CommunityToolkit.Mvvm.ComponentModel;

namespace MdExplorer.Application.Settings;

/// 应用设置（§6.7）。主题/视图/排序/确认开关——与概念稿设置页对应。
public sealed partial class AppSettings : ObservableObject
{
    [ObservableProperty] private string _theme = "system";           // light | dark | system
    [ObservableProperty] private string _viewMode = "grid";          // grid | list
    [ObservableProperty] private string _sortBy = "name";
    [ObservableProperty] private bool _sortDescending;
    [ObservableProperty] private bool _showHidden;
    [ObservableProperty] private bool _reduceMotion;
    [ObservableProperty] private bool _confirmDelete = true;
}

public interface ISettingsService
{
    AppSettings Current { get; }
    event EventHandler? Changed;
    ValueTask SaveAsync(CancellationToken ct);
}
