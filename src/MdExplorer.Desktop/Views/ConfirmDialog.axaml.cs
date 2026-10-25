using Avalonia.Controls;

namespace MdExplorer.Desktop.Views;

public sealed partial class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message, string confirmText)
    {
        InitializeComponent();
        Title = title;
        ConfirmationMessage.Text = message;
        ConfirmButton.Content = confirmText;
        ConfirmButton.Click += (_, _) => Close(true);
        CancelButton.Click += (_, _) => Close(false);
    }

    public static Task<bool> ShowAsync(Window owner, string title, string message, string confirmText)
    {
        var dialog = new ConfirmDialog(title, message, confirmText);
        return dialog.ShowDialog<bool>(owner);
    }
}
