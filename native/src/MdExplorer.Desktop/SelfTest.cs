using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
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
/// <remarks>
/// 线程模型（两处都踩过的坑，勿改）：
/// 1) Avalonia 必须在主线程先初始化，否则 Dispatcher 会被线程池线程占用，之后 SetupWithoutStarting 抛
///    "The calling thread cannot access this object because a different thread owns it"。
/// 2) 初始化后主线程直接 `.GetAwaiter().GetResult()` 会死锁：AvaloniaSynchronizationContext 会把续体
///    投回主线程。故异步检查一律走 <see cref="RunAsync{T}"/>，阻塞期间持续 RunJobs 抽取队列。
/// </remarks>
internal static class SelfTest
{
    private static readonly List<string> Failures = [];

    public static int Run()
    {
        Console.WriteLine("[selftest] Native AOT 运行时自检开始");

        // 1) Avalonia 平台初始化（主线程，AOT 下 Skia/平台后端必须可用）
        try
        {
            Program.BuildAvaloniaApp().SetupWithoutStarting();
            Pass("avalonia-setup", "初始化成功");
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
            Console.WriteLine($"[selftest]   fs    = {shell.Services.Fs.GetType().Name}");
            Console.WriteLine($"[selftest]   zip   = {shell.Services.Zip.GetType().Name}");
            Console.WriteLine($"[selftest]   index = {shell.Services.Index.GetType().Name}");
            Console.WriteLine($"[selftest]   thumbs= {shell.Services.Thumbs.GetType().Name}");
        }
        catch (Exception ex)
        {
            Fail("composition-root", ex);
            return Report();
        }

        // 3) SQLite 索引（Native AOT 下 SQLitePCLRaw 原生库加载 + 建表）
        try
        {
            var status = RunAsync(async () =>
                await shell.Services.Index.GetStatusAsync(CancellationToken.None));
            Pass("sqlite-index", $"GetStatusAsync 成功（roots={status.Roots.Count}, files={status.FileCount}）");
        }
        catch (Exception ex)
        {
            Fail("sqlite-index", ex);
        }

        // 4) JSON 源生成（设置读写往返）
        try
        {
            var theme = shell.Services.Settings.Current.Theme;
            RunAsync(async () =>
            {
                await shell.Services.Settings.SaveAsync(CancellationToken.None);
                return true;
            });
            Pass("json-settings", $"SaveAsync 成功（theme={theme}）");
        }
        catch (Exception ex)
        {
            Fail("json-settings", ex);
        }

        // 5) 递归搜索（用例层，走临时目录）
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "mdexplorer-selftest-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "selftest-needle.txt"), "needle");
            var query = new SearchQuery("selftest-needle", FileLocator.FromPath(dir));
            var hits = RunAsync(async () =>
            {
                var n = 0;
                await foreach (var _ in shell.Services.Search.SearchAsync(query, CancellationToken.None))
                {
                    if (++n >= 50) break;
                }
                return n;
            });
            Directory.Delete(dir, recursive: true);
            if (hits >= 1)
                Pass("search", $"SearchAsync 命中 {hits} 项");
            else
                Failures.Add("search: 0 命中（期望 ≥1）");
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
            Pump();
            var host = window.FindControl<ContentControl>("PageHost");
            if (host is null)
                Failures.Add("xaml-mainwindow: 找不到 PageHost");
            else
                Pass("xaml-mainwindow", $"加载成功（PageHost.Content={host.Content?.GetType().Name ?? "null"}）");
        }
        catch (Exception ex)
        {
            Fail("xaml-mainwindow", ex);
        }

        // 7) 反射绑定（关键：AOT 裁剪后反射式 Binding 仍能读到属性）
        if (window is not null)
        {
            try
            {
                shell.SnackbarText = "selftest-ok";
                shell.IsSnackbarOpen = true;
                Pump();
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
                Pump();
                host.Measure(new Size(800, 600));
                host.Arrange(new Rect(0, 0, 800, 600));
                Pump();
                var size = page.DesiredSize;
                host.Close();
                // XAML 未加载时根控件没有内容，期望尺寸为 0；布局出尺寸说明可视树已构建。
                if (size.Width > 0 && size.Height > 0)
                    Pass($"xaml-{name}", $"加载成功（期望尺寸 {size.Width:0}x{size.Height:0}）");
                else
                    Failures.Add($"xaml-{name}: 布局尺寸为 {size.Width:0}x{size.Height:0}，疑似 XAML 未加载");
            }
            catch (Exception ex)
            {
                Fail($"xaml-{name}", ex);
            }
        }

        window?.Close();
        Pump();
        return Report();
    }

    /// <summary>阻塞等待异步结果，期间持续抽取 UI 队列（避免 Avalonia 同步上下文死锁）。</summary>
    private static T RunAsync<T>(Func<Task<T>> body)
    {
        var task = body();
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (!task.IsCompleted)
        {
            Dispatcher.UIThread.RunJobs();
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("异步检查超时（60s）");
            Thread.Sleep(5);
        }
        return task.GetAwaiter().GetResult();
    }

    private static void Pump()
    {
        for (var i = 0; i < 3; i++)
            Dispatcher.UIThread.RunJobs();
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
