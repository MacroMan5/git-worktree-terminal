using System.Windows.Input;

namespace tmuxlike.Services;

public static class KeyComboParser
{
    private static readonly Dictionary<string, Key> SpecialKeyMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Up"] = Key.Up,
        ["Down"] = Key.Down,
        ["Left"] = Key.Left,
        ["Right"] = Key.Right,
        ["Delete"] = Key.Delete,
        ["Del"] = Key.Delete,
        ["Tab"] = Key.Tab,
        ["Enter"] = Key.Enter,
        ["Return"] = Key.Return,
        ["Space"] = Key.Space,
        ["Escape"] = Key.Escape,
        ["Esc"] = Key.Escape,
        ["Backspace"] = Key.Back,
        ["Home"] = Key.Home,
        ["End"] = Key.End,
        ["PageUp"] = Key.PageUp,
        ["PageDown"] = Key.PageDown,
        ["Insert"] = Key.Insert,
        ["F1"] = Key.F1,
        ["F2"] = Key.F2,
        ["F3"] = Key.F3,
        ["F4"] = Key.F4,
        ["F5"] = Key.F5,
        ["F6"] = Key.F6,
        ["F7"] = Key.F7,
        ["F8"] = Key.F8,
        ["F9"] = Key.F9,
        ["F10"] = Key.F10,
        ["F11"] = Key.F11,
        ["F12"] = Key.F12,
    };

    public static bool TryParse(string combo, out Key key, out ModifierKeys modifiers)
    {
        key = Key.None;
        modifiers = ModifierKeys.None;

        if (string.IsNullOrWhiteSpace(combo))
            return false;

        var parts = combo.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return false;

        // All parts except the last are modifiers; the last is the key
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var mod = parts[i].ToLowerInvariant();
            switch (mod)
            {
                case "ctrl":
                case "control":
                    modifiers |= ModifierKeys.Control;
                    break;
                case "alt":
                    modifiers |= ModifierKeys.Alt;
                    break;
                case "shift":
                    modifiers |= ModifierKeys.Shift;
                    break;
                case "win":
                case "windows":
                    modifiers |= ModifierKeys.Windows;
                    break;
                default:
                    return false;
            }
        }

        var keyPart = parts[^1];

        // Check special key map first
        if (SpecialKeyMap.TryGetValue(keyPart, out key))
            return true;

        // Single character letter/digit
        if (keyPart.Length == 1)
        {
            var ch = char.ToUpperInvariant(keyPart[0]);
            if (ch is >= 'A' and <= 'Z')
            {
                key = (Key)(Key.A + (ch - 'A'));
                return true;
            }
            if (ch is >= '0' and <= '9')
            {
                key = (Key)(Key.D0 + (ch - '0'));
                return true;
            }
        }

        // Try standard Enum.TryParse as fallback
        return Enum.TryParse(keyPart, true, out key) && key != Key.None;
    }

    public static string Format(Key key, ModifierKeys modifiers)
    {
        var parts = new List<string>(4);

        if (modifiers.HasFlag(ModifierKeys.Control))
            parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt))
            parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift))
            parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows))
            parts.Add("Win");

        parts.Add(FormatKey(key));
        return string.Join("+", parts);
    }

    private static string FormatKey(Key key)
    {
        return key switch
        {
            Key.Delete => "Delete",
            Key.Back => "Backspace",
            Key.Return or Key.Enter => "Enter",
            Key.Escape => "Escape",
            Key.Space => "Space",
            >= Key.A and <= Key.Z => key.ToString(),
            >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => $"NumPad{(char)('0' + (key - Key.NumPad0))}",
            _ => key.ToString()
        };
    }

    public static bool IsModifierKey(Key key)
    {
        return key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin
            or Key.System;
    }

    /// <summary>
    /// Resolves the actual key from a KeyEventArgs, handling the WPF Key.System quirk
    /// where Alt-modified keys report Key.System instead of the actual key.
    /// </summary>
    public static Key ResolveKey(System.Windows.Input.KeyEventArgs e)
    {
        return e.Key == Key.System ? e.SystemKey : e.Key;
    }
}
