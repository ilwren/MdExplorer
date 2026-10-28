using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MdExplorer.Application.Navigation;
using MdExplorer.Domain.Files;
using MdExplorer.Domain.Operations;
using MdExplorer.UI.Services;

namespace MdExplorer.UI.ViewModels;

/// 压缩包工作台（§6.6）：zip 列表/预览/解压/压缩；7z/rar 降级元数据。
public sealed partial class ArchiveViewModel : ObservableObject
{
    private readonly UiServices _svc;

    [ObservableProperty] private string _archivePathText = "";
    [ObservableProperty] private string _statusText = "输入 zip 路径或从浏览页双击进入";
    [ObservableProperty] private string _destDirText = "";
    [ObservableProperty] private bool _isLoaded;

    public ObservableCollection<ArchiveEntryRow> Entries { get; } = new();

    public ArchiveViewModel(UiServices svc)
    {
        _svc = svc;
        DestDirText = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    [RelayCommand]
    private void Load()
    {
        Entries.Clear();
        var path = ArchivePathText.Trim();
        if (path.Length == 0)
        {
            StatusText = "请输入压缩包路径";
            return;
        }
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".7z" or ".rar")
        {
            StatusText = "7z/RAR 仅支持元数据预览（降级策略）；请用系统工具打开";
            IsLoaded = false;
            return;
        }
        var result = _svc.Zip.ListAll(path);
        if (!result.IsSuccess)
        {
            StatusText = result.Error?.Message ?? "无法读取压缩包";
            IsLoaded = false;
            return;
        }
        foreach (var e in result.Value)
            Entries.Add(new ArchiveEntryRow(
                e.Name,
                e.IsDirectory ? "文件夹" : e.Length is { } l ? Formatters.Size(l) : "",
                e.IsDirectory ? "folder" : "file"));
        StatusText = $"{Entries.Count} 个条目";
        IsLoaded = true;
    }

    [RelayCommand]
    private void ExtractHere()
    {
        var path = ArchivePathText.Trim();
        if (path.Length == 0) return;
        var dest = DestDirText.Trim();
        if (dest.Length == 0) return;
        _svc.Queue.Enqueue(new FileOperationJob(
            FileJobKind.Extract,
            [FileLocator.FromPath(path)],
            FileLocator.FromPath(dest),
            ConflictPolicy.KeepBoth));
        _svc.Notify.ShowTransient("解压任务已加入队列");
    }

    [RelayCommand]
    private void CompressSelection()
    {
        var tab = (CurrentContext as ShellViewModel)?.ActiveTab;
        var items = tab?.SelectedItems.ToList() ?? [];
        if (items.Count == 0)
        {
            _svc.Notify.ShowTransient("先在浏览页选中要压缩的文件");
            return;
        }
        var dest = tab!.Session.Current is Destination.Folder f ? f.Locator : null;
        if (dest is null) return;
        _svc.Queue.Enqueue(new FileOperationJob(
            FileJobKind.Compress,
            items.Select(i => i.Locator).ToList(),
            dest,
            NewName: $"archive-{DateTime.Now:yyyyMMdd-HHmmss}.zip"));
        _svc.Notify.ShowTransient("压缩任务已加入队列");
    }

    /// 由 Shell 注入以获取当前选择。
    public object? CurrentContext { get; set; }
}
