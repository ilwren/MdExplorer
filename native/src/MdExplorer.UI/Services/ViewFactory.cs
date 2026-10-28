using Avalonia.Controls;

namespace MdExplorer.UI.Services;

/// <summary>
/// 视图工厂（§4.3）：显式 Dictionary&lt;Type, Func&lt;Control&gt;&gt; 映射，禁反射 ViewLocator（AOT）。
/// </summary>
public sealed class ViewFactory
{
    private readonly Dictionary<Type, Func<Control>> _map = new();

    public ViewFactory Register<TVm>(Func<Control> factory)
    {
        _map[typeof(TVm)] = factory;
        return this;
    }

    public Control Create(object viewModel)
        => _map.TryGetValue(viewModel.GetType(), out var factory)
            ? factory()
            : new TextBlock { Text = $"（未注册视图：{viewModel.GetType().Name}）" };
}
