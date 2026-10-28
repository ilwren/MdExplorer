using MdExplorer.Domain.Errors;
using MdExplorer.Domain.Files;
using MdExplorer.Domain.Operations;

namespace MdExplorer.Application.UI;

/// 全部对话框/工作表收口，ViewModel 不碰控件（§5.7）。
/// 呈现形态是 View 的事：Expanded+ 居中对话框，Compact/Medium 底部 Sheet。
public interface IDialogService
{
    ValueTask<string?> PromptTextAsync(string title, string? initial, string? hint, CancellationToken ct = default);
    ValueTask<bool> ConfirmAsync(string title, string message, string confirmLabel, bool destructive, CancellationToken ct = default);
    ValueTask<ConflictDecision> ResolveConflictAsync(ConflictContext ctx, CancellationToken ct = default);
    ValueTask ShowPropertiesAsync(FileItem item, CancellationToken ct = default);
    ValueTask ShowErrorAsync(FileError error, CancellationToken ct = default);
}

public interface ISystemPickerService
{
    ValueTask<FileLocator?> PickFolderAsync(string title, CancellationToken ct = default);
}

/// 应用内 Snackbar/横幅优先（§5.7）。
public interface INotificationService
{
    void ShowTransient(string message, string? actionLabel = null, Action? action = null);
}
