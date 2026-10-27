using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MdExplorer.Domain;

namespace MdExplorer.UI.ViewModels;

/// <summary>任务中心中的一项文件操作：排队/运行状态、进度、取消与完成结果都可追溯。</summary>
public enum OperationTaskStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled,
}

public sealed partial class OperationTaskViewModel : ObservableObject, IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();

    internal OperationTaskViewModel(string title) => _title = title;

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(IsActive))]
    [NotifyPropertyChangedFor(nameof(IsFinished))]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    [NotifyPropertyChangedFor(nameof(IsQueued))]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyPropertyChangedFor(nameof(IsCompleted))]
    [NotifyPropertyChangedFor(nameof(IsFailed))]
    [NotifyPropertyChangedFor(nameof(IsCancelled))]
    private OperationTaskStatus _status = OperationTaskStatus.Queued;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressPercentText))]
    private double _progressPercent;

    [ObservableProperty]
    private string _detail = "等待开始";

    [ObservableProperty]
    private string? _errorText;

    public DateTimeOffset StartedAt { get; } = DateTimeOffset.Now;
    public DateTimeOffset? FinishedAt { get; private set; }

    public string TimeText => FinishedAt is { } finished
        ? $"{StartedAt:HH:mm:ss} – {finished:HH:mm:ss}"
        : $"开始于 {StartedAt:HH:mm:ss}";

    public string StatusText => Status switch
    {
        OperationTaskStatus.Queued => "排队中",
        OperationTaskStatus.Running => "运行中",
        OperationTaskStatus.Completed => "已完成",
        OperationTaskStatus.Failed => "未完全完成",
        OperationTaskStatus.Cancelled => "已取消",
        _ => string.Empty,
    };

    public bool IsActive => Status is OperationTaskStatus.Queued or OperationTaskStatus.Running;
    public bool IsFinished => !IsActive;
    public bool CanCancel => IsActive;
    public bool HasErrors => ErrorText is not null;
    public bool IsQueued => Status == OperationTaskStatus.Queued;
    public bool IsRunning => Status == OperationTaskStatus.Running;
    public bool IsCompleted => Status == OperationTaskStatus.Completed;
    public bool IsFailed => Status == OperationTaskStatus.Failed;
    public bool IsCancelled => Status == OperationTaskStatus.Cancelled;
    public string ProgressPercentText => $"{ProgressPercent:0}%";

    internal CancellationToken CancellationToken => _cancellation.Token;

    [RelayCommand]
    private void Cancel()
    {
        if (!IsActive)
            return;
        Detail = "正在取消…";
        _cancellation.Cancel();
    }

    internal void MarkRunning()
    {
        if (Status != OperationTaskStatus.Queued)
            return;
        Status = OperationTaskStatus.Running;
        Detail = "正在准备…";
    }

    internal void Report(OperationProgress progress)
    {
        // Progress<T> 的回调是异步投递的；终态之后到达的旧进度不得把 100% 回退。
        if (Status is OperationTaskStatus.Completed or OperationTaskStatus.Failed or OperationTaskStatus.Cancelled)
            return;
        if (Status == OperationTaskStatus.Queued)
            Status = OperationTaskStatus.Running;
        ProgressPercent = Math.Clamp(progress.Fraction, 0, 1) * 100;
        Detail = progress.ItemsTotal > 0
            ? $"{progress.CurrentItemName}（{progress.ItemsDone}/{progress.ItemsTotal}）"
            : progress.CurrentItemName;
    }

    internal void Complete(OperationOutcome outcome)
    {
        FinishedAt = DateTimeOffset.Now;
        if (outcome.Cancelled)
        {
            Status = OperationTaskStatus.Cancelled;
            Detail = outcome.SucceededCount > 0
                ? $"已取消；此前完成了 {outcome.SucceededCount} 项"
                : "已取消";
        }
        else if (outcome.Errors.Count > 0)
        {
            Status = OperationTaskStatus.Failed;
            ProgressPercent = 100;
            Detail = outcome.SucceededCount > 0
                ? $"完成了 {outcome.SucceededCount} 项，{outcome.Errors.Count} 项失败"
                : $"{outcome.Errors.Count} 项失败";
            ErrorText = BuildErrorText(outcome.Errors);
        }
        else
        {
            Status = OperationTaskStatus.Completed;
            ProgressPercent = 100;
            Detail = outcome.SucceededCount > 0 ? $"已完成 {outcome.SucceededCount} 项" : "已完成";
        }
        OnPropertyChanged(nameof(TimeText));
        OnPropertyChanged(nameof(HasErrors));
    }

    private static string BuildErrorText(IReadOnlyList<OperationError> errors)
    {
        var text = string.Join("\n", errors.Take(8).Select(e => $"· {e.ItemName}：{e.Message}"));
        return errors.Count > 8 ? $"{text}\n…等共 {errors.Count} 项失败" : text;
    }

    public void Dispose() => _cancellation.Dispose();
}
