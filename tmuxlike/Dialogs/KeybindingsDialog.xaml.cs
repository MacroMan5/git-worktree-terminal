using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using tmuxlike.Models;
using tmuxlike.Services;

namespace tmuxlike.Dialogs;

public partial class KeybindingsDialog : Window
{
    private readonly List<KeybindingEntry> _entries = [];
    private KeybindingEntry? _recordingEntry;
    private string? _conflictCombo;

    public KeybindingsDialog(KeybindingsConfig config)
    {
        InitializeComponent();
        LoadEntries(config);
        BindingsList.ItemsSource = _entries;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void LoadEntries(KeybindingsConfig config)
    {
        _entries.Clear();
        _entries.AddRange(
        [
            new("newWorktree", "New Worktree", config.NewWorktree),
            new("refresh", "Refresh", config.Refresh),
            new("deleteWorktree", "Delete Worktree", config.DeleteWorktree),
            new("toggleFiles", "Toggle Files", config.ToggleFiles),
            new("openVSCode", "Open VS Code", config.OpenVSCode),
            new("splitPane", "Split Pane", config.SplitPane),
            new("closePane", "Close Pane", config.ClosePane),
            new("nextPane", "Next Pane", config.NextPane),
            new("prevPane", "Previous Pane", config.PrevPane),
            new("nextWorktree", "Next Worktree", config.NextWorktree),
            new("prevWorktree", "Previous Worktree", config.PrevWorktree),
            new("voiceToggle", "Voice Toggle", config.VoiceToggle),
        ]);
    }

    private void BindingsList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (BindingsList.SelectedItem is KeybindingEntry entry)
            StartRecording(entry);
    }

    private void StartRecording(KeybindingEntry entry)
    {
        _recordingEntry = entry;
        _conflictCombo = null;
        StatusText.Text = $"Press a key combo for \"{entry.DisplayName}\"... (Escape to cancel)";
        StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0xff, 0xcc, 0x00));
    }

    private void StopRecording()
    {
        _recordingEntry = null;
        _conflictCombo = null;
        StatusText.Text = "";
        StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88));
        BindingsList.SelectedItem = null;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_recordingEntry == null)
            return;

        e.Handled = true;

        var key = KeyComboParser.ResolveKey(e);

        // Escape cancels recording
        if (key == Key.Escape)
        {
            StopRecording();
            return;
        }

        // Ignore bare modifier keys — wait for a non-modifier
        if (KeyComboParser.IsModifierKey(key))
            return;

        var modifiers = Keyboard.Modifiers;
        var combo = KeyComboParser.Format(key, modifiers);

        // Check for duplicates
        var conflict = _entries.FirstOrDefault(
            entry => entry != _recordingEntry &&
                     string.Equals(entry.Shortcut, combo, StringComparison.OrdinalIgnoreCase));

        if (conflict != null)
        {
            // If the user presses the same conflicting combo a second time, force-assign it
            if (string.Equals(_conflictCombo, combo, StringComparison.OrdinalIgnoreCase))
            {
                conflict.Shortcut = "";
                _recordingEntry.Shortcut = combo;
                StopRecording();
                return;
            }

            _conflictCombo = combo;
            StatusText.Text = $"\"{combo}\" is used by \"{conflict.DisplayName}\". Press again to reassign.";
            StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xff, 0x88, 0x00));
            return;
        }

        _recordingEntry.Shortcut = combo;
        StopRecording();
    }

    private KeybindingsConfig BuildConfig()
    {
        var config = new KeybindingsConfig();
        foreach (var entry in _entries)
        {
            switch (entry.Id)
            {
                case "newWorktree": config.NewWorktree = entry.Shortcut; break;
                case "refresh": config.Refresh = entry.Shortcut; break;
                case "deleteWorktree": config.DeleteWorktree = entry.Shortcut; break;
                case "toggleFiles": config.ToggleFiles = entry.Shortcut; break;
                case "openVSCode": config.OpenVSCode = entry.Shortcut; break;
                case "splitPane": config.SplitPane = entry.Shortcut; break;
                case "closePane": config.ClosePane = entry.Shortcut; break;
                case "nextPane": config.NextPane = entry.Shortcut; break;
                case "prevPane": config.PrevPane = entry.Shortcut; break;
                case "nextWorktree": config.NextWorktree = entry.Shortcut; break;
                case "prevWorktree": config.PrevWorktree = entry.Shortcut; break;
                case "voiceToggle": config.VoiceToggle = entry.Shortcut; break;
            }
        }
        return config;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var config = BuildConfig();
        KeybindingsService.Save(config);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show("Reset all shortcuts to defaults?", "Confirm Reset",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        LoadEntries(new KeybindingsConfig());
        BindingsList.ItemsSource = null;
        BindingsList.ItemsSource = _entries;
        StopRecording();
    }

    private void OpenConfigFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = KeybindingsService.ConfigFilePath,
                UseShellExecute = true
            });
        }
        catch
        {
            MessageBox.Show($"Could not open:\n{KeybindingsService.ConfigFilePath}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public class KeybindingEntry : INotifyPropertyChanged
    {
        private string _shortcut;

        public string Id { get; }
        public string DisplayName { get; }

        public string Shortcut
        {
            get => _shortcut;
            set
            {
                if (_shortcut == value) return;
                _shortcut = value;
                OnPropertyChanged();
            }
        }

        public KeybindingEntry(string id, string displayName, string shortcut)
        {
            Id = id;
            DisplayName = displayName;
            _shortcut = shortcut;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
