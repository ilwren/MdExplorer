using MdExplorer.Domain.Files;

namespace MdExplorer.Application.Operations;

/// 应用内文件剪贴板（guaranteed）；系统互通为 best-effort（§5.3）。
public interface IAppClipboardService
{
    ClipboardOperation? Operation { get; }
    IReadOnlyList<FileLocator> Items { get; }
    void Set(IReadOnlyList<FileLocator> items, ClipboardOperation op);
    void Clear();
}
