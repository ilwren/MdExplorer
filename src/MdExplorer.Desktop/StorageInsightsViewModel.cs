using System.Collections.ObjectModel;

namespace MdExplorer.Desktop;

/// <summary>Display-only sample model for the storage-insights concept preview.</summary>
public sealed class StorageInsightsViewModel : ObservableObject
{
    private string _selectedCategory = "全部";
    private string _statusText = "规划预览样例 · 不会扫描或清理真实文件";
    private int _reviewCount;

    public StorageInsightsViewModel()
    {
        Categories = new ObservableCollection<StorageCategory>
        {
            new("图片与视频", "214 GB", "39%", 0.39),
            new("文档", "86 GB", "16%", 0.16),
            new("应用与缓存", "128 GB", "24%", 0.24),
            new("压缩包", "72 GB", "13%", 0.13),
            new("其他", "44 GB", "8%", 0.08)
        };
        LargeFiles = new ObservableCollection<StorageFileItem>
        {
            new("旅行素材-原片.tar", "D: / 归档 / 旅行", "42.8 GB", "压缩包"),
            new("演示视频-4K.mov", "此电脑 / 视频 / 项目", "18.2 GB", "视频"),
            new("旧设备备份.zip", "D: / 备份 / 设备", "12.6 GB", "压缩包"),
            new("相机导出-2025", "此电脑 / 图片 / 相机", "8.4 GB", "文件夹")
        };
        Duplicates = new ObservableCollection<DuplicateGroup>
        {
            new("IMG_0098.jpg", "3 份 · 9.3 MB", "此电脑 / 图片  ·  D: / 备份", false),
            new("报价单-最终版.pdf", "2 份 · 1.6 MB", "此电脑 / 文档  ·  D: / 归档", false)
        };
    }

    public ObservableCollection<StorageCategory> Categories { get; }
    public ObservableCollection<StorageFileItem> LargeFiles { get; }
    public ObservableCollection<DuplicateGroup> Duplicates { get; }
    public string SelectedCategory { get => _selectedCategory; set => SetField(ref _selectedCategory, value); }
    public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }
    public int ReviewCount { get => _reviewCount; private set => SetField(ref _reviewCount, value); }
    public string ReviewCountLabel => ReviewCount == 0 ? "加入复核" : $"已加入复核 · {ReviewCount}";

    public void SelectCategory(StorageCategory item)
    {
        SelectedCategory = item.Name;
        StatusText = $"演示：已定位“{item.Name}”的空间块；不会触发删除";
    }

    public void AddToReview(DuplicateGroup group)
    {
        if (group.IsInReview) return;
        group.IsInReview = true;
        ReviewCount++;
        OnPropertyChanged(nameof(ReviewCountLabel));
        StatusText = $"演示：已将“{group.FileName}”加入复核列表；后续仍需确认任务";
    }

    public void Locate(StorageFileItem file) => StatusText = $"演示：定位到“{file.FileName}”；未读取磁盘";
    public void Preview(StorageFileItem file) => StatusText = $"演示：打开“{file.FileName}”的预览；未读取磁盘";
}

public sealed record StorageCategory(string Name, string Size, string Share, double Ratio);
public sealed record StorageFileItem(string FileName, string Path, string Size, string Kind);

public sealed class DuplicateGroup : ObservableObject
{
    private bool _isInReview;
    public DuplicateGroup(string fileName, string size, string locations, bool isInReview)
    {
        FileName = fileName;
        Size = size;
        Locations = locations;
        _isInReview = isInReview;
    }
    public string FileName { get; }
    public string Size { get; }
    public string Locations { get; }
    public bool IsInReview
    {
        get => _isInReview;
        set => SetField(ref _isInReview, value);
    }
}
