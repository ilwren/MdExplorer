using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Input;

namespace MdExplorer.UI.Controls;

/// <summary>
/// Material 风格的窗格分隔条原型：在 Md3.Avalonia 正式提供同类组件前，先在 Eidoria 内验证
/// 细轨道 + 居中胶囊把手 + 键盘可达三件套。拖动/键盘增量行为复用 Avalonia GridSplitter，
/// 视觉与状态反馈由本控件的 ControlTheme 承担；成熟后可整体回流 Md3.Avalonia。
/// </summary>
[TemplatePart("PART_Handle", typeof(Border))]
[TemplatePart("PART_Track", typeof(Border))]
[PseudoClasses(":pointerover", ":pressed", ":focus-visible")]
public sealed class MaterialGridSplitter : GridSplitter
{
    static MaterialGridSplitter()
    {
        InputElement.FocusableProperty.OverrideDefaultValue<MaterialGridSplitter>(true);
    }

    public MaterialGridSplitter()
    {
        ResizeDirection = GridResizeDirection.Columns;
        KeyboardIncrement = 8;
    }
}
