using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MdExplorer.Domain.Files;
using MdExplorer.UI.Services;

namespace MdExplorer.UI.ViewModels;

public sealed record StatRow(string Label, string Value);
public sealed record ExtRow(string Ext, long Count, long Bytes);

/// 统计（概念稿 §4.1 统计卡：最近/最大/类型分布）。
public sealed partial class InsightsViewModel : ObservableObject
{
    private readonly UiServices _svc;

    public ObservableCollection<StatRow> Summary { get; } = new();
    public ObservableCollection<ExtRow> ByExtension { get; } = new();
    public ObservableCollection<SearchHitRow> Largest { get; } = new();
    public ObservableCollection<SearchHitRow> Newest { get; } = new();

    [ObservableProperty] private string _statusText = "统计中…";

    public InsightsViewModel(UiServices svc) => _svc = svc;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var root = await _svc.Known.GetAsync(KnownFolder.Home, ct);
        if (root is not { } r)
        {
            StatusText = "无法定位主目录";
            return;
        }
        long totalFiles = 0, totalDirs = 0, totalBytes = 0;
        var byExt = new Dictionary<string, (long Count, long Bytes)>(StringComparer.OrdinalIgnoreCase);
        var all = new List<FileItem>();
        try
        {
            await foreach (var item in _svc.Fs.ListAsync(r, new ListOptions(IncludeHidden: false, MaxCount: 5000), ct))
            {
                totalFiles++;
                totalBytes += item.Length ?? 0;
                if (item.Kind == FileKind.Folder) totalDirs++;
                var ext = string.IsNullOrEmpty(item.Extension) ? "(无扩展名)" : item.Extension!;
                byExt.TryGetValue(ext, out var cur);
                byExt[ext] = (cur.Count + 1, cur.Bytes + (item.Length ?? 0));
                all.Add(item);
            }
        }
        catch (OperationCanceledException)
        {
        }

        Summary.Clear();
        Summary.Add(new StatRow("文件", totalFiles.ToString()));
        Summary.Add(new StatRow("文件夹", totalDirs.ToString()));
        Summary.Add(new StatRow("总大小", Formatters.Size(totalBytes)));
        ByExtension.Clear();
        foreach (var kv in byExt.OrderByDescending(k => k.Value.Bytes).Take(8))
            ByExtension.Add(new ExtRow(kv.Key, kv.Value.Count, kv.Value.Bytes));
        Largest.Clear();
        foreach (var item in all.Where(i => i.Length is not null).OrderByDescending(i => i.Length).Take(10))
            Largest.Add(new SearchHitRow(item.Name, "", item.Kind.ToString(), Formatters.Size(item.Length!.Value), item));
        Newest.Clear();
        foreach (var item in all.OrderByDescending(i => i.LastWriteTime).Take(10))
            Newest.Add(new SearchHitRow(item.Name, "", item.Kind.ToString(), item.LastWriteTime.LocalDateTime.ToString("MM-dd HH:mm"), item));
        StatusText = $"基于主目录前 {totalFiles} 项的本地统计";
    }
}
