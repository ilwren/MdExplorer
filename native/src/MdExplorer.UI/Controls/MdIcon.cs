using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace MdExplorer.UI.Controls;

/// <summary>
/// 轻量图标控件：名称 → 内置几何数据（AOT 安全，无字体/反射依赖）。
/// </summary>
public sealed class MdIcon : UserControl
{
    public static readonly StyledProperty<string?> IconNameProperty =
        AvaloniaProperty.Register<MdIcon, string?>(nameof(IconName));

    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<MdIcon, double>(nameof(IconSize), 20);

    private readonly Path _path = new() { Stretch = Stretch.Uniform };

    public MdIcon()
    {
        Content = _path;
        Width = IconSize;
        Height = IconSize;
        _path.Data = Geometry.Parse(IconStore.Data["file"]);
    }

    public string? IconName
    {
        get => GetValue(IconNameProperty);
        set => SetValue(IconNameProperty, value);
    }

    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IconNameProperty)
        {
            var name = IconName;
            _path.Data = Geometry.Parse(
                name is not null && IconStore.Data.TryGetValue(name, out var d) ? d : IconStore.Data["file"]);
        }
        else if (change.Property == IconSizeProperty)
        {
            Width = IconSize;
            Height = IconSize;
        }
        else if (change.Property == ForegroundProperty)
        {
            _path.Fill = Foreground;
        }
    }
}

/// 图标几何数据（Material 图标 24px 路径的精简集）。
public static class IconStore
{
    public static readonly IReadOnlyDictionary<string, string> Data = new Dictionary<string, string>
    {
        ["file"] = "M6 2h8l6 6v12a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2zm7 1.5V9h5.5",
        ["folder"] = "M10 4H4a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-8l-2-2z",
        ["home"] = "M12 3l9 8h-3v9h-5v-6h-2v6H6v-9H3l9-8z",
        ["search"] = "M15.5 14h-.79l-.28-.27a6.5 6.5 0 1 0-.7.7l.27.28v.79l5 4.99L20.49 19l-4.99-5zm-6 0A4.5 4.5 0 1 1 14 9.5 4.5 4.5 0 0 1 9.5 14z",
        ["star"] = "M12 17.27L18.18 21l-1.64-7.03L22 9.24l-7.19-.61L12 2 9.19 8.63 2 9.24l5.46 4.73L4.82 21z",
        ["schedule"] = "M11.99 2C6.47 2 2 6.48 2 12s4.47 10 9.99 10C17.52 22 22 17.52 22 12S17.52 2 11.99 2zM12 20a8 8 0 1 1 0-16 8 8 0 0 1 0 16zm.5-13H11v6l5.25 3.15.75-1.23-4.5-2.67z",
        ["delete"] = "M6 19a2 2 0 0 0 2 2h8a2 2 0 0 0 2-2V7H6v12zM19 4h-3.5l-1-1h-5l-1 1H5v2h14V4z",
        ["settings"] = "M19.14 12.94a7.07 7.07 0 0 0 0-1.88l2.03-1.58a.49.49 0 0 0 .12-.62l-1.92-3.32a.49.49 0 0 0-.59-.22l-2.39.96a7.03 7.03 0 0 0-1.63-.94l-.36-2.54a.48.48 0 0 0-.48-.41h-3.84a.48.48 0 0 0-.48.41l-.36 2.54c-.59.24-1.13.56-1.63.94l-2.39-.96a.49.49 0 0 0-.59.22L2.71 8.86a.49.49 0 0 0 .12.62l2.03 1.58a7.07 7.07 0 0 0 0 1.88l-2.03 1.58a.49.49 0 0 0-.12.62l1.92 3.32c.12.22.38.3.59.22l2.39-.96c.5.38 1.04.7 1.63.94l.36 2.54c.04.24.24.41.48.41h3.84c.24 0 .44-.17.48-.41l.36-2.54c.59-.24 1.13-.56 1.63-.94l2.39.96c.22.08.47 0 .59-.22l1.92-3.32a.49.49 0 0 0-.12-.62l-2.03-1.58zM12 15.6A3.6 3.6 0 1 1 15.6 12 3.6 3.6 0 0 1 12 15.6z",
        ["arrow_back"] = "M20 11H7.83l5.59-5.59L12 4l-8 8 8 8 1.41-1.41L7.83 13H20v-2z",
        ["arrow_forward"] = "M12 4l-1.41 1.41L16.17 11H4v2h12.17l-5.58 5.59L12 20l8-8z",
        ["arrow_upward"] = "M4 12l1.41 1.41L11 7.83V20h2V7.83l5.58 5.59L20 12l-8-8-8 8z",
        ["refresh"] = "M17.65 6.35A7.96 7.96 0 0 0 12 4a8 8 0 1 0 7.73 10h-2.08A6 6 0 1 1 12 6c1.66 0 3.14.69 4.22 1.78L13 11h7V4l-2.35 2.35z",
        ["more_vert"] = "M12 8a2 2 0 1 0 0-4 2 2 0 0 0 0 4zm0 2a2 2 0 1 0 0 4 2 2 0 0 0 0-4zm0 6a2 2 0 1 0 0 4 2 2 0 0 0 0-4z",
        ["close"] = "M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z",
        ["check"] = "M9 16.17L4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z",
        ["add"] = "M19 13h-6v6h-2v-6H5v-2h6V5h2v6h6v2z",
        ["edit"] = "M3 17.25V21h3.75L17.81 9.94l-3.75-3.75L3 17.25zM20.71 7.04a1 1 0 0 0 0-1.41l-2.34-2.34a1 1 0 0 0-1.41 0l-1.83 1.83 3.75 3.75 1.83-1.83z",
        ["save"] = "M17 3H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2V7l-4-4zm-5 16a3 3 0 1 1 0-6 3 3 0 0 1 0 6zm3-10H5V5h10v4z",
        ["content_copy"] = "M16 1H4a2 2 0 0 0-2 2v14h2V3h12V1zm3 4H8a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h11a2 2 0 0 0 2-2V7a2 2 0 0 0-2-2zm0 16H8V7h11v14z",
        ["content_cut"] = "M9.64 7.64a3 3 0 1 0-1.36 1.36l1.36 2 1.36-2-1.36-1.36zM6 4a2 2 0 1 1-2 2 2 2 0 0 1 2-2zm12 13.5a2 2 0 1 1 2-2 2 2 0 0 1-2 2zM6 18a2 2 0 1 1-2 2 2 2 0 0 1 2-2zm12-6.5L13.5 6l-1.5 2.5L10.5 6 6 11.5l1.5 2.5L6 16.5 8 18l3.5-4.5L15 18l2-1.5-2.5-2.5L18 11.5z",
        ["content_paste"] = "M19 2h-4.18A3 3 0 0 0 12 0a3 3 0 0 0-2.82 2H5a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2V4a2 2 0 0 0-2-2zm-7 0a1 1 0 1 1-1 1 1 1 0 0 1 1-1zm7 20H5V4h2v3h10V4h2z",
        ["folder_open"] = "M20 6h-8l-2-2H4a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2zm0 12H4V8h16z",
        ["description"] = "M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8l-6-6zM6 20V4h7v5h5v11H6z",
        ["image"] = "M21 19V5a2 2 0 0 0-2-2H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2zM8.5 13.5l2.5 3.01L14.5 12l4.5 6H5l3.5-4.5z",
        ["movie"] = "M18 4l2 4h-3l-2-4h-2l2 4h-3l-2-4H8l2 4H7L5 4H4a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2V4h-4z",
        ["audio_file"] = "M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8l-6-6zm-2 16a3 3 0 1 1 0-6 3 3 0 0 1 0 6zm1-6.5V9l4-2v5.5a2.5 2.5 0 1 1-1-2V9.5z",
        ["archive"] = "M20.54 5.23l-1.39-1.68A2 2 0 0 0 17.72 3H6.28a2 2 0 0 0-1.43.55L3.46 5.23A2 2 0 0 0 3 6.68V19a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2V6.68a2 2 0 0 0-.46-1.45zM12 17.5L6.5 12H10V8h4v4h3.5L12 17.5z",
        ["code"] = "M9.4 16.6L4.8 12l4.6-4.6L8 6l-6 6 6 6 1.4-1.4zm5.2 0L19.2 12l-4.6-4.6L16 6l6 6-6 6-1.4-1.4z",
        ["data_usage"] = "M12 2a10 10 0 1 0 10 10A10 10 0 0 0 12 2zm0 18a8 8 0 1 1 8-8 8 8 0 0 1-8 8zm0-14a6 6 0 1 0 6 6 6 6 0 0 0-6-6zm0 10a4 4 0 1 1 4-4 4 4 0 0 1-4 4z",
        ["manage_search"] = "M15.5 14h-.79l-.28-.27a6.5 6.5 0 1 0-.7.7l.27.28v.79l5 4.99L20.49 19l-4.99-5zm-6 0A4.5 4.5 0 1 1 14 9.5 4.5 4.5 0 0 1 9.5 14zM3 3h2v6H3zm4 4h2v2H7zm-4 4h2v2H3z",
        ["unarchive"] = "M20.54 5.23l-1.39-1.68A2 2 0 0 0 17.72 3H6.28a2 2 0 0 0-1.43.55L3.46 5.23A2 2 0 0 0 3 6.68V19a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2V6.68a2 2 0 0 0-.46-1.45zM11 17H9v-2H7v-2h2v-2h2v2h2v2h-2v2zm3-6.5V6l-4 2v1.5a2.5 2.5 0 1 0 1 2z",
        ["vertical_split"] = "M3 5v14a2 2 0 0 0 2 2h4V3H5a2 2 0 0 0-2 2zm8-2v18h8a2 2 0 0 0 2-2V5a2 2 0 0 0-2-2z",
        ["volume_up"] = "M3 9v6h4l5 5V4L7 9H3zm13.5 3A4.5 4.5 0 0 0 14 7.97v8.05A4.47 4.47 0 0 0 16.5 12zM14 3.23v2.06a7 7 0 0 1 0 13.42v2.06a9 9 0 0 0 0-17.54z",
        ["policy"] = "M12 2a10 10 0 1 0 10 10A10 10 0 0 0 12 2zm-1 15l-4-4 1.41-1.41L11 14.17l5.59-5.59L18 10l-7 7z",
        ["update"] = "M21 10.12h-6.78l2.74-2.82A6 6 0 0 0 12 3a6 6 0 0 0-5.66 8.06A6 6 0 0 0 12 21a5.9 5.9 0 0 0 4.95-2.57l1.63 1.63 1.42-1.41zM12 19a4 4 0 1 1 4-4 4 4 0 0 1-4 4z",
        ["toc"] = "M3 9h14V7H3v2zm0 4h14v-2H3v2zm0 4h14v-2H3v2zm16 0h2v-2h-2v2zm0-10v2h2V7h-2zm0 6h2v-2h-2v2z",
        ["open_in_new"] = "M19 19H5V5h7V3H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7h-2v7zM14 3v2h3.59l-9.83 9.83 1.41 1.41L19 6.41V10h2V3h-7z",
        ["share"] = "M18 16.08a2.9 2.9 0 0 0-1.96.77L8.91 12.7a3.27 3.27 0 0 0 0-1.39l7.05-4.11A2.99 2.99 0 1 0 15 5a3.1 3.1 0 0 0 .09.72L8.04 9.81a3 3 0 1 0 0 4.37l7.12 4.16c-.05.21-.09.43-.09.66a2.92 2.92 0 1 0 2.92-2.92z",
        ["info"] = "M12 2a10 10 0 1 0 10 10A10 10 0 0 0 12 2zm1 15h-2v-6h2zm0-8h-2V7h2z",
        ["sort"] = "M3 18h6v-2H3v2zM3 6v2h18V6H3zm0 7h12v-2H3v2z",
        ["grid_view"] = "M3 3h8v8H3V3zm10 0h8v8h-8V3zM3 13h8v8H3v-8zm10 0h8v8h-8v-8z",
        ["view_list"] = "M4 14h4v4H4v-4zm0-6h4v4H4V8zm0-6h4v4H4V2zm6 0h14v4H10V2zm0 6h14v4H10V8zm0 6h14v4H10v-4zm0 6h14v4H10v-4z",
        ["filter_alt"] = "M4 5h16v2l-6 6v5l-4-2v-3L4 7z",
        ["swap_horiz"] = "M6.99 11L3 15l3.99 4v-3H14v-2H6.99v-3zM21 9l-3.99-4v3H10v2h7.01v3L21 9z",
        ["fact_check"] = "M20 3H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2V5a2 2 0 0 0-2-2zm-9 11.5l-3.5-3.5 1.41-1.41L11 11.67l5.59-5.59L18 7.5l-7 7z",
        ["my_location"] = "M12 8a4 4 0 1 0 4 4 4 4 0 0 0-4-4zm8.94 3A9 9 0 0 0 13 3.06V1h-2v2.06A9 9 0 0 0 3.06 11H1v2h2.06A9 9 0 0 0 11 20.94V23h2v-2.06A9 9 0 0 0 20.94 13H23v-2h-2.06zM12 19a7 7 0 1 1 7-7 7 7 0 0 1-7 7z",
    };
}
