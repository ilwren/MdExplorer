using MdExplorer.Application.Operations;
using MdExplorer.Domain.Files;

namespace MdExplorer.Infrastructure.Shell;

/// 应用内文件剪贴板（guaranteed；系统互通 best-effort 由 IShareService/桌面后续接）。
public sealed class AppClipboardService : IAppClipboardService
{
    public ClipboardOperation? Operation { get; private set; }
    public IReadOnlyList<FileLocator> Items { get; private set; } = [];

    public void Set(IReadOnlyList<FileLocator> items, ClipboardOperation op)
    {
        Items = items.ToList();
        Operation = op;
    }

    public void Clear()
    {
        Items = [];
        Operation = null;
    }
}
