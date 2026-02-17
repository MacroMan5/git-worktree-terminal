using System.Windows.Controls;
using EasyWindowsTerminalControl;

namespace tmuxlike.Models;

/// <summary>
/// Represents a single terminal pane within a worktree, containing the terminal control and its PTY session.
/// </summary>
public class TerminalPane
{
    /// <summary>The WPF terminal control rendered in the UI.</summary>
    public EasyTerminalControl Control { get; set; } = null!;

    /// <summary>The underlying ConPTY session, or null if not yet connected.</summary>
    public TermPTY? Session { get; set; }

    /// <summary>The border element used for tiled layout and focus highlighting.</summary>
    public Border TileBorder { get; set; } = null!;
}

/// <summary>
/// Represents a git worktree with its metadata and associated terminal panes.
/// </summary>
public class WorktreeInfo
{
    /// <summary>The absolute filesystem path to the worktree directory.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>The branch name checked out in this worktree.</summary>
    public string Branch { get; set; } = string.Empty;

    /// <summary>The abbreviated HEAD commit hash (first 7 characters).</summary>
    public string HeadCommit { get; set; } = string.Empty;

    /// <summary>Whether this is the main (first) worktree of the repository.</summary>
    public bool IsMain { get; set; }

    /// <summary>The terminal panes open in this worktree.</summary>
    public List<TerminalPane> Panes { get; } = new();

    /// <summary>The branch name for display, or "(detached)" if no branch is checked out.</summary>
    public string DisplayName => string.IsNullOrEmpty(Branch) ? "(detached)" : Branch;

    /// <summary>A truncated version of the path for display in the sidebar.</summary>
    public string ShortPath => Path.Length > 40 ? "..." + Path[^37..] : Path;
}
