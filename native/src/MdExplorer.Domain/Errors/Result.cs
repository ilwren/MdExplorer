namespace MdExplorer.Domain.Errors;

/// 应用层之下不抛裸异常跨界：统一 Result&lt;T&gt; + FileError（§5.1）。
public enum FileErrorKind
{
    NotFound,
    AccessDenied,
    NameConflict,
    InvalidName,
    DiskFull,
    PathTooLong,
    IoError,
    PermissionNotGranted,
    Unsupported,
    Cancelled,
    Unknown,
}

public sealed record FileError(
    FileErrorKind Kind,
    string Message,
    Files.FileLocator? Locator = null,
    Exception? Cause = null);

public readonly struct Result
{
    public FileError? Error { get; }
    public bool IsSuccess => Error is null;

    private Result(FileError? error) => Error = error;

    public static Result Ok() => new(null);
    public static Result Fail(FileError error) => new(error);
    public static Result Fail(FileErrorKind kind, string message, Files.FileLocator? locator = null, Exception? cause = null)
        => new(new FileError(kind, message, locator, cause));

    public Result<T> Map<T>(T value) => IsSuccess ? Result<T>.Ok(value) : Result<T>.Fail(Error!);
}

public readonly struct Result<T>
{
    public T? Value { get; }
    public FileError? Error { get; }
    public bool IsSuccess => Error is null;

    private Result(T? value, FileError? error)
    {
        Value = value;
        Error = error;
    }

    public static Result<T> Ok(T value) => new(value, null);
    public static Result<T> Fail(FileError error) => new(default, error);
    public static Result<T> Fail(FileErrorKind kind, string message, Files.FileLocator? locator = null, Exception? cause = null)
        => new(default, new FileError(kind, message, locator, cause));

    public T GetValueOrThrow()
        => IsSuccess ? Value! : throw new InvalidOperationException(Error!.Message, Error.Cause);
}
