using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MdExplorer.Domain.Files;
using MdExplorer.UI.Services;

namespace MdExplorer.UI.ViewModels;

public sealed record ShortcutRow(string Label, string IconName, Destination Destination);
public sealed record VolumeRow(string Label, string Detail);

/// 主页（概念稿 §4.1 主页：快捷入口 + 存储概览）。
public sealed partial class HomeViewModel : ObservableObject
{
    private readonly UiServices _svc;

    public ObservableCollection<ShortcutRow> Shortcuts { get; } = new();
    public ObservableCollection<VolumeRow> Volumes { get; } = new();

    [ObservableProperty] private string _storageSummary = "";

    public HomeViewModel(UiServices svc)
    {
        _svc = svc;
        _ = LoadAsync();
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        Shortcuts.Clear();
        void Add(string label, string icon, Destination d) => Shortcuts.Add(new ShortcutRow(label, icon, d));

        Add("主文件夹", "home", new Destination.Home());
        foreach (var (folder, label, icon) in new (KnownFolder, string, string)[]
        {
            (KnownFolder.Documents, "文档", "description"),
            (KnownFolder.Downloads, "下载", "unarchive"),
            (KnownFolder.Pictures, "图片", "image"),
            (KnownFolder.Music, "音乐", "audio_file"),
            (KnownFolder.Videos, "视频", "movie"),
            (KnownFolder.Desktop, "桌面", "grid_view"),
        })
        {
            var loc = await _svc.Known.GetAsync(folder, ct);
            if (loc is { } l)
                Add(label, icon, new Destination.Folder(l));
        }
        Add("最近", "schedule", new Destination.Recent());
        Add("回收站", "delete", new Destination.Trash());

        Volumes.Clear();
        long total = 0, free = 0;
        foreach (var v in await _svc.Volumes.GetVolumesAsync(ct))
        {
            total += v.TotalBytes ?? 0;
            free += v.FreeBytes ?? 0;
            Volumes.Add(new VolumeRow(
                v.DisplayName,
                v.TotalBytes is { } t
                    ? $"已用 {Formatters.Size(t - (v.FreeBytes ?? 0))} / {Formatters.Size(t)}"
                    : "容量未知"));
        }
        StorageSummary = total > 0
            ? $"剩余 {Formatters.Size(free)} / 共 {Formatters.Size(total)}"
            : "存储信息不可用";
    }
}
