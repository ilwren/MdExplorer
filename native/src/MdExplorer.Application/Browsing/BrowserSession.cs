using MdExplorer.Application.Navigation;
using MdExplorer.Domain.Files;

namespace MdExplorer.Application.Browsing;

public enum EntryViewMode
{
    Grid,
    List,
}

public enum SortKey
{
    Name,
    Size,
    Modified,
    Kind,
}

/// 浏览器会话（§4.4）：一个标签页 = 一个会话，持有当前位置、前进/后退栈、
/// 选择集、排序/视图模式、搜索态。断点切换时会话不重建。
public sealed class BrowserSession
{
    private readonly List<Destination> _history = [];
    private int _histIndex = -1;

    public Guid Id { get; } = Guid.NewGuid();
    public Destination Current => _histIndex >= 0 ? _history[_histIndex] : new Destination.Home();
    public bool CanGoBack => _histIndex > 0;
    public bool CanGoForward => _histIndex < _history.Count - 1;

    public HashSet<FileLocator> Selection { get; } = [];
    public EntryViewMode ViewMode { get; set; } = EntryViewMode.Grid;
    public SortKey SortBy { get; set; } = SortKey.Name;
    public bool SortDescending { get; set; }
    public string Filter { get; set; } = string.Empty;

    public void Navigate(Destination destination, bool push = true)
    {
        if (!push && _histIndex >= 0)
        {
            _history[_histIndex] = destination;
        }
        else
        {
            if (_histIndex < _history.Count - 1)
                _history.RemoveRange(_histIndex + 1, _history.Count - _histIndex - 1);
            _history.Add(destination);
            _histIndex = _history.Count - 1;
        }
        Selection.Clear();
        Filter = string.Empty;
    }

    public void GoBack()
    {
        if (CanGoBack)
        {
            _histIndex--;
            Selection.Clear();
        }
    }

    public void GoForward()
    {
        if (CanGoForward)
        {
            _histIndex++;
            Selection.Clear();
        }
    }

    /// 上一级：文件夹 → 父级；压缩包子路径 → 上一层；压缩包根 → 所在目录。
    public void GoUp(Func<FileLocator, FileLocator?> getParent)
    {
        switch (Current)
        {
            case Destination.Folder f:
                var parent = getParent(f.Locator);
                if (parent is not null) Navigate(new Destination.Folder(parent.Value));
                break;
            case Destination.Archive a:
                var parts = a.Inner.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                    Navigate(new Destination.Archive(a.Locator, string.Join('/', parts[..^1])));
                else
                {
                    var zipParent = getParent(a.Locator);
                    if (zipParent is not null) Navigate(new Destination.Folder(zipParent.Value));
                }
                break;
        }
    }
}
