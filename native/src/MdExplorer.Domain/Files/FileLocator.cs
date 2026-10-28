namespace MdExplorer.Domain.Files;

/// <summary>
/// 文件定位符：跨平台统一寻址，刻意不等于本地路径字符串（§5.1）。
/// 桌面是绝对路径（file:/// scheme）；Android 可能是 SAF 树 URI 下的文档定位（saf://）。
/// 序列化形式带 scheme 前缀，旧数据可迁移识别。路径拼接/父级计算由 IPathService 完成，禁止字符串手拼。
/// </summary>
public readonly record struct FileLocator(string Value)
{
    public const string LocalScheme = "file:///";
    public const string SafScheme = "saf://";

    public static FileLocator FromPath(string absolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        return new FileLocator(LocalScheme + Uri.EscapeDataString(absolutePath));
    }

    public static FileLocator FromAndroidTreeDocument(string treeUri, string documentId)
        => new($"{SafScheme}{Uri.EscapeDataString(treeUri)}/{Uri.EscapeDataString(documentId)}");

    public bool IsLocalPath => Value.StartsWith(LocalScheme, StringComparison.OrdinalIgnoreCase);

    /// 仅当 IsLocalPath 时可调用。
    public string ToLocalPath()
    {
        if (!IsLocalPath)
            throw new InvalidOperationException($"Locator '{Value}' is not a local path.");
        return Uri.UnescapeDataString(Value[LocalScheme.Length..]);
    }

    public override string ToString() => Value;
}
