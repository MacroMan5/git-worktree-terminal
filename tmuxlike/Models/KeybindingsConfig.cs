using System.Text.Json.Serialization;

namespace tmuxlike.Models;

public class KeybindingsConfig
{
    [JsonPropertyName("newWorktree")]
    public string NewWorktree { get; set; } = "Ctrl+N";

    [JsonPropertyName("refresh")]
    public string Refresh { get; set; } = "F5";

    [JsonPropertyName("deleteWorktree")]
    public string DeleteWorktree { get; set; } = "Delete";

    [JsonPropertyName("toggleFiles")]
    public string ToggleFiles { get; set; } = "Ctrl+E";

    [JsonPropertyName("openVSCode")]
    public string OpenVSCode { get; set; } = "Ctrl+O";

    [JsonPropertyName("splitPane")]
    public string SplitPane { get; set; } = "Ctrl+T";

    [JsonPropertyName("closePane")]
    public string ClosePane { get; set; } = "Ctrl+W";

    [JsonPropertyName("nextPane")]
    public string NextPane { get; set; } = "Ctrl+Tab";

    [JsonPropertyName("prevPane")]
    public string PrevPane { get; set; } = "Ctrl+Shift+Tab";

    [JsonPropertyName("nextWorktree")]
    public string NextWorktree { get; set; } = "Alt+Down";

    [JsonPropertyName("prevWorktree")]
    public string PrevWorktree { get; set; } = "Alt+Up";

    [JsonPropertyName("voiceToggle")]
    public string VoiceToggle { get; set; } = "Ctrl+Shift+V";
}
