using System.Diagnostics;
using System.IO;
using System.Text.Json;
using tmuxlike.Models;

namespace tmuxlike.Services;

public class ConfigService
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true
    };

    public static string ConfigFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "tmuxlike", "voice-bridge.json");

    public static VoiceBridgeConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigFilePath))
                return new VoiceBridgeConfig();

            var json = File.ReadAllText(ConfigFilePath);
            return JsonSerializer.Deserialize<VoiceBridgeConfig>(json) ?? new VoiceBridgeConfig();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConfigService] Failed to load config: {ex.Message}");
            return new VoiceBridgeConfig();
        }
    }

    public static void Save(VoiceBridgeConfig config)
    {
        var dir = Path.GetDirectoryName(ConfigFilePath)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(config, WriteOptions);
        File.WriteAllText(ConfigFilePath, json);
    }

    public static void EnsureConfigExists()
    {
        if (File.Exists(ConfigFilePath))
            return;

        Save(new VoiceBridgeConfig());
    }
}
