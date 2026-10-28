using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using MdExplorer.Application.Search;
using MdExplorer.Domain.Files;
using MdExplorer.Domain.Search;
using MdExplorer.UI.ViewModels;
using MdExplorer.UI.Views;

namespace MdExplorer.Desktop;

/// <summary>
/// AOT 运行时自检（`MdExplorer --selftest`）：在 Native AOT 产物里真实执行一次启动路径，
/// 验证裁剪没有破坏 DI 装配、SQLite 原生库、JSON 源生成、XAML 加载与反射绑定（§11 P2 AOT）。
/// 返回码 0 = 全部通过；1 = 有失败项。CI 以此作为 AOT 冒烟门槛。
/// </summary>
internal static class SelfTest
{
    private static readonly List<string> Failures = [];

    public static int Run()
    {
        Console.WriteLine("[selftest] Native AOT 运行时自检开始");

        // 1) Avalonia 平台初始化（AOT 下 Skia/平台后端必须可用）
        try
        {
            Program.BuildAvaloniaApp().SetupWithoutStarting();
            Pass("avalonia-setup", $"初始化成功（{Application.Current?.GetType().Name}）");
        }
        catch (Exception ex)
        {
            Fail("avalonia-setup", ex);
            return Report();
        }

        // 2) 组合根：显式 DI 装配（§4.3）
        ShellViewModel shell;
        try
        {
            shell = CompositionRoot.Build(out _);
            Pass("composition-root", "ShellViewModel 装配成功");
            Console.WriteLine($"[selftest]   fs   = {shell.Services.Fs.GetType().Name}");
            Console.WriteLine($"[selftest]   zip  = {shell.Services.Zip.GetType().Name}");
            Console.WriteLine($"[selftest]   index= {shell.Services.Index.GetType().Name}");
            Console.WriteLine($"[selftest]   thumbs={shell.Services.Thumbs.GetType().Name}");
        }
        catch (Exception ex)
        {
            Fail("composition-root", ex);
            return Report();
        }

        // 3) SQLite 索引（Native AOT 下 SQLitePCLRaw 原生库能否加载 + 建表）
        try
        {
            var status = shell.Services.Index.GetStatusAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
            Pass("sqlite-index", $"GetStatusAsync 成功（roots={status.Roots.Count}, files={status.FileCount}）");
        }
        catch (Exception ex)
        {
            Fail("sqlite-index", ex);
        }

        // 4) JSON 源生成（设置读写往返）
        try
        {
            var s = shell.Services.Settings;
            var before = s.Current.Theme;
            s.SaveAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
            Pass("json-settings", $"SaveAsync 成功（theme={before}）");
        }
        catch (Exception ex)
        {
            Fail("json-settings", ex);
        }

        // 5) 递归搜索（用例层 + STJ/集合路径，走临时目录）
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "mdexplorer-selftest-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "selftest-needle.txt"), "needle");
            var hitCount = 0;
            var query = new SearchQuery("selftest-needle", FileLocator.FromPath(dir));
            var e = shell.Services.Search.SearchAsync(query, CancellationToken.None).GetAsyncEnumerator();
            try
            {
                while (e.MoveNextAsync().AsTask().GetAwaiter().GetResult() && hitCount < 50)
                    hitCount++;
            }
            finally
            {
                e.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            Directory.Delete(dir, recursive: true);
            if (hitCount >= 1)
                Pass("search", $"SearchAsync 命中 {hitCount} 项");
            else
                Failures.Add($"search: 0 命中（期望 ≥1）");
        }
        catch (Exception ex)
        {
            Fail("search", ex);
        }

        // 6) 主窗口 XAML 加载（Avalonia XamlX 编译产物 + 主题资源）
        MainWindow? window = null;
        try
        {
            window = new MainWindow { DataContext = shell };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var host = window.FindControl<ContentControl>("PageHost");
            if (host is null)
                Failures.Add("xaml-mainwindow: 找不到 PageHost");
            else
                Pass("xaml-mainwindow", $"MainWindow 加载成功（PageHost.Content={host.Content?.GetType().Name ?? "null"}）");
        }
        catch (Exception ex)
        {
            Fail("xaml-mainwindow", ex);
        }

        // 7) 反射绑定（关键：AOT 裁剪后 Binding 仍能读到属性）
        if (window is not null)
        {
            try
            {
                shell.SnackbarText = "selftest-ok";
                shell.IsSnackbarOpen = true;
                Dispatcher.UIThread.RunJobs();
                var text = window.FindControl<TextBlock>("SnackbarTextBlock")?.Text;
                if (text == "selftest-ok")
                    Pass("binding-snackbar", "反射绑定经 AOT 裁剪后仍生效");
                else
                    Failures.Add($"binding-snackbar: 期望 'selftest-ok'，实际 '{text ?? "<null>"}'");
            }
            catch (Exception ex)
            {
                Fail("binding-snackbar", ex);
            }
        }

        // 8) 全部页面视图 XAML 加载（§4.3 显式 ViewFactory）
        var pages = new (string Name, Func<Control> Create)[]
        {
            ("BrowserView", () => new BrowserView()),
            ("SearchCenterView", () => new SearchCenterView()),
            ("SettingsView", () => new SettingsView()),
            ("HomeView", () => new HomeView()),
            ("TasksView", () => new TasksView()),
            ("TrashView", () => new TrashView()),
            ("InsightsView", () => new InsightsView()),
            ("ArchiveView", () => new ArchiveView()),
        };
        foreach (var (name, create) in pages)
        {
            try
            {
                var page = create();
                var host = new Window { Content = page, Width = 800, Height = 600, DataContext = shell };
                host.Show();
                Dispatcher.UIThread.RunJobs();
                host.Measure(new Size(800, 600));
                host.Arrange(new Rect(0, 0, 800, 600));
                Dispatcher.UIThread.RunJobs();
                var descendants = CountVisuals(page);
                host.Close();
                if (descendants > 3)
                    Pass($"xaml-{name}", $"加载成功（可视元素 {descendants}）");
                else
                    Failures.Add($"xaml-{name}: 可视树仅 {descendants} 个元素，疑似 XAML 未加载");
            }
            catch (Exception ex)
            {
                Fail(name, ex);
            }
        }

        window?.Close();
        return Report();
    }

    private static int CountVisuals(Visual v)
    {
        var count = 1;
        foreach (var child in v.GetVisualChildren())
            count += CountVisuals(child);
        return count;
    }

    private static void Pass(string name, string detail)
        => Console.WriteLine($"[selftest] PASS {name}: {detail}");

    private static void Fail(string name, Exception ex)
        => Failures.Add($"{name}: {ex.GetType().Name}: {ex.Message}");

    private static int Report()
    {
        if (Failures.Count == 0)
        {
            Console.WriteLine("[selftest] ✅ 全部通过");
            return 0;
        }
        Console.WriteLine($"[selftest] ❌ 失败 {Failures.Count} 项：");
        foreach (var f in Failures)
            Console.WriteLine($"[selftest]   - {f}");
        return 1;
    }
}
