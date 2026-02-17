using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace tmuxlike.Controls;

public partial class PromptOverlay : UserControl
{
    public event Action<string>? PromptAccepted;
    public event Action? PromptDiscarded;

    public PromptOverlay()
    {
        InitializeComponent();
    }

    public void Show(string prompt)
    {
        PromptTextBox.Text = prompt;
        Visibility = Visibility.Visible;
        PromptTextBox.Focus();
        PromptTextBox.SelectAll();
    }

    public void Hide()
    {
        Visibility = Visibility.Collapsed;
        PromptTextBox.Text = "";
    }

    public bool IsOpen => Visibility == Visibility.Visible;

    private void Overlay_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            PromptDiscarded?.Invoke();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            var text = PromptTextBox.Text.Trim();
            Hide();
            if (!string.IsNullOrEmpty(text))
                PromptAccepted?.Invoke(text);
            e.Handled = true;
        }
        // Shift+Enter allows newlines in the textbox (default behavior)
    }
}
