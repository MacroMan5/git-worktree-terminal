using System.Text.Json.Serialization;

namespace tmuxlike.Models;

public class VoiceBridgeConfig
{
    [JsonPropertyName("whisperModel")]
    public string WhisperModel { get; set; } = "base";

    [JsonPropertyName("language")]
    public string Language { get; set; } = "fr";

    [JsonPropertyName("ollamaModel")]
    public string OllamaModel { get; set; } = "qwen2.5-coder:7b";

    [JsonPropertyName("ollamaUrl")]
    public string OllamaUrl { get; set; } = "http://localhost:11434/api/generate";

    [JsonPropertyName("maxRecordSeconds")]
    public int MaxRecordSeconds { get; set; } = 60;

    [JsonPropertyName("refineTimeout")]
    public int RefineTimeout { get; set; } = 10;

    [JsonPropertyName("pythonPath")]
    public string PythonPath { get; set; } = "python";

    [JsonPropertyName("host")]
    public string Host { get; set; } = "localhost";

    [JsonPropertyName("port")]
    public int Port { get; set; } = 5005;

    [JsonPropertyName("reconnectDelayMs")]
    public int ReconnectDelayMs { get; set; } = 5000;

    [JsonPropertyName("connectTimeoutMs")]
    public int ConnectTimeoutMs { get; set; } = 3000;

    [JsonPropertyName("systemPrompt")]
    public string? SystemPrompt { get; set; }
}
