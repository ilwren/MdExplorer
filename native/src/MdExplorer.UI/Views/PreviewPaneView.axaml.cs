using Avalonia.Controls;
using Avalonia.Media.Imaging;
using MdExplorer.UI.ViewModels;

namespace MdExplorer.UI.Views;

public sealed partial class PreviewPaneView : UserControl
{
    public PreviewPaneView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach();
    }

    private void Attach()
    {
        if (DataContext is PreviewViewModel vm)
        {
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(PreviewViewModel.ImagePath))
                    SetImage(vm.ImagePath);
            };
            SetImage(vm.ImagePath);
        }
    }

    private void SetImage(string? path)
    {
        try
        {
            PreviewImage.Source = string.IsNullOrEmpty(path) ? null : new Bitmap(path);
        }
        catch
        {
            PreviewImage.Source = null;
        }
    }
}
