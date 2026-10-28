using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MdExplorer.Application.UI;
using MdExplorer.Domain.Errors;
using MdExplorer.Domain.Files;
using MdExplorer.Domain.Operations;
using MdExplorer.UI.ViewModels;

namespace MdExplorer.UI.Services;

/// 对话框实现（§5.7）：全部用代码构建控件，避免 XAML 生成依赖；
/// 后台队列询问经 Dispatcher 封送回 UI 线程（§5.5）。
public sealed class DialogService : IDialogService, ISystemPickerService
{
    private Window? _owner;

    public void Attach(Window owner) => _owner = owner;

    private Window CreateShell(string title, object content, IEnumerable<Control>? buttons)
    {
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        if (buttons is not null)
        {
            foreach (var b in buttons)
                footer.Children.Add(b);
        }
        var root = new StackPanel { Spacing = 16, MinWidth = 360, Margin = new Thickness(24) };
        root.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
        });
        root.Children.Add(content as Control ?? new TextBlock { Text = content?.ToString() ?? "" });
        root.Children.Add(footer);
        return new Window
        {
            Content = root,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.Transparent,
        };
    }

    private Task RunOnUiAsync(Func<Task> action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            return action();
        return Dispatcher.UIThread.InvokeAsync(action);
    }

    public ValueTask<string?> PromptTextAsync(string title, string? initial, string? hint, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = RunOnUiAsync(async () =>
        {
            var box = new TextBox { Text = initial ?? "", Width = 360, Watermark = hint };
            var window = CreateShell(title, box, null);
            var ok = new Button { Content = "确定", MinWidth = 88 };
            var cancel = new Button { Content = "取消", MinWidth = 88 };
            ((StackPanel)((StackPanel)window.Content!).Children[2]).Children.Add(cancel);
            ((StackPanel)((StackPanel)window.Content!).Children[2]).Children.Add(ok);
            ok.Click += (_, _) => { window.Close(box.Text); };
            cancel.Click += (_, _) => { window.Close(null); };
            var result = _owner is not null ? await window.ShowDialog<string?>(_owner) : null;
            tcs.TrySetResult(result);
        });
        return new ValueTask<string?>(tcs.Task);
    }

    public ValueTask<bool> ConfirmAsync(string title, string message, string confirmLabel, bool destructive, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = RunOnUiAsync(async () =>
        {
            var window = CreateShell(title, new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 420,
            }, null);
            var yes = new Button { Content = confirmLabel, MinWidth = 88 };
            if (destructive)
            {
                yes.Classes.Add("md");
                yes.Classes.Add("filled");
            }
            var no = new Button { Content = "取消", MinWidth = 88 };
            var footer = (StackPanel)((StackPanel)window.Content!).Children[2];
            footer.Children.Add(no);
            footer.Children.Add(yes);
            yes.Click += (_, _) => window.Close(true);
            no.Click += (_, _) => window.Close(false);
            var result = _owner is not null && await window.ShowDialog<bool?>(_owner) == true;
            tcs.TrySetResult(result);
        });
        return new ValueTask<bool>(tcs.Task);
    }

    public ValueTask<ConflictDecision> ResolveConflictAsync(ConflictContext ctx, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<ConflictDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = RunOnUiAsync(async () =>
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,*"),
                RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
                MinWidth = 420,
            };
            void Cell(int row, int col, string text, bool bold = false)
            {
                var tb = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
                if (bold) tb.FontWeight = FontWeight.SemiBold;
                Grid.SetRow(tb, row);
                Grid.SetColumn(tb, col);
                grid.Children.Add(tb);
            }
            Cell(0, 0, "目标位置（已存在）", true);
            Cell(0, 1, "来源", true);
            Cell(1, 0, $"{ctx.Name}\n{(ctx.DestSize is { } ds ? Formatters.Size(ds) : "?")} · {ctx.DestTime?.LocalDateTime:yyyy-MM-dd HH:mm}");
            Cell(1, 1, $"{ctx.Name}\n{(ctx.SourceSize is { } ss ? Formatters.Size(ss) : "?")} · {ctx.SourceTime.LocalDateTime:yyyy-MM-dd HH:mm}");
            Cell(2, 0, $"同名冲突：{ctx.Name}", true);

            var applyAll = new CheckBox { Content = "应用到其余全部", Margin = new Thickness(0, 8, 0, 0) };
            var stack = new StackPanel { Spacing = 8 };
            stack.Children.Add(grid);
            stack.Children.Add(applyAll);

            var window = CreateShell("发生冲突", stack, null);
            var footer = (StackPanel)((StackPanel)window.Content!).Children[2];
            var overwrite = new Button { Content = "覆盖", MinWidth = 88 };
            var keepBoth = new Button { Content = "保留两者", MinWidth = 88 };
            var skip = new Button { Content = "跳过", MinWidth = 88 };
            footer.Children.Add(skip);
            footer.Children.Add(keepBoth);
            footer.Children.Add(overwrite);
            ConflictResolution? chosen = null;
            overwrite.Click += (_, _) => { chosen = ConflictResolution.Overwrite; window.Close(); };
            keepBoth.Click += (_, _) => { chosen = ConflictResolution.KeepBoth; window.Close(); };
            skip.Click += (_, _) => { chosen = ConflictResolution.Skip; window.Close(); };
            if (_owner is not null)
                await window.ShowDialog(_owner);
            tcs.TrySetResult(new ConflictDecision(
                chosen ?? ConflictResolution.Skip,
                applyAll.IsChecked == true));
        });
        return new ValueTask<ConflictDecision>(tcs.Task);
    }

    public ValueTask ShowPropertiesAsync(FileItem item, CancellationToken ct = default)
    {
        _ = RunOnUiAsync(async () =>
        {
            var rows = new StackPanel { Spacing = 6 };
            void Row(string label, string value)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
                sp.Children.Add(new TextBlock { Text = label, Width = 90, Opacity = 0.7 });
                sp.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap });
                rows.Children.Add(sp);
            }
            Row("名称", item.Name);
            Row("类型", item.Kind.ToString());
            Row("大小", item.Length is { } l ? Formatters.Size(l) : "—");
            Row("修改时间", item.LastWriteTime.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
            Row("路径", item.Locator.IsLocalPath ? item.Locator.ToLocalPath() : item.Locator.Value);
            Row("只读", item.IsReadOnly ? "是" : "否");
            Row("MIME", item.MimeType ?? "—");
            var window = CreateShell("属性", rows, null);
            var footer = (StackPanel)((StackPanel)window.Content!).Children[2];
            var close = new Button { Content = "关闭", MinWidth = 88 };
            close.Click += (_, _) => window.Close();
            footer.Children.Add(close);
            if (_owner is not null)
                await window.ShowDialog(_owner);
        });
        return ValueTask.CompletedTask;
    }

    public ValueTask ShowErrorAsync(FileError error, CancellationToken ct = default)
    {
        _ = RunOnUiAsync(async () =>
        {
            var window = CreateShell("出错了", new TextBlock
            {
                Text = $"{error.Message}\n({error.Kind})",
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 420,
            }, null);
            var footer = (StackPanel)((StackPanel)window.Content!).Children[2];
            var close = new Button { Content = "知道了", MinWidth = 88 };
            close.Click += (_, _) => window.Close();
            footer.Children.Add(close);
            if (_owner is not null)
                await window.ShowDialog(_owner);
        });
        return ValueTask.CompletedTask;
    }

    public ValueTask<FileLocator?> PickFolderAsync(string title, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<FileLocator?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = RunOnUiAsync(async () =>
        {
            var box = new TextBox { Width = 360, Watermark = "输入或粘贴目录路径" };
            var window = CreateShell(title, box, null);
            var footer = (StackPanel)((StackPanel)window.Content!).Children[2];
            var ok = new Button { Content = "选择", MinWidth = 88 };
            var cancel = new Button { Content = "取消", MinWidth = 88 };
            footer.Children.Add(cancel);
            footer.Children.Add(ok);
            ok.Click += (_, _) => window.Close(true);
            cancel.Click += (_, _) => window.Close(false);
            var confirmed = _owner is not null && await window.ShowDialog<bool?>(_owner) == true;
            tcs.TrySetResult(confirmed && box.Text.Length > 0 ? FileLocator.FromPath(box.Text.Trim()) : null);
        });
        return new ValueTask<FileLocator?>(tcs.Task);
    }
}

/// Snackbar 通知（§5.7 应用内优先）：由 ShellViewModel 呈现。
public sealed class NotificationService : INotificationService
{
    public Action<string>? OnShow { get; set; }

    public void ShowTransient(string message, string? actionLabel = null, Action? action = null)
        => OnShow?.Invoke(message);
}
