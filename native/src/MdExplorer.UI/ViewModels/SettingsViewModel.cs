using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MdExplorer.Application.Settings;
using MdExplorer.Domain.Files;
using MdExplorer.UI.Services;

namespace MdExplorer.UI.ViewModels;

/// 设置页（§6.7 + 概念稿 §4.1 设置卡）。
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly UiServices _svc;

    public AppSettings Settings => _svc.Settings.Current;

    [ObservableProperty] private string _savedHint = "";

    public SettingsViewModel(UiServices svc) => _svc = svc;

    [RelayCommand]
    private async Task SaveAsync()
    {
        await _svc.Settings.SaveAsync(CancellationToken.None);
        SavedHint = "已保存";
    }
}

/// 任务中心。
public sealed partial class TasksViewModel : ObservableObject
{
    public System.Collections.ObjectModel.ObservableCollection<JobRow> Jobs { get; }

    public TasksViewModel(System.Collections.ObjectModel.ObservableCollection<JobRow> jobs)
    {
        Jobs = jobs;
    }

    public bool IsEmpty => Jobs.Count == 0;
}

/// 回收站（§6.4）：应用私有回收站跨断点一致。
public sealed partial class TrashViewModel : ObservableObject
{
    private readonly UiServices _svc;

    public System.Collections.ObjectModel.ObservableCollection<TrashRow> Items { get; } = new();

    [ObservableProperty] private string _statusText = "";

    public TrashViewModel(UiServices svc) => _svc = svc;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        Items.Clear();
        await foreach (var t in _svc.Trash.ListTrashAsync(ct))
        {
            Items.Add(new TrashRow(
                t.Id,
                t.Item,
                t.OriginalPath,
                t.DeletedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm")));
        }
        StatusText = Items.Count == 0 ? "回收站是空的" : $"{Items.Count} 项";
    }

    [RelayCommand]
    private async Task RestoreAsync(TrashRow row)
    {
        var r = await _svc.Trash.RestoreAsync(row.Id, CancellationToken.None);
        if (r.IsSuccess)
            _svc.Notify.ShowTransient($"已还原「{row.Name}」");
        else
            _svc.Notify.ShowTransient(r.Error?.Message ?? "还原失败");
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeletePermanentlyAsync(TrashRow row)
    {
        var ok = await _svc.Dialogs.ConfirmAsync("永久删除",
            $"将永久删除「{row.Name}」，不可恢复！", "永久删除", destructive: true);
        if (!ok) return;
        await _svc.Trash.DeletePermanentlyAsync(row.Locator, CancellationToken.None);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task EmptyTrashAsync()
    {
        var ok = await _svc.Dialogs.ConfirmAsync("清空回收站",
            "将永久删除回收站中的全部内容！", "清空回收站", destructive: true);
        if (!ok) return;
        await _svc.Trash.EmptyTrashAsync(CancellationToken.None);
        await LoadAsync();
    }
}

public sealed partial class TrashRow : ObservableObject
{
    public string Id { get; }
    public string Name { get; }
    public string OriginalPath { get; }
    public string DeletedLabel { get; }
    public string SizeLabel { get; }
    public FileItem Item { get; }
    public FileLocator Locator => Item.Locator;

    public TrashRow(string id, FileItem item, string originalPath, string deletedLabel)
    {
        Id = id;
        Item = item;
        Name = item.Name;
        OriginalPath = originalPath;
        DeletedLabel = deletedLabel;
        SizeLabel = item.Length is { } l ? Formatters.Size(l) : "";
    }
}
