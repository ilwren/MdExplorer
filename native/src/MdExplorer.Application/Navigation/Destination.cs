using MdExplorer.Domain.Files;

namespace MdExplorer.Application.Navigation;

/// 路由目的地（§4.4）：与断点解耦的「逻辑导航」。
public abstract record Destination
{
    public sealed record Home : Destination;
    public sealed record Folder(FileLocator Locator) : Destination;
    public sealed record Category(FileKind Kind) : Destination;
    public sealed record Recent : Destination;
    public sealed record Favorites : Destination;
    public sealed record Search(string Query) : Destination;
    public sealed record SearchCenter : Destination;
    public sealed record Trash : Destination;
    public sealed record Insights : Destination;
    public sealed record Archive(FileLocator Locator, string Inner) : Destination;
    public sealed record Settings : Destination;
}
