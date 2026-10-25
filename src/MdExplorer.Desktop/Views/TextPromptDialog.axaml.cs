using Avalonia.Controls;
using Md3.Avalonia.Controls;

namespace MdExplorer.Desktop.Views;

public sealed partial class TextPromptDialog : Window
{
    public TextPromptDialog(string title, string prompt, string placeholder, string initialValue, string confirmText)
    {
        InitializeComponent();
        Title = title;
        PromptMessage.Text = prompt;
        NameInput.PlaceholderText = placeholder;
        NameInput.Text = initialValue;
        AcceptButton.Content = confirmText;
        AcceptButton.Click += (_, _) => Close(NameInput.Text);
        CancelButton.Click += (_, _) => Close(null);
    }

    public static Task<string?> ShowAsync(Window owner, string title, string prompt, string placeholder, string initialValue, string confirmText)
    {
        var dialog = new TextPromptDialog(title, prompt, placeholder, initialValue, confirmText);
        return dialog.ShowDialog<string?>(owner);
    }
}
