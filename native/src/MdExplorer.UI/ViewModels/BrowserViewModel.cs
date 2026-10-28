using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MdExplorer.Application.Browsing;
using MdExplorer.Application.Navigation;
using MdExplorer.Domain.Files;
using MdExplorer.Platform.Abstractions.FileSystem;
using MdExplorer.Platform.Abstractions.Shell;
using MdExplorer.UI.Services;

namespace MdExplorer.UI.ViewModels;

/// 目的地显示名（Destination 是可辨识联合，UI 侧集中映射）。
public static class DestNames
{
    public static string Title(Destination d) => d switch
    {
        Destination.Home => "主页",
        Destination.Folder f => SafeName(f.Locator) is { Length: > 0 } n ? n : "文件夹",
        Destination.Category c => c.Kind switch
        {
            FileKind.Image => "图片",
            FileKind.Video => "视频",
            FileKind.Audio => "音频",
            FileKind.Document => "文档",
            FileKind.Archive => "压缩包",
            FileKind.Code => "代码",
            FileKind.Text => "文本",
            _ => "分类",
        },
        Destination.Recent => "最近",
        Destination.Favorites => "收藏",
        Destination.Search s => $"搜索「{s.Query}」",
        Destination.SearchCenter => "搜索中心",
        Destination.Trash => "回收站",
        Destination.Insights => "统计",
        Destination.Archive a => SafeName(a.Locator) is { Length: > 0 } n ? n : "压缩包",
        Destination.Settings => "设置",
        _ => "浏览",
    };

    public static string Icon(Destination d) => d switch
    {
        Destination.Home => "home",
        Destination.Folder => "folder",
        Destination.Category c => c.Kind switch
        {
            FileKind.Image => "image",
            FileKind.Video => "movie",
            FileKind.Audio => "audio_file",
            FileKind.Document => "description",
            FileKind.Archive => "archive",
            FileKind.Code => "code",
            _ => "file",
        },
        Destination.Recent => "schedule",
        Destination.Favorites => "star",
        Destination.Search => "search",
        Destination.SearchCenter => "manage_search",
        Destination.Trash => "delete",
        Destination.Insights => "data_usage",
        Destination.Archive => "archive",
        Destination.Settings => "settings",
        _ => "file",
    };

    private static string SafeName(FileLocator locator)
    {
        if (!locator.IsLocalPath) return "";
        var p = locator.ToLocalPath();
        return System.IO.Path.GetFileName(p.TrimEnd(System.IO.Path.DirectorySeparatorChar, '/'));
    }
}

/// 列表项的可绑定投影（§4.4：ViewModel 承载状态，UI 只绑定）。
public sealed partial class EntryViewModel : ObservableObject
{
    public FileItem Item { get; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isCut;

    public EntryViewModel(FileItem item)
    {
        Item = item;
    }

    public string Name => Item.Name;
    public string KindLabel => Item.Kind switch
    {
        FileKind.Folder => "文件夹",
        FileKind.Image => "图片",
        FileKind.Video => "视频",
        FileKind.Audio => "音频",
        FileKind.Document => "文档",
        FileKind.Archive => "压缩包",
        FileKind.Code => "代码",
        FileKind.Text => "文本",
        FileKind.Executable => "可执行",
        _ => "文件",
    };
    public string SizeLabel => Item.Length is { } l ? Formatters.Size(l) : "";
    public string ModifiedLabel => Item.LastWriteTime.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
    public string IconName => Item.Kind switch
    {
        FileKind.Folder => "folder",
        FileKind.Image => "image",
        FileKind.Video => "movie",
        FileKind.Audio => "audio_file",
        FileKind.Document => "description",
        FileKind.Archive => "archive",
        FileKind.Code => "code",
        FileKind.Executable => "data_usage",
        _ => "file",
    };
}

public static class Formatters
{
    public static string Size(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double v = bytes;
        var u = 0;
        while (v >= 1024 && u < units.Length - 1)
        {
            v /= 1024;
            u++;
        }
        return u == 0 ? $"{bytes} B" : $"{v:0.#} {units[u]}";
    }
}

/// 浏览器会话 ViewModel：一个标签页 = 一个 BrowserSession（§4.4）。
public sealed partial class BrowserViewModel : ObservableObject
{
    private readonly UiServices _svc;
    private CancellationTokenSource? _loadCts;

    public BrowserSession Session { get; }

    [ObservableProperty]
    private string _title = "主页";

    [ObservableProperty]
    private string _locationText = "";

    [ObservableProperty]
    private string? _filterText;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private EntryViewModel? _previewed;

    [ObservableProperty]
    private bool _canGoBack;

    [ObservableProperty]
    private bool _canGoForward;

    [ObservableProperty]
    private bool _isGridView = true;

    public ObservableCollection<EntryViewModel> Entries { get; } = new();

    public event EventHandler? SelectionChanged;

    public BrowserViewModel(UiServices svc, Destination initial)
    {
        _svc = svc;
        Session = new BrowserSession();
        Session.Navigate(initial);
        Title = DestNames.Title(initial);
        _ = ReloadAsync();
    }

    public IEnumerable<FileItem> SelectedItems => Entries.Where(e => e.IsSelected).Select(e => e.Item);

    public async Task NavigateAsync(Destination dest, CancellationToken ct = default)
    {
        Session.Navigate(dest);
        await ReloadAsync(ct);
    }

    [RelayCommand]
    private async Task GoBackAsync()
    {
        Session.GoBack();
        await ReloadAsync();
    }

    [RelayCommand]
    private async Task GoForwardAsync()
    {
        Session.GoForward();
        await ReloadAsync();
    }

    [RelayCommand]
    private async Task GoUpAsync()
    {
        Session.GoUp(l => _svc.Path.GetParent(l));
        await ReloadAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await ReloadAsync();
    }

    [RelayCommand]
    private async Task SetSortAsync(string by)
    {
        var key = by switch
        {
            "size" => SortKey.Size,
            "modified" => SortKey.Modified,
            "kind" => SortKey.Kind,
            _ => SortKey.Name,
        };
        if (Session.SortBy == key) Session.SortDescending = !Session.SortDescending;
        else
        {
            Session.SortBy = key;
            Session.SortDescending = false;
        }
        await ReloadAsync();
    }

    [RelayCommand]
    private void SetViewMode(string mode)
    {
        Session.ViewMode = mode == "list" ? EntryViewMode.List : EntryViewMode.Grid;
        IsGridView = Session.ViewMode == EntryViewMode.Grid;
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var e in Entries) e.IsSelected = true;
        SyncSelection();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var e in Entries) e.IsSelected = false;
        SyncSelection();
    }

    public void ToggleSelect(EntryViewModel entry)
    {
        entry.IsSelected = !entry.IsSelected;
        SyncSelection();
    }

    private void SyncSelection()
    {
        Session.Selection.Clear();
        foreach (var e in Entries.Where(x => x.IsSelected))
            Session.Selection.Add(e.Item.Locator);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetPreview(EntryViewModel? entry)
    {
        Previewed = entry;
    }

    public async Task OpenAsync(EntryViewModel entry, CancellationToken ct = default)
    {
        if (entry.Item.Kind == FileKind.Folder)
        {
            await NavigateAsync(new Destination.Folder(entry.Item.Locator), ct);
            return;
        }
        if (entry.Item.Kind == FileKind.Archive && entry.Item.Extension == ".zip")
        {
            await NavigateAsync(new Destination.Archive(entry.Item.Locator, ""), ct);
            return;
        }
        SetPreview(entry);
        await _svc.Launcher.OpenAsync(entry.Item.Locator, ct);
    }

    private async Task ReloadAsync(CancellationToken ct = default)
    {
        _loadCts?.Cancel();
        _loadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _loadCts.Token;
        var dest = Session.Current;
        Title = DestNames.Title(dest);
        LocationText = dest switch
        {
            Destination.Folder f when f.Locator.IsLocalPath => _svc.Path.Describe(f.Locator),
            Destination.Archive a when a.Locator.IsLocalPath => $"{_svc.Path.Describe(a.Locator)} ▸ {a.Inner}",
            _ => DestNames.Title(dest),
        };
        CanGoBack = Session.CanGoBack;
        CanGoForward = Session.CanGoForward;
        Entries.Clear();
        SetPreview(null);

        var pending = new List<EntryViewModel>();
        var lastFlush = DateTime.UtcNow;
        var count = 0;
        try
        {
            await foreach (var item in Source(dest, token).WithCancellation(token))
            {
                if (!string.IsNullOrEmpty(FilterText) &&
                    !item.Name.Contains(FilterText, StringComparison.OrdinalIgnoreCase))
                    continue;
                pending.Add(new EntryViewModel(item));
                count++;
                if (pending.Count >= 50 || (DateTime.UtcNow - lastFlush).TotalMilliseconds > 150)
                {
                    Flush(pending);
                    lastFlush = DateTime.UtcNow;
                    StatusText = $"{count} 项…";
                }
            }
            Flush(pending);
            // 分批入集后再排序一次（burst 先到先显 + 最终有序）
            var sorted = Session.SortBy switch
            {
                SortKey.Size => Session.SortDescending
                    ? Entries.OrderByDescending(e => e.Item.Length ?? -1)
                    : Entries.OrderBy(e => e.Item.Length ?? -1),
                SortKey.Modified => Session.SortDescending
                    ? Entries.OrderByDescending(e => e.Item.LastWriteTime)
                    : Entries.OrderBy(e => e.Item.LastWriteTime),
                SortKey.Kind => Session.SortDescending
                    ? Entries.OrderByDescending(e => e.KindLabel).ThenByDescending(e => e.Name)
                    : Entries.OrderBy(e => e.KindLabel).ThenBy(e => e.Name),
                _ => Session.SortDescending
                    ? Entries.OrderByDescending(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
                    : Entries.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase),
            };
            var list = sorted.ToList();
            if (list.Count != Entries.Count || !list.SequenceEqual(Entries))
            {
                Entries.Clear();
                foreach (var e in list) Entries.Add(e);
            }
            StatusText = $"{Entries.Count} 项";
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Flush(List<EntryViewModel> pending)
    {
        if (pending.Count == 0) return;
        var batch = pending.ToList();
        pending.Clear();
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var e in batch) Entries.Add(e);
        });
    }

    private async IAsyncEnumerable<FileItem> Source(Destination dest, [EnumeratorCancellation] CancellationToken ct)
    {
        switch (dest)
        {
            case Destination.Folder f:
                await foreach (var item in _svc.Fs.ListAsync(f.Locator, new ListOptions(IncludeHidden: true), ct))
                    yield return item;
                break;
            case Destination.Trash:
                await foreach (var trashed in _svc.Trash.ListTrashAsync(ct))
                    yield return trashed.Item;
                break;
            case Destination.Archive a:
                var listing = _svc.Zip.ListAll(a.Locator.ToLocalPath());
                if (listing.IsSuccess)
                {
                    foreach (var entry in listing.Value)
                    {
                        ct.ThrowIfCancellationRequested();
                        yield return new FileItem(
                            a.Locator, entry.Name,
                            entry.IsDirectory ? FileKind.Folder : FileKind.Archive,
                            entry.Length, default,
                            false, false, false,
                            System.IO.Path.GetExtension(entry.Name), null);
                    }
                }
                break;
            case Destination.Home:
                var home = await _svc.Known.GetAsync(KnownFolder.Home, ct);
                if (home is { } h)
                {
                    await foreach (var item in _svc.Fs.ListAsync(h, new ListOptions(IncludeHidden: true), ct))
                        yield return item;
                }
                break;
            case Destination.Category c:
                var root = await _svc.Known.GetAsync(KnownFolder.Home, ct);
                if (root is { } r)
                {
                    await foreach (var item in _svc.Fs.ListAsync(r, new ListOptions(IncludeHidden: false), ct))
                    {
                        ct.ThrowIfCancellationRequested();
                        if (item.Kind == c.Kind)
                            yield return item;
                    }
                }
                break;
            case Destination.Recent:
                var root2 = await _svc.Known.GetAsync(KnownFolder.Home, ct);
                if (root2 is { } r2)
                {
                    var recent = new List<FileItem>();
                    await foreach (var item in _svc.Fs.ListAsync(r2, new ListOptions(IncludeHidden: false, MaxCount: 200), ct))
                        recent.Add(item);
                    foreach (var item in recent.OrderByDescending(x => x.LastWriteTime).Take(50))
                        yield return item;
                }
                break;
            case Destination.Favorites:
                yield break; // v1：收藏为设置项内管理，浏览页留空态
            default:
                yield break;
        }
    }
}
