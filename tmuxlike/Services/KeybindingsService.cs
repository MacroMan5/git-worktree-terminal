using System.Diagnostics;
using System.IO;
using System.Text.Json;
using tmuxlike.Models;

namespace tmuxlike.Services;

public class KeybindingsService
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true
    };

    public static string ConfigFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "tmuxlike", "keybindings.json");

    public static KeybindingsConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigFilePath))
                return new KeybindingsConfig();

            var json = File.ReadAllText(ConfigFilePath);
            return JsonSerializer.Deserialize<KeybindingsConfig>(json) ?? new KeybindingsConfig();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[KeybindingsService] Failed to load config: {ex.Message}");
            return new KeybindingsConfig();
        }
    }

    public static void Save(KeybindingsConfig config)
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

        Save(new KeybindingsConfig());
    }
}
