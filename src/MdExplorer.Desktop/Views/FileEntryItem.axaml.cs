using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Md3.Avalonia.Controls;

namespace MdExplorer.Desktop.Views;

public sealed partial class FileEntryItem : UserControl
{
    private FileSystemInfo _entry = null!;
    private bool _toggleSelection;
    private bool _rangeSelection;

    public event Action<FileSystemInfo, bool, bool>? EntryActivated;
    public event Action<FileSystemInfo>? EntryOpenRequested;

    public FileEntryItem()
    {
        InitializeComponent();
    }

    public void Bind(FileSystemInfo entry, bool selected, bool gridMode)
    {
        _entry = entry;
        var isDirectory = entry is DirectoryInfo;
        var icon = isDirectory ? "▰" : MainWindow.IconFor(entry.Extension);
        var iconBrush = new SolidColorBrush(Color.Parse(isDirectory ? "#FFF6E2A2" : "#FFE8DEF8"));
        var name = entry.Name;
        var size = isDirectory ? "文件夹" : MainWindow.FormatSize(((FileInfo)entry).Length);
        ListName.Text = name;
        ListIconLabel.Text = icon;
        ListIconTile.Background = iconBrush;
        ListModified.Text = entry.LastWriteTime.ToString("g");
        ListSize.Text = size;
        GridName.Text = name;
        GridIconLabel.Text = icon;
        GridIconTile.Background = iconBrush;
        GridMetadata.Text = isDirectory ? "文件夹" : $"{size} · {entry.Extension.TrimStart('.').ToUpperInvariant()}";
        ListSurface.Variant = selected ? MdButtonVariant.Tonal : MdButtonVariant.Text;
        GridSurface.Variant = selected ? MdCardVariant.Filled : MdCardVariant.Outlined;
        ListSurface.IsVisible = !gridMode;
        GridSurface.IsVisible = gridMode;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _toggleSelection = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        _rangeSelection = !_toggleSelection && e.KeyModifiers.HasFlag(KeyModifiers.Shift);
    }

    private void OnClicked(object? sender, RoutedEventArgs e)
    {
        EntryActivated?.Invoke(_entry, _toggleSelection, _rangeSelection);
        _toggleSelection = false;
        _rangeSelection = false;
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e) => EntryOpenRequested?.Invoke(_entry);
}
