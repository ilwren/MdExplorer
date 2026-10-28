using System.Reflection;
using Xunit;

namespace MdExplorer.ArchitectureChecks;

/// <summary>
/// 分层引用规则（DEVELOPMENT.md §4.2「引用规则 CI 锁死」）：
/// 通过程序集引用关系验证，而不是靠注释约定。
/// </summary>
public sealed class LayeringTests
{
    /// 框架程序集前缀：永远放行。
    private static readonly string[] FrameworkPrefixes =
    [
        "System", "netstandard", "mscorlib", "Microsoft.Win32", "WindowsBase",
    ];

    /// 允许的 MdExplorer 项目引用（分层规则的可执行形式）。
    private static readonly Dictionary<string, string[]> AllowedProjectRefs = new()
    {
        // Domain：零 PackageReference、零项目引用（§4.2）
        ["MdExplorer.Domain"] = [],
        // Platform.Abstractions：仅 Domain
        ["MdExplorer.Platform.Abstractions"] = ["MdExplorer.Domain"],
        // Application：Domain + Abstractions（+ CommunityToolkit/Logging）
        ["MdExplorer.Application"] = ["MdExplorer.Domain", "MdExplorer.Platform.Abstractions"],
        // Infrastructure：Domain + Abstractions + Application
        ["MdExplorer.Infrastructure"] =
        [
            "MdExplorer.Domain", "MdExplorer.Platform.Abstractions", "MdExplorer.Application",
        ],
        // Platform.*：Domain + Abstractions（薄适配，LibraryImport/XDG）
        ["MdExplorer.Platform.Windows"] = ["MdExplorer.Domain", "MdExplorer.Platform.Abstractions"],
        ["MdExplorer.Platform.Linux"] = ["MdExplorer.Domain", "MdExplorer.Platform.Abstractions"],
        // UI：禁引 Platform.* 实现与 Infrastructure（§4.2「UI 禁引 Platform.*」）
        ["MdExplorer.UI"] = ["MdExplorer.Domain", "MdExplorer.Platform.Abstractions", "MdExplorer.Application"],
    };

    /// 允许的第三方程序集前缀（每层的白名单，对应 CPM 登记的包）。
    private static readonly Dictionary<string, string[]> AllowedExternalPrefixes = new()
    {
        ["MdExplorer.Domain"] = [],
        ["MdExplorer.Platform.Abstractions"] = [],
        ["MdExplorer.Application"] = ["CommunityToolkit.", "Microsoft.Extensions.Logging"],
        ["MdExplorer.Infrastructure"] =
        [
            "CommunityToolkit.", "Microsoft.Extensions.Logging", "Microsoft.Data.Sqlite",
            "SQLitePCLRaw.", "SkiaSharp.",
        ],
        ["MdExplorer.Platform.Windows"] = [],
        ["MdExplorer.Platform.Linux"] = [],
        ["MdExplorer.UI"] = ["Avalonia.", "CommunityToolkit."],
    };

    private static Assembly Load(string assemblyName) => assemblyName switch
    {
        "MdExplorer.Domain" => typeof(MdExplorer.Domain.Files.FileLocator).Assembly,
        "MdExplorer.Platform.Abstractions" => typeof(MdExplorer.Platform.Abstractions.FileSystem.IFileSystemService).Assembly,
        "MdExplorer.Application" => typeof(MdExplorer.Application.Browsing.BrowserSession).Assembly,
        "MdExplorer.Infrastructure" => typeof(MdExplorer.Infrastructure.FileSystem.LocalFileSystemService).Assembly,
        "MdExplorer.Platform.Windows" => typeof(MdExplorer.Platform.Windows.WindowsCapabilities).Assembly,
        "MdExplorer.Platform.Linux" => typeof(MdExplorer.Platform.Linux.LinuxCapabilities).Assembly,
        "MdExplorer.UI" => typeof(MdExplorer.UI.ViewModels.ShellViewModel).Assembly,
        _ => throw new ArgumentException(assemblyName),
    };

    private static bool IsFramework(string name)
        => FrameworkPrefixes.Any(p => name == p || name.StartsWith(p + ".", StringComparison.Ordinal));

    [Theory]
    [InlineData("MdExplorer.Domain")]
    [InlineData("MdExplorer.Platform.Abstractions")]
    [InlineData("MdExplorer.Application")]
    [InlineData("MdExplorer.Infrastructure")]
    [InlineData("MdExplorer.Platform.Windows")]
    [InlineData("MdExplorer.Platform.Linux")]
    [InlineData("MdExplorer.UI")]
    public void ReferencedAssemblies_RespectLayering(string assemblyName)
    {
        var allowedProjects = AllowedProjectRefs[assemblyName];
        var allowedExternal = AllowedExternalPrefixes[assemblyName];
        var violations = new List<string>();

        foreach (var reference in Load(assemblyName).GetReferencedAssemblies())
        {
            var name = reference.Name ?? "";
            if (IsFramework(name))
                continue;

            if (name.StartsWith("MdExplorer.", StringComparison.Ordinal))
            {
                if (!allowedProjects.Contains(name))
                    violations.Add($"项目引用越界：{assemblyName} → {name}");
                continue;
            }

            // 前缀匹配：既接受完全相等（如 SkiaSharp），也接受带命名空间后缀（如 SkiaSharp.Skottie）。
            if (!allowedExternal.Any(p => name == p.TrimEnd('.') || name.StartsWith(p, StringComparison.Ordinal)))
                violations.Add($"未登记的第三方引用：{assemblyName} → {name}");
        }

        Assert.True(violations.Count == 0,
            $"{assemblyName} 违反分层规则：\n{string.Join('\n', violations)}");
    }

    [Fact]
    public void Ui_DoesNotReference_PlatformImplementationsOrInfrastructure()
    {
        var refs = typeof(MdExplorer.UI.ViewModels.ShellViewModel).Assembly
            .GetReferencedAssemblies()
            .Select(r => r.Name ?? "")
            .ToHashSet(StringComparer.Ordinal);

        foreach (var forbidden in new[]
        {
            "MdExplorer.Infrastructure",
            "MdExplorer.Platform.Windows",
            "MdExplorer.Platform.Linux",
            "MdExplorer.Platform.Android",
        })
        {
            Assert.DoesNotContain(forbidden, refs);
        }
    }

    [Fact]
    public void Domain_HasNoProjectOrPackageReferences()
    {
        var refs = typeof(MdExplorer.Domain.Files.FileLocator).Assembly.GetReferencedAssemblies();
        foreach (var reference in refs)
        {
            var name = reference.Name ?? "";
            Assert.False(name.StartsWith("MdExplorer.", StringComparison.Ordinal),
                $"Domain 不得引用其他项目：{name}");
            Assert.True(IsFramework(name),
                $"Domain 不得引用第三方包（§4.2 零 PackageReference）：{name}");
        }
    }
}
