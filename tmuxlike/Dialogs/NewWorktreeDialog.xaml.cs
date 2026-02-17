using System.IO;
using System.Windows;

namespace tmuxlike.Dialogs;

/// <summary>
/// Dialog window for creating a new git worktree. Prompts for a branch name
/// and displays the auto-generated worktree path.
/// </summary>
public partial class NewWorktreeDialog : Window
{
    private readonly string _repoRoot;

    /// <summary>The branch name entered by the user, trimmed of whitespace.</summary>
    public string BranchName => BranchInput.Text.Trim();

    /// <summary>The computed filesystem path where the new worktree will be created.</summary>
    public string WorktreePath => PathPreview.Text;

    /// <summary>
    /// Initializes the dialog with the given repository root path.
    /// </summary>
    /// <param name="repoRoot">The root path of the git repository.</param>
    public NewWorktreeDialog(string repoRoot)
    {
        InitializeComponent();
        _repoRoot = repoRoot;
        BranchInput.Focus();
    }

    private void BranchInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(BranchInput.Text))
        {
            PathPreview.Text = "";
            return;
        }

        var safeName = BranchInput.Text.Trim().Replace('/', '-');
        var parentDir = Path.GetDirectoryName(_repoRoot) ?? _repoRoot;
        PathPreview.Text = Path.Combine(parentDir, safeName);
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(BranchName))
        {
            MessageBox.Show("Please enter a branch name.", "Validation",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
