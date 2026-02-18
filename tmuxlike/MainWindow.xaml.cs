using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using EasyWindowsTerminalControl;
using tmuxlike.Dialogs;
using tmuxlike.Models;
using tmuxlike.Services;

namespace tmuxlike;

/// <summary>
/// Converts a boolean IsMain value to a colored brush for the worktree sidebar indicator.
/// Green for the main worktree, blue for others.
/// </summary>
public class MainBranchBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (bool)value
            ? new SolidColorBrush(Color.FromRgb(0x4e, 0xc9, 0xb0)) // green
            : new SolidColorBrush(Color.FromRgb(0x56, 0x9c, 0xd6)); // blue
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts a boolean IsDirectory value to a folder or file emoji icon.
/// </summary>
public class FileIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (bool)value ? "📁" : "📄";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Main application window. Manages the worktree sidebar, terminal pane tiling,
/// file explorer, and VS Code integration.
/// </summary>
public partial class MainWindow : Window
{
    public static readonly RoutedCommand NewWorktreeCommand = new();
    public static readonly RoutedCommand RefreshCommand = new();
    public static readonly RoutedCommand DeleteWorktreeCommand = new();
    public static readonly RoutedCommand ToggleFilesCommand = new();
    public static readonly RoutedCommand OpenVSCodeCommand = new();
    public static readonly RoutedCommand SplitPaneCommand = new();
    public static readonly RoutedCommand ClosePaneCommand = new();
    public static readonly RoutedCommand NextPaneCommand = new();
    public static readonly RoutedCommand PrevPaneCommand = new();
    public static readonly RoutedCommand NextWorktreeCommand = new();
    public static readonly RoutedCommand PrevWorktreeCommand = new();
    public static readonly RoutedCommand VoiceToggleCommand = new();

    private const int MaxPanes = 4;

    private readonly string _repoRoot;
    private readonly string _shellPath;
    private WorktreeInfo? _currentWorktree;
    private bool _filesVisible;
    private int _focusedPaneIndex;
    private VoiceService? _voiceService;

    public MainWindow()
    {
        InitializeComponent();

        _repoRoot = App.RepoRoot;
        _shellPath = FindShell();

        Title = $"Git Worktree Manager - {App.RepoName}";
        RepoNameText.Text = App.RepoName;

        CommandBindings.Add(new CommandBinding(NewWorktreeCommand, (_, _) => NewWorktree_Click(null, null!)));
        CommandBindings.Add(new CommandBinding(RefreshCommand, (_, _) => Refresh_Click(null, null!)));
        CommandBindings.Add(new CommandBinding(DeleteWorktreeCommand, (_, _) => DeleteWorktree_Click(null, null!)));
        CommandBindings.Add(new CommandBinding(ToggleFilesCommand, (_, _) => ToggleFiles_Click(null, null!)));
        CommandBindings.Add(new CommandBinding(OpenVSCodeCommand, (_, _) => OpenVSCode_Click(null, null!)));
        CommandBindings.Add(new CommandBinding(SplitPaneCommand, (_, _) => SplitPane_Click(null, null!)));
        CommandBindings.Add(new CommandBinding(ClosePaneCommand, (_, _) => ClosePane_Click(null, null!)));
        CommandBindings.Add(new CommandBinding(NextPaneCommand, (_, _) => CyclePane(1)));
        CommandBindings.Add(new CommandBinding(PrevPaneCommand, (_, _) => CyclePane(-1)));
        CommandBindings.Add(new CommandBinding(NextWorktreeCommand, (_, _) => CycleWorktree(1)));
        CommandBindings.Add(new CommandBinding(PrevWorktreeCommand, (_, _) => CycleWorktree(-1)));
        CommandBindings.Add(new CommandBinding(VoiceToggleCommand, (_, _) =>
        {
            if (VoiceOverlay.IsOpen) return;
            _voiceService?.Toggle();
        }));

        KeybindingsService.EnsureConfigExists();
        ApplyKeybindings();

        ContentRendered += MainWindow_ContentRendered;

        ConfigService.EnsureConfigExists();
        var config = ConfigService.Load();
        _voiceService = new VoiceService(Dispatcher, config);
        _voiceService.StateChanged += OnVoiceStateChanged;
        _voiceService.PromptReady += OnVoicePromptReady;
        _voiceService.ErrorOccurred += OnVoiceError;
        _voiceService.StartBridge();

        VoiceOverlay.PromptAccepted += OnPromptAccepted;
        VoiceOverlay.PromptDiscarded += OnPromptDiscarded;
    }

    // ── Keybindings ───────────────────────────────────────────────

    private void ApplyKeybindings()
    {
        InputBindings.Clear();

        var config = KeybindingsService.Load();
        var bindings = new (string combo, RoutedCommand command)[]
        {
            (config.NewWorktree, NewWorktreeCommand),
            (config.Refresh, RefreshCommand),
            (config.DeleteWorktree, DeleteWorktreeCommand),
            (config.ToggleFiles, ToggleFilesCommand),
            (config.OpenVSCode, OpenVSCodeCommand),
            (config.SplitPane, SplitPaneCommand),
            (config.ClosePane, ClosePaneCommand),
            (config.NextPane, NextPaneCommand),
            (config.PrevPane, PrevPaneCommand),
            (config.NextWorktree, NextWorktreeCommand),
            (config.PrevWorktree, PrevWorktreeCommand),
            (config.VoiceToggle, VoiceToggleCommand),
        };

        foreach (var (combo, command) in bindings)
        {
            if (KeyComboParser.TryParse(combo, out var key, out var modifiers))
                InputBindings.Add(new KeyBinding(command, key, modifiers));
        }

        UpdateStatusBarText(config);
    }

    private void UpdateStatusBarText(KeybindingsConfig? config = null)
    {
        config ??= KeybindingsService.Load();
        StatusBarHintText.Text =
            $"{config.NewWorktree}: New | {config.Refresh}: Refresh | {config.DeleteWorktree}: Remove | " +
            $"{config.ToggleFiles}: Files | {config.OpenVSCode}: VS Code | {config.SplitPane}: Split | " +
            $"{config.ClosePane}: Close | {config.NextPane}: Panes | " +
            $"{config.PrevWorktree}/{config.NextWorktree}: Worktrees";
    }

    private void KeybindingsSettings_Click(object? sender, RoutedEventArgs e)
    {
        var config = KeybindingsService.Load();
        var dialog = new KeybindingsDialog(config) { Owner = this };
        if (dialog.ShowDialog() == true)
            ApplyKeybindings();
    }

    private void MainWindow_ContentRendered(object? sender, EventArgs e)
    {
        LoadWorktrees();

        if (WorktreeList.Items.Count > 0)
            WorktreeList.SelectedIndex = 0;
    }

    private static string FindShell()
    {
        var pwsh = FindInPath("pwsh.exe");
        if (pwsh != null) return pwsh;

        var ps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        if (File.Exists(ps)) return ps;

        return "cmd.exe";
    }

    private static string? FindInPath(string exe)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var full = Path.Combine(dir, exe);
            if (File.Exists(full)) return full;
        }
        return null;
    }

    // ── Pane creation & layout ──────────────────────────────────────

    private static string BuildStartupCommand(string shellPath, string workingDir)
    {
        var name = Path.GetFileName(shellPath);

        if (name.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase))
            return $"\"{shellPath}\" -WorkingDirectory \"{workingDir}\"";

        if (name.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase))
        {
            var escaped = workingDir.Replace("'", "''");
            return $"\"{shellPath}\" -NoExit -Command \"Set-Location '{escaped}'\"";
        }

        // cmd.exe or other
        return $"\"{shellPath}\" /K \"cd /d \"{workingDir}\"\"";
    }

    private TerminalPane CreatePane(WorktreeInfo wt)
    {
        var pane = new TerminalPane();
        var paneIndex = wt.Panes.Count; // index this pane will occupy

        var terminal = new EasyTerminalControl
        {
            FontSizeWhenSettingTheme = 13,
            StartupCommandLine = BuildStartupCommand(_shellPath, wt.Path)
        };
        pane.Control = terminal;

        var label = new TextBlock
        {
            Text = $"{wt.DisplayName} [{paneIndex + 1}/{wt.Panes.Count + 1}]",
            Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(8, 4, 8, 4),
            VerticalAlignment = VerticalAlignment.Center
        };

        var header = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2d, 0x2d, 0x2d)),
            Child = label,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3c, 0x3c, 0x3c)),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };

        var dock = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);
        dock.Children.Add(terminal);

        var border = new Border
        {
            Child = dock,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3c, 0x3c, 0x3c)),
            Margin = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            ClipToBounds = true
        };

        // Prevent WPF from intercepting Tab — let the terminal handle it
        KeyboardNavigation.SetTabNavigation(border, KeyboardNavigationMode.None);
        KeyboardNavigation.SetControlTabNavigation(border, KeyboardNavigationMode.None);
        KeyboardNavigation.SetDirectionalNavigation(border, KeyboardNavigationMode.None);

        pane.TileBorder = border;

        // Track focus
        terminal.GotFocus += (_, _) =>
        {
            if (_currentWorktree == null) return;
            var idx = _currentWorktree.Panes.IndexOf(pane);
            if (idx >= 0)
            {
                _focusedPaneIndex = idx;
                UpdatePaneHighlight();
            }
        };

        // Capture session reference after terminal loads
        terminal.Loaded += (_, _) =>
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                pane.Session = terminal.ConPTYTerm;
            };
            timer.Start();
        };

        wt.Panes.Add(pane);
        return pane;
    }

    private void RebuildTileLayout()
    {
        if (_currentWorktree == null) return;

        var panes = _currentWorktree.Panes;
        var count = panes.Count;
        if (count == 0) return;

        TerminalTileGrid.Children.Clear();
        TerminalTileGrid.RowDefinitions.Clear();
        TerminalTileGrid.ColumnDefinitions.Clear();

        switch (count)
        {
            case 1:
                TerminalTileGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                TerminalTileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetRow(panes[0].TileBorder, 0);
                Grid.SetColumn(panes[0].TileBorder, 0);
                TerminalTileGrid.Children.Add(panes[0].TileBorder);
                break;

            case 2:
                TerminalTileGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                TerminalTileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                TerminalTileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetRow(panes[0].TileBorder, 0);
                Grid.SetColumn(panes[0].TileBorder, 0);
                Grid.SetColumnSpan(panes[0].TileBorder, 1);
                TerminalTileGrid.Children.Add(panes[0].TileBorder);
                Grid.SetRow(panes[1].TileBorder, 0);
                Grid.SetColumn(panes[1].TileBorder, 1);
                Grid.SetColumnSpan(panes[1].TileBorder, 1);
                TerminalTileGrid.Children.Add(panes[1].TileBorder);
                break;

            case 3:
                TerminalTileGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                TerminalTileGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                TerminalTileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                TerminalTileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                // Top row: 2 panes side by side
                Grid.SetRow(panes[0].TileBorder, 0);
                Grid.SetColumn(panes[0].TileBorder, 0);
                Grid.SetColumnSpan(panes[0].TileBorder, 1);
                TerminalTileGrid.Children.Add(panes[0].TileBorder);
                Grid.SetRow(panes[1].TileBorder, 0);
                Grid.SetColumn(panes[1].TileBorder, 1);
                Grid.SetColumnSpan(panes[1].TileBorder, 1);
                TerminalTileGrid.Children.Add(panes[1].TileBorder);
                // Bottom row: 1 pane spanning full width
                Grid.SetRow(panes[2].TileBorder, 1);
                Grid.SetColumn(panes[2].TileBorder, 0);
                Grid.SetColumnSpan(panes[2].TileBorder, 2);
                TerminalTileGrid.Children.Add(panes[2].TileBorder);
                break;

            case 4:
                TerminalTileGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                TerminalTileGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                TerminalTileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                TerminalTileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetRow(panes[0].TileBorder, 0);
                Grid.SetColumn(panes[0].TileBorder, 0);
                Grid.SetColumnSpan(panes[0].TileBorder, 1);
                TerminalTileGrid.Children.Add(panes[0].TileBorder);
                Grid.SetRow(panes[1].TileBorder, 0);
                Grid.SetColumn(panes[1].TileBorder, 1);
                Grid.SetColumnSpan(panes[1].TileBorder, 1);
                TerminalTileGrid.Children.Add(panes[1].TileBorder);
                Grid.SetRow(panes[2].TileBorder, 1);
                Grid.SetColumn(panes[2].TileBorder, 0);
                Grid.SetColumnSpan(panes[2].TileBorder, 1);
                TerminalTileGrid.Children.Add(panes[2].TileBorder);
                Grid.SetRow(panes[3].TileBorder, 1);
                Grid.SetColumn(panes[3].TileBorder, 1);
                Grid.SetColumnSpan(panes[3].TileBorder, 1);
                TerminalTileGrid.Children.Add(panes[3].TileBorder);
                break;
        }

        // Update pane labels
        for (var i = 0; i < panes.Count; i++)
        {
            if (panes[i].TileBorder.Child is DockPanel dp && dp.Children[0] is Border header && header.Child is TextBlock tb)
                tb.Text = $"{_currentWorktree.DisplayName} [{i + 1}/{count}]";
        }

        if (_focusedPaneIndex >= count)
            _focusedPaneIndex = count - 1;

        UpdatePaneHighlight();
    }

    private void UpdatePaneHighlight()
    {
        if (_currentWorktree == null) return;
        var panes = _currentWorktree.Panes;
        for (var i = 0; i < panes.Count; i++)
        {
            panes[i].TileBorder.BorderBrush = i == _focusedPaneIndex
                ? new SolidColorBrush(Color.FromRgb(0x00, 0x7a, 0xcc))
                : new SolidColorBrush(Color.FromRgb(0x3c, 0x3c, 0x3c));
        }
    }

    // ── Split / Close pane ──────────────────────────────────────────

    private void SplitPane_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentWorktree == null) return;
        if (_currentWorktree.Panes.Count >= MaxPanes) return;

        CreatePane(_currentWorktree);
        RebuildTileLayout();
    }

    private void CyclePane(int direction)
    {
        if (_currentWorktree == null || _currentWorktree.Panes.Count <= 1) return;

        _focusedPaneIndex = (_focusedPaneIndex + direction + _currentWorktree.Panes.Count) % _currentWorktree.Panes.Count;
        _currentWorktree.Panes[_focusedPaneIndex].Control.Focus();
        UpdatePaneHighlight();
    }

    private void CycleWorktree(int direction)
    {
        if (WorktreeList.Items.Count <= 1) return;

        var nextIdx = (WorktreeList.SelectedIndex + direction + WorktreeList.Items.Count) % WorktreeList.Items.Count;
        WorktreeList.SelectedIndex = nextIdx;
        WorktreeList.ScrollIntoView(WorktreeList.SelectedItem);
    }

    private void ClosePane_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentWorktree == null) return;
        if (_currentWorktree.Panes.Count <= 1) return;

        var pane = _currentWorktree.Panes[_focusedPaneIndex];

        // Disconnect & dispose the session
        try { pane.Control.DisconnectConPTYTerm(); } catch { }
        if (pane.Session != null)
        {
            try { pane.Session.CloseStdinToApp(); } catch { }
        }

        _currentWorktree.Panes.RemoveAt(_focusedPaneIndex);
        RebuildTileLayout();
    }

    // ── Worktree management ─────────────────────────────────────────

    private void LoadWorktrees()
    {
        var previousSelection = _currentWorktree?.Path;
        var existingWorktrees = new Dictionary<string, WorktreeInfo>();

        // Preserve existing WorktreeInfo objects (with their Panes lists)
        if (WorktreeList.ItemsSource is List<WorktreeInfo> oldList)
        {
            foreach (var wt in oldList)
                existingWorktrees[wt.Path] = wt;
        }

        var freshWorktrees = GitService.GetWorktrees(_repoRoot);
        var result = new List<WorktreeInfo>();

        foreach (var fresh in freshWorktrees)
        {
            if (existingWorktrees.TryGetValue(fresh.Path, out var existing))
            {
                // Update metadata but keep panes
                existing.Branch = fresh.Branch;
                existing.HeadCommit = fresh.HeadCommit;
                existing.IsMain = fresh.IsMain;
                result.Add(existing);
                existingWorktrees.Remove(fresh.Path);
            }
            else
            {
                result.Add(fresh);
            }
        }

        // Dispose panes for removed worktrees
        foreach (var removed in existingWorktrees.Values)
            DisposeWorktreePanes(removed);

        WorktreeList.ItemsSource = result;

        // Restore selection
        if (previousSelection != null)
        {
            var match = result.FindIndex(w => w.Path == previousSelection);
            if (match >= 0) WorktreeList.SelectedIndex = match;
        }
    }

    private void DisposeWorktreePanes(WorktreeInfo wt)
    {
        foreach (var pane in wt.Panes)
        {
            try { pane.Control.DisconnectConPTYTerm(); } catch { }
            if (pane.Session != null)
            {
                try { pane.Session.CloseStdinToApp(); } catch { }
            }
        }
        wt.Panes.Clear();
    }

    private void WorktreeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WorktreeList.SelectedItem is not WorktreeInfo target) return;
        if (target == _currentWorktree) return;

        // Detach current worktree's pane borders from the grid
        if (_currentWorktree != null)
        {
            foreach (var pane in _currentWorktree.Panes)
            {
                try { pane.Control.DisconnectConPTYTerm(); } catch { }
            }
            TerminalTileGrid.Children.Clear();
        }

        _currentWorktree = target;
        _focusedPaneIndex = 0;

        // If target has no panes yet, create the first one
        if (target.Panes.Count == 0)
            CreatePane(target);
        else
        {
            // Reattach existing sessions
            foreach (var pane in target.Panes)
            {
                if (pane.Session != null)
                {
                    pane.Control.ConPTYTerm = pane.Session;
                    SendToTermDelayed(pane.Session, "\r", 100);
                }
            }
        }

        RebuildTileLayout();

        // Refresh file explorer if visible
        if (_filesVisible)
            LoadFileTree(target.Path);
    }

    private void SendToTermDelayed(TermPTY session, string text, int delayMs)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try { session.WriteToTerm(text); } catch { }
        };
        timer.Start();
    }

    private void NewWorktree_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new NewWorktreeDialog(_repoRoot) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            var (success, message) = GitService.AddWorktree(_repoRoot, dialog.BranchName, dialog.WorktreePath);
            if (success)
            {
                LoadWorktrees();
                if (WorktreeList.ItemsSource is List<WorktreeInfo> list)
                {
                    var idx = list.FindIndex(w => w.Path == dialog.WorktreePath);
                    if (idx >= 0) WorktreeList.SelectedIndex = idx;
                }
            }
            else
            {
                MessageBox.Show($"Failed to create worktree:\n{message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void Refresh_Click(object? sender, RoutedEventArgs e) => LoadWorktrees();

    private void DeleteWorktree_Click(object? sender, RoutedEventArgs e)
    {
        if (WorktreeList.SelectedItem is not WorktreeInfo target) return;

        if (target.IsMain)
        {
            MessageBox.Show("Cannot remove the main worktree.", "Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show(
            $"Remove worktree '{target.Branch}'?\nPath: {target.Path}\n\nThis will delete the directory.",
            "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        // Detach from grid if this is the current worktree
        if (target == _currentWorktree)
        {
            TerminalTileGrid.Children.Clear();
            _currentWorktree = null;
        }

        // Dispose all panes
        DisposeWorktreePanes(target);

        var (success, message) = GitService.RemoveWorktree(_repoRoot, target.Path);
        if (!success)
        {
            MessageBox.Show($"Failed to remove worktree:\n{message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }

        LoadWorktrees();
        if (WorktreeList.Items.Count > 0 && WorktreeList.SelectedIndex < 0)
            WorktreeList.SelectedIndex = 0;
    }

    // ── File explorer ───────────────────────────────────────────────

    private void ToggleFiles_Click(object? sender, RoutedEventArgs e)
    {
        _filesVisible = !_filesVisible;

        if (_filesVisible)
        {
            FilesColumn.Width = new GridLength(250);
            FileExplorerPanel.Visibility = Visibility.Visible;
            FilesSplitter.Visibility = Visibility.Visible;

            if (_currentWorktree != null)
                LoadFileTree(_currentWorktree.Path);

            FileTreeView.Focus();
        }
        else
        {
            FilesColumn.Width = new GridLength(0);
            FileExplorerPanel.Visibility = Visibility.Collapsed;
            FilesSplitter.Visibility = Visibility.Collapsed;
        }
    }

    private void LoadFileTree(string rootPath)
    {
        var items = FileExplorerService.GetDirectoryItems(rootPath);
        FileTreeView.ItemsSource = items;
    }

    private void FileTreeView_Expanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem tvi && tvi.DataContext is FileItem item)
        {
            if (item.IsDirectory && item.HasDummyChild)
            {
                item.Children = FileExplorerService.GetDirectoryItems(item.FullPath);
                tvi.ItemsSource = item.Children;
            }
        }
    }

    private void FileTreeView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileTreeView.SelectedItem is FileItem item && !item.IsDirectory)
            OpenFileInVSCode(item.FullPath);
    }

    private void FileTreeView_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && FileTreeView.SelectedItem is FileItem item && !item.IsDirectory)
        {
            OpenFileInVSCode(item.FullPath);
            e.Handled = true;
        }
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control
                 && FileTreeView.SelectedItem is FileItem copyItem)
        {
            Clipboard.SetText(copyItem.FullPath);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            // Return focus to the focused terminal pane
            if (_currentWorktree != null && _focusedPaneIndex < _currentWorktree.Panes.Count)
                _currentWorktree.Panes[_focusedPaneIndex].Control.Focus();
            e.Handled = true;
        }
    }

    // ── File explorer context menu ────────────────────────────────────

    private void TreeViewItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TreeViewItem tvi)
        {
            tvi.IsSelected = true;
            e.Handled = true;
        }
    }

    private void TreeViewItem_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is TreeViewItem tvi && tvi.DataContext is FileItem item
            && tvi.ContextMenu is ContextMenu menu)
        {
            foreach (var obj in menu.Items)
            {
                if (obj is MenuItem mi && mi.Header is string header && header == "Open in VS Code")
                    mi.Visibility = item.IsDirectory ? Visibility.Collapsed : Visibility.Visible;
            }
        }
    }

    private static FileItem? GetFileItemFromContextMenu(object sender)
    {
        if (sender is MenuItem mi && mi.Parent is ContextMenu cm
            && cm.PlacementTarget is TreeViewItem tvi)
            return tvi.DataContext as FileItem;
        return null;
    }

    private void Context_OpenVSCode(object sender, RoutedEventArgs e)
    {
        if (GetFileItemFromContextMenu(sender) is { } item && !item.IsDirectory)
            OpenFileInVSCode(item.FullPath);
    }

    private void Context_OpenExplorer(object sender, RoutedEventArgs e)
    {
        if (GetFileItemFromContextMenu(sender) is { } item)
            Process.Start("explorer.exe", $"/select,\"{item.FullPath}\"");
    }

    private void Context_CopyPath(object sender, RoutedEventArgs e)
    {
        if (GetFileItemFromContextMenu(sender) is { } item)
            Clipboard.SetText(item.FullPath);
    }

    private void Context_CopyName(object sender, RoutedEventArgs e)
    {
        if (GetFileItemFromContextMenu(sender) is { } item)
            Clipboard.SetText(item.Name);
    }

    // ── VS Code ─────────────────────────────────────────────────────

    private void OpenVSCode_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentWorktree == null) return;
        OpenInVSCode(_currentWorktree.Path);
    }

    private static void OpenInVSCode(string path) => LaunchCode($"\"{path}\"");

    private static void OpenFileInVSCode(string filePath) => LaunchCode($"\"{filePath}\"");

    private static void LaunchCode(string arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c code {arguments}",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch
        {
            MessageBox.Show("Could not open VS Code. Is 'code' in your PATH?", "Error");
        }
    }

    // ── Voice settings ────────────────────────────────────────────────

    private void VoiceSettings_Click(object? sender, RoutedEventArgs e)
    {
        var config = ConfigService.Load();
        var dialog = new VoiceSettingsDialog(config) { Owner = this };
        if (dialog.ShowDialog() == true)
            RestartVoiceBridge();
    }

    private void RestartVoiceBridge()
    {
        _voiceService?.Dispose();

        var config = ConfigService.Load();
        _voiceService = new VoiceService(Dispatcher, config);
        _voiceService.StateChanged += OnVoiceStateChanged;
        _voiceService.PromptReady += OnVoicePromptReady;
        _voiceService.ErrorOccurred += OnVoiceError;
        _voiceService.StartBridge();
    }

    // ── Voice service ───────────────────────────────────────────────

    private void OnVoiceStateChanged(VoiceState state)
    {
        VoiceStatusText.Text = state switch
        {
            VoiceState.Disconnected => "\u26a0\ufe0f Voice Offline",
            VoiceState.Idle => "\U0001f3a4 Voice Ready",
            VoiceState.Recording => $"\U0001f534 Recording... ({KeybindingsService.Load().VoiceToggle} to stop)",
            VoiceState.Processing => "\u23f3 Refining prompt...",
            _ => ""
        };

        VoiceStatusText.Foreground = state switch
        {
            VoiceState.Recording => new SolidColorBrush(Color.FromRgb(0xff, 0x44, 0x44)),
            VoiceState.Processing => new SolidColorBrush(Color.FromRgb(0xff, 0xcc, 0x00)),
            _ => new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88))
        };
    }

    private void OnVoicePromptReady(string text)
    {
        VoiceOverlay.Show(text);
    }

    private void OnPromptAccepted(string text)
    {
        if (_currentWorktree != null && _focusedPaneIndex < _currentWorktree.Panes.Count)
        {
            var session = _currentWorktree.Panes[_focusedPaneIndex].Session;
            if (session != null)
            {
                try { session.WriteToTerm(text + "\r"); } catch { }
            }
        }

        if (_currentWorktree != null && _focusedPaneIndex < _currentWorktree.Panes.Count)
            _currentWorktree.Panes[_focusedPaneIndex].Control.Focus();
    }

    private void OnPromptDiscarded()
    {
        if (_currentWorktree != null && _focusedPaneIndex < _currentWorktree.Panes.Count)
            _currentWorktree.Panes[_focusedPaneIndex].Control.Focus();
    }

    private void OnVoiceError(string message)
    {
        VoiceStatusText.Text = $"\u274c {message}";
        VoiceStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xff, 0x44, 0x44));

        // Auto-clear after 5 seconds
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_voiceService != null)
                OnVoiceStateChanged(_voiceService.State);
        };
        timer.Start();
    }

    // ── Shutdown ────────────────────────────────────────────────────

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _voiceService?.Dispose();

        if (WorktreeList.ItemsSource is List<WorktreeInfo> list)
        {
            foreach (var wt in list)
                DisposeWorktreePanes(wt);
        }
    }
}
