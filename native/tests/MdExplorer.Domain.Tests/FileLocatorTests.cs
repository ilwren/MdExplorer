using MdExplorer.Domain.Files;
using Xunit;

namespace MdExplorer.Domain.Tests;

public class FileLocatorTests
{
    [Fact]
    public void FromPath_RoundTrips_ToLocalPath()
    {
        var path = OperatingSystem.IsWindows() ? @"C:\Users\demo\文档\file name.txt" : "/home/demo/文档/file name.txt";
        var locator = FileLocator.FromPath(path);
        Assert.True(locator.IsLocalPath);
        Assert.Equal(path, locator.ToLocalPath());
    }

    [Fact]
    public void FromPath_Empty_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => FileLocator.FromPath(""));
    }

    [Fact]
    public void Value_HasLocalScheme()
    {
        var locator = FileLocator.FromPath(OperatingSystem.IsWindows() ? @"C:\a.txt" : "/a.txt");
        Assert.StartsWith(FileLocator.LocalScheme, locator.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Saf_Locator_IsNotLocal()
    {
        var locator = FileLocator.FromAndroidTreeDocument("content://tree/primary", "doc:1");
        Assert.False(locator.IsLocalPath);
        Assert.Throws<InvalidOperationException>(() => locator.ToLocalPath());
    }
}

public class ResultTests
{
    [Fact]
    public void Ok_HasNoError()
    {
        var r = Domain.Errors.Result.Ok();
        Assert.True(r.IsSuccess);
        Assert.Null(r.Error);
    }

    [Fact]
    public void Fail_CarriesError()
    {
        var r = Domain.Errors.Result.Fail(Domain.Errors.FileErrorKind.NameConflict, "已存在");
        Assert.False(r.IsSuccess);
        Assert.Equal(Domain.Errors.FileErrorKind.NameConflict, r.Error!.Kind);
        Assert.Equal("已存在", r.Error.Message);
    }

    [Fact]
    public void Generic_Map_TransformsValue()
    {
        var r = Domain.Errors.Result.Ok().Map(42);
        Assert.True(r.IsSuccess);
        Assert.Equal(42, r.Value);
    }

    [Fact]
    public void Generic_Fail_KeepsDefault()
    {
        var r = Domain.Errors.Result<int>.Fail(Domain.Errors.FileErrorKind.IoError, "io");
        Assert.False(r.IsSuccess);
        Assert.Equal(0, r.Value);
    }
}

public class FileItemTests
{
    [Fact]
    public void Record_Equality_ByValue()
    {
        var loc = FileLocator.FromPath(OperatingSystem.IsWindows() ? @"C:\x" : "/x");
        var a = new FileItem(loc, "x", FileKind.Text, 3, DateTimeOffset.UnixEpoch, false, false, false, ".txt", "text/plain");
        var b = new FileItem(loc, "x", FileKind.Text, 3, DateTimeOffset.UnixEpoch, false, false, false, ".txt", "text/plain");
        Assert.Equal(a, b);
    }
}

public class SearchModelTests
{
    [Fact]
    public void SearchQuery_Defaults()
    {
        var q = new Domain.Search.SearchQuery("hello", default);
        Assert.True(q.Recursive);
        Assert.False(q.SearchContents);
        Assert.Null(q.Filters);
    }

    [Fact]
    public void IndexStatus_SevenMembers()
    {
        var st = new Domain.Search.IndexStatus([], false, false, null, 0, 0, 0);
        Assert.Equal(0, st.FileCount);
    }
}
