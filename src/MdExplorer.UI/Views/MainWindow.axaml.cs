using Avalonia.Controls;
using Avalonia.Styling;

namespace MdExplorer.UI.Views;

/// <summary>
/// 桌面顶层窗口的装饰策略。Windows 的 DWM 最小化/最大化/还原动画依赖原生标题栏样式位
/// （WS_CAPTION 与最小化/最大化框）；BorderOnly 会把这些样式位一并去掉，所以 Windows
/// 必须保留 Full 的原生窗口语义，再以 MainWindow 的空白 WindowDrawnDecorations 主题隐藏
/// Avalonia 自绘标题与按钮。Linux 继续使用 BorderOnly，避免改变已验证的自绘边框路径。
/// </summary>
public static class MainWindowChromePolicy
{
    public static WindowDecorations DecorationsForPlatform(bool isWindows) =>
        isWindows ? WindowDecorations.Full : WindowDecorations.BorderOnly;
}

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        if (OperatingSystem.IsWindows() &&
            Resources.TryGetValue("EidoriaWindowDrawnDecorationsTheme", out var theme) &&
            theme is ControlTheme decorationsTheme)
        {
            WindowDecorationsTheme = decorationsTheme;
        }
        WindowDecorations = MainWindowChromePolicy.DecorationsForPlatform(OperatingSystem.IsWindows());
    }
}
