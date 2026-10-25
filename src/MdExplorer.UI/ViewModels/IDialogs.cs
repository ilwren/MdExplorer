using MdExplorer.Domain;

namespace MdExplorer.UI.ViewModels;

/// <summary>
/// Shell 向各页 ViewModel 提供的交互面（弹窗/进度/Snackbar/打开文件）。
/// 语义对齐 DEVELOPMENT.md §5.7；呈现形态由 Shell 视图负责。
/// </summary>
public interface IDialogs
{
    /// <summary>文本输入弹窗；验证器返回非空表示错误并阻止关闭。取消返回 null。</summary>
    Task<string?> PromptAsync(string title, string? initialText, string confirmLabel,
        Func<string, string?>? validate = null);

    /// <summary>确认弹窗。</summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmLabel, bool destructive = false);

    /// <summary>粘贴等操作遇同名冲突时询问策略；取消返回 null。</summary>
    Task<ConflictPolicy?> AskConflictAsync(string fileName);

    /// <summary>属性弹窗（只读信息）。</summary>
    Task ShowPropertiesAsync(string title, string body);

    /// <summary>短提示（Snackbar 语义）。</summary>
    void Notify(string message);

    /// <summary>
    /// 在进度弹窗中运行文件写操作：自动处理进度上报、取消按钮、结果提示与错误明细弹窗。
    /// 返回操作结果供调用方决定刷新。
    /// </summary>
    Task<OperationOutcome> RunOperationAsync(
        string title,
        Func<IProgress<OperationProgress>, CancellationToken, Task<OperationOutcome>> operation);
}
