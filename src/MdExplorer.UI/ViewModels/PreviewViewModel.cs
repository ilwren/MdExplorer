using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MdExplorer.Domain;
using MdExplorer.Platform.Abstractions;
using MdExplorer.UI.Preview;

namespace MdExplorer.UI.ViewModels;

/// <summary>
/// 预览状态机：图片位图 / 文本（含代码、数据、未知二进制 Hex 摘要）/ ZIP 条目 / 纯元数据卡。
/// 内容生产已插件化（P0，docs/PLUGIN-SYSTEM.md §6）：经 <see cref="PreviewProviderRegistry"/>
/// 按优先级择一提供者，本类只管呈现状态与代次防串。每次 Load 先递增代次作废上一轮。
/// </summary>
public sealed partial class PreviewViewModel(
    PreviewProviderRegistry previews,
    Func<IFileLauncherService> launcherRefresh) : ObservableObject
{
    private int _generation;

    [ObservableProperty] private bool _hasItem;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private Bitmap? _image;
    [ObservableProperty] private string? _textContent;
    [ObservableProperty] private string? _failureText;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _canOpen;

    public bool HasImage => Image is not null;
    public bool HasText => TextContent is not null;
    public bool HasArchive => ArchiveEntries.Count > 0;
    public bool HasFailure => FailureText is not null;

    public ObservableCollection<MetadataRowModel> MetadataRows { get; } = [];
    public ObservableCollection<ArchiveEntryRowModel> ArchiveEntries { get; } = [];

    public FileItem? CurrentItem { get; private set; }

    partial void OnImageChanged(Bitmap? value) => OnPropertyChanged(nameof(HasImage));
    partial void OnTextContentChanged(string? value) => OnPropertyChanged(nameof(HasText));
    partial void OnFailureTextChanged(string? value) => OnPropertyChanged(nameof(HasFailure));


    public async Task LoadAsync(FileItem? item)
    {
        var generation = ++_generation;
        CurrentItem = item;
        Image = null;
        TextContent = null;
        FailureText = null;
        ArchiveEntries.Clear();
        MetadataRows.Clear();
        OnPropertyChanged(nameof(HasArchive));

        if (item is null)
        {
            HasItem = false;
            CanOpen = false;
            Title = string.Empty;
            Subtitle = string.Empty;
            IsLoading = false;
            return;
        }

        HasItem = true;
        CanOpen = !item.IsFolder;
        Title = item.Name;
        Subtitle = FileKindClassifier.Describe(item);
        BuildMetadata(item);
        if (item.IsFolder)
        {
            IsLoading = false;
            return;
        }

        IsLoading = true;
        try
        {
            var provider = previews.Resolve(item);
            if (provider is null)
            {
                FailureText = "此类型暂不支持内置预览，可先用系统应用打开。";
                return;
            }

            var content = await provider.LoadAsync(item, CancellationToken.None).ConfigureAwait(true);
            if (generation != _generation) return;
            Image = content.Image;
            TextContent = content.Text;
            if (content.Entries is not null)
            {
                foreach (var entry in content.Entries)
                    ArchiveEntries.Add(entry);
                OnPropertyChanged(nameof(HasArchive));
            }
            FailureText = content.Message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or ArgumentException)
        {
            if (generation == _generation)
                FailureText = $"预览失败：{ex.Message}";
        }
        finally
        {
            if (generation == _generation)
                IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        if (CurrentItem is null) return;
        // Android 上启动器在 MainView 挂载后替换，故每次取最新实例
        await launcherRefresh().OpenAsync(CurrentItem.FullPath, CancellationToken.None).ConfigureAwait(true);
    }

    private void BuildMetadata(FileItem item)
    {
        MetadataRows.Add(new MetadataRowModel("类型", FileKindClassifier.Describe(item)));
        if (!item.IsFolder)
            MetadataRows.Add(new MetadataRowModel("大小", $"{Formatting.Bytes(item.Length)}（{item.Length:N0} 字节）"));
        MetadataRows.Add(new MetadataRowModel("修改时间", Formatting.FullDateTime(item.LastWriteTime)));
        MetadataRows.Add(new MetadataRowModel("位置", Path.GetDirectoryName(item.FullPath) ?? item.FullPath));
        if (item.IsSymlink)
            MetadataRows.Add(new MetadataRowModel("链接", "符号链接"));
        if (item.IsHidden)
            MetadataRows.Add(new MetadataRowModel("可见性", "隐藏"));
    }
}
