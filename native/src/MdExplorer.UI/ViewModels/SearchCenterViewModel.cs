using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MdExplorer.Application.Search;
using MdExplorer.Domain.Files;
using MdExplorer.Domain.Search;
using MdExplorer.UI.Services;

namespace MdExplorer.UI.ViewModels;

public sealed record SearchHitRow(
    string Name,
    string Path,
    string KindLabel,
    string? Snippet,
    FileItem Item);

/// 搜索中心（§5.6 + 概念稿 §4.1）：过滤 → 实时递归 → 索引增强。
public sealed partial class SearchCenterViewModel : ObservableObject
{
    private readonly UiServices _svc;
    private CancellationTokenSource? _searchCts;

    [ObservableProperty] private string _queryText = "";
    [ObservableProperty] private bool _searchContents;
    [ObservableProperty] private string _statusText = "输入关键字开始搜索";
    [ObservableProperty] private string _indexStatusText = "";
    [ObservableProperty] private bool _kindImage;
    [ObservableProperty] private bool _kindVideo;
    [ObservableProperty] private bool _kindAudio;
    [ObservableProperty] private bool _kindDocument;
    [ObservableProperty] private bool _kindArchive;
    [ObservableProperty] private bool _kindCode;
    [ObservableProperty] private bool _kindText;
    [ObservableProperty] private bool _kindPdf;

    public ObservableCollection<SearchHitRow> Results { get; } = new();

    public SearchCenterViewModel(UiServices svc)
    {
        _svc = svc;
        _ = RefreshIndexStatusAsync();
    }

    private async Task RefreshIndexStatusAsync()
    {
        var st = await _svc.Index.GetStatusAsync(CancellationToken.None);
        IndexStatusText = st.Roots.Count == 0
            ? "未启用内容索引（默认关闭）· 本地优先"
            : $"索引 {st.FileCount} 个文件 · {(st.Paused ? "已暂停" : "运行中")}" +
              (st.UpdatedAt is { } u ? $" · 更新于 {u.LocalDateTime:MM-dd HH:mm}" : "");
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        Results.Clear();
        if (string.IsNullOrWhiteSpace(QueryText))
        {
            StatusText = "输入关键字开始搜索";
            return;
        }
        var kinds = new HashSet<FileKind>();
        if (KindImage) kinds.Add(FileKind.Image);
        if (KindVideo) kinds.Add(FileKind.Video);
        if (KindAudio) kinds.Add(FileKind.Audio);
        if (KindDocument) kinds.Add(FileKind.Document);
        if (KindArchive) kinds.Add(FileKind.Archive);
        if (KindCode) kinds.Add(FileKind.Code);
        if (KindText) kinds.Add(FileKind.Text);
        if (KindPdf) kinds.Add(FileKind.Document);
        var scope = await _svc.Known.GetAsync(KnownFolder.Home, CancellationToken.None) ?? default;
        var query = new SearchQuery(
            QueryText,
            scope,
            Recursive: true,
            Filters: kinds.Count > 0 ? new SearchFilters(Kinds: kinds) : null,
            SearchContents: SearchContents);
        var count = 0;
        StatusText = "搜索中…";
        try
        {
            await foreach (var hit in _svc.Search.SearchAsync(query, token))
            {
                token.ThrowIfCancellationRequested();
                Results.Add(new SearchHitRow(
                    hit.Item.Name,
                    hit.ParentPath ?? (hit.Item.Locator.IsLocalPath ? hit.Item.Locator.ToLocalPath() : ""),
                    hit.Item.Kind.ToString(),
                    hit.Snippet,
                    hit.Item));
                count++;
                if (count % 50 == 0)
                    StatusText = $"搜索中… 已找到 {count} 条";
            }
            StatusText = count == 0 ? "没有找到匹配项" : $"共 {count} 条结果";
        }
        catch (OperationCanceledException)
        {
        }
    }

    [RelayCommand]
    private void Clear()
    {
        _searchCts?.Cancel();
        Results.Clear();
        QueryText = "";
        StatusText = "输入关键字开始搜索";
    }

    [RelayCommand]
    private async Task RebuildIndexAsync()
    {
        StatusText = "正在建立本地索引…";
        var st = await _svc.Index.RebuildAsync(CancellationToken.None);
        await RefreshIndexStatusAsync();
        StatusText = $"索引完成：{st.FileCount} 个文件";
    }

    [RelayCommand]
    private async Task ClearIndexAsync()
    {
        await _svc.Index.ClearAsync(CancellationToken.None);
        await RefreshIndexStatusAsync();
        StatusText = "本地索引已清空";
    }

    [RelayCommand]
    private async Task ToggleContentIndexAsync()
    {
        await _svc.Index.SetContentEnabledAsync(!SearchContents, CancellationToken.None);
        await RefreshIndexStatusAsync();
    }
}
