using System.Collections.ObjectModel;

namespace MdExplorer.Desktop;

/// <summary>Display-only conflict/task-center sample; resolving never touches real files.</summary>
public sealed class ConflictTaskViewModel : ObservableObject
{
    private string _selectedResolution = "保留两者";
    private bool _applyToRemaining;
    private int _currentConflict = 2;
    private string _statusText = "规划预览样例 · 未执行真实复制任务";

    public ConflictTaskViewModel()
    {
        Tasks = new ObservableCollection<ConflictTask>
        {
            new("图片整理", "等待确认", "同名图片 · 第 2/9 项", "●"),
            new("复制到 D:/归档", "排队中", "等待 1 个任务", "◷"),
            new("备份 文档/项目.md", "已完成", "已复制 · 12 KB", "✓"),
            new("移动 素材/旧版", "失败", "目标位置不可用（示例）", "! ")
        };
    }

    public ObservableCollection<ConflictTask> Tasks { get; }
    public string SelectedResolution
    {
        get => _selectedResolution;
        private set
        {
            if (SetField(ref _selectedResolution, value)) OnPropertyChanged(nameof(ResolutionSummary));
        }
    }
    public bool ApplyToRemaining { get => _applyToRemaining; set => SetField(ref _applyToRemaining, value); }
    public int CurrentConflict { get => _currentConflict; private set => SetField(ref _currentConflict, value); }
    public int TotalConflicts => 9;
    public string ConflictCounter => $"第 {Math.Min(CurrentConflict, TotalConflicts)}/{TotalConflicts} 个冲突";
    public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }
    public string ResolutionSummary => $"当前策略：{SelectedResolution}" + (ApplyToRemaining ? " · 应用到其余同类冲突" : "");

    public void ChooseResolution(string value)
    {
        SelectedResolution = value;
        OnPropertyChanged(nameof(ResolutionSummary));
    }

    public void Resolve()
    {
        var suffix = ApplyToRemaining ? "；其余同类冲突沿用此选择（演示）" : "";
        StatusText = $"演示：选择“{SelectedResolution}”{suffix}；没有读写真实文件";
        CurrentConflict = Math.Min(CurrentConflict + 1, TotalConflicts);
        OnPropertyChanged(nameof(ConflictCounter));
    }

    public void Retry() => StatusText = "演示：失败任务已标记为等待重试；没有再次执行文件操作";
    public void ToggleTaskRun() => StatusText = "演示：任务继续/暂停状态已切换；没有后台任务";
    public void CancelTask() => StatusText = "演示：取消请求已记录；没有真实任务需要取消";
}

public sealed record ConflictTask(string Name, string State, string Detail, string Glyph);
