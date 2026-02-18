using System.Diagnostics;
using System.Windows;
using tmuxlike.Models;
using tmuxlike.Services;

namespace tmuxlike.Dialogs;

public partial class VoiceSettingsDialog : Window
{
    public VoiceSettingsDialog(VoiceBridgeConfig config)
    {
        InitializeComponent();
        LoadConfig(config);
    }

    private void LoadConfig(VoiceBridgeConfig config)
    {
        WhisperModelInput.Text = config.WhisperModel;
        LanguageInput.Text = config.Language;
        OllamaModelInput.Text = config.OllamaModel;
        OllamaUrlInput.Text = config.OllamaUrl;
        MaxRecordInput.Text = config.MaxRecordSeconds.ToString();
        RefineTimeoutInput.Text = config.RefineTimeout.ToString();
        PythonPathInput.Text = config.PythonPath;
        HostInput.Text = config.Host;
        PortInput.Text = config.Port.ToString();
        ReconnectDelayInput.Text = config.ReconnectDelayMs.ToString();
        ConnectTimeoutInput.Text = config.ConnectTimeoutMs.ToString();
        SystemPromptInput.Text = config.SystemPrompt ?? "";
    }

    private VoiceBridgeConfig BuildConfig()
    {
        return new VoiceBridgeConfig
        {
            WhisperModel = WhisperModelInput.Text.Trim(),
            Language = LanguageInput.Text.Trim(),
            OllamaModel = OllamaModelInput.Text.Trim(),
            OllamaUrl = OllamaUrlInput.Text.Trim(),
            MaxRecordSeconds = int.TryParse(MaxRecordInput.Text.Trim(), out var mr) ? mr : 60,
            RefineTimeout = int.TryParse(RefineTimeoutInput.Text.Trim(), out var rt) ? rt : 10,
            PythonPath = PythonPathInput.Text.Trim(),
            Host = HostInput.Text.Trim(),
            Port = int.TryParse(PortInput.Text.Trim(), out var p) ? p : 5005,
            ReconnectDelayMs = int.TryParse(ReconnectDelayInput.Text.Trim(), out var rd) ? rd : 5000,
            ConnectTimeoutMs = int.TryParse(ConnectTimeoutInput.Text.Trim(), out var ct) ? ct : 3000,
            SystemPrompt = string.IsNullOrWhiteSpace(SystemPromptInput.Text) ? null : SystemPromptInput.Text.Trim()
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var config = BuildConfig();
        ConfigService.Save(config);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void BrowsePython_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*",
            Title = "Select Python executable"
        };

        if (dialog.ShowDialog() == true)
            PythonPathInput.Text = dialog.FileName;
    }

    private void OpenConfigFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = ConfigService.ConfigFilePath,
                UseShellExecute = true
            });
        }
        catch
        {
            MessageBox.Show($"Could not open:\n{ConfigService.ConfigFilePath}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ResetSystemPrompt_Click(object sender, RoutedEventArgs e)
    {
        SystemPromptInput.Text = "";
    }
}
