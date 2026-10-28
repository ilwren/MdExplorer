using MdExplorer.Application.Browsing;
using MdExplorer.Application.Navigation;
using MdExplorer.Domain.Files;
using Xunit;

namespace MdExplorer.Application.Tests;

public class BrowserSessionTests
{
    private static FileLocator L(string p) => FileLocator.FromPath(p);

    private static string Root => OperatingSystem.IsWindows() ? @"C:\data" : "/data";

    [Fact]
    public void Navigate_PushesHistory()
    {
        var s = new BrowserSession();
        s.Navigate(new Destination.Folder(L(Root)));
        Assert.False(s.CanGoBack);
        Assert.False(s.CanGoForward);
        s.Navigate(new Destination.Folder(L(Root + "/sub")));
        Assert.True(s.CanGoBack);
    }

    [Fact]
    public void BackForward_Restores()
    {
        var s = new BrowserSession();
        s.Navigate(new Destination.Folder(L(Root)));
        s.Navigate(new Destination.Trash());
        s.GoBack();
        Assert.True(s.Current is Destination.Folder);
        Assert.True(s.CanGoForward);
        s.GoForward();
        Assert.True(s.Current is Destination.Trash);
    }

    [Fact]
    public void GoUp_Folder_GoesToParent()
    {
        var s = new BrowserSession();
        s.Navigate(new Destination.Folder(L(Root + "/sub")));
        s.GoUp(l =>
        {
            var path = l.ToLocalPath();
            var idx = path.LastIndexOfAny(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            return idx > 0 ? FileLocator.FromPath(path[..idx]) : null;
        });
        Assert.True(s.Current is Destination.Folder f && f.Locator.ToLocalPath() == Root);
    }

    [Fact]
    public void GoUp_Archive_Inner_Shortens()
    {
        var s = new BrowserSession();
        s.Navigate(new Destination.Archive(L(Root + "/a.zip"), "dir/inner"));
        s.GoUp(_ => null);
        Assert.True(s.Current is Destination.Archive a && a.Inner == "dir");
    }

    [Fact]
    public void Navigate_ClearsSelectionAndFilter()
    {
        var s = new BrowserSession();
        s.Selection.Add(L(Root + "/f.txt"));
        s.Filter = "abc";
        s.Navigate(new Destination.Home());
        Assert.Empty(s.Selection);
        Assert.Equal("", s.Filter);
    }
}

public class UniqueNameTests
{
    [Theory]
    [InlineData("a.txt", 2, "a (2).txt")]
    [InlineData("archive.tar.gz", 3, "archive.tar (3).gz")]
    [InlineData("noext", 4, "noext (4)")]
    public void UniqueName_Pattern(string name, int index, string expected)
    {
        Assert.Equal(expected, MdExplorer.Application.Operations.FileOperationQueue.UniqueName(name, index));
    }
}

public class AppSettingsTests
{
    [Fact]
    public void Defaults_MatchSpec()
    {
        var s = new MdExplorer.Application.Settings.AppSettings();
        Assert.Equal("system", s.Theme);
        Assert.Equal("grid", s.ViewMode);
        Assert.Equal("name", s.SortBy);
        Assert.False(s.SortDescending);
        Assert.False(s.ShowHidden);
        Assert.False(s.ReduceMotion);
        Assert.True(s.ConfirmDelete);
    }

    [Fact]
    public void SetProp_RaisesChange()
    {
        var s = new MdExplorer.Application.Settings.AppSettings();
        string? changed = null;
        s.PropertyChanged += (_, e) => changed = e.PropertyName;
        s.Theme = "dark";
        Assert.Equal("Theme", changed);
        Assert.Equal("dark", s.Theme);
    }
}

public class DestinationTests
{
    [Fact]
    public void Union_IsDiscriminated()
    {
        Destination d = new Destination.Folder(FileLocator.FromPath(OperatingSystem.IsWindows() ? @"C:\x" : "/x"));
        Assert.True(d is Destination.Folder);
        Assert.False(d is Destination.Trash);
    }

    [Fact]
    public void Search_CarriesQuery()
    {
        Destination d = new Destination.Search("hello");
        Assert.Equal("hello", ((Destination.Search)d).Query);
    }
}
