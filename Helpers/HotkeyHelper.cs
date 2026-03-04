using System.Windows.Input;

namespace ReviewTool.Helpers;

public static class HotkeyHelper
{
    public static string NormalizeHotkey(string? hotkey)
    {
        if (string.IsNullOrWhiteSpace(hotkey))
        {
            return string.Empty;
        }

        var parts = hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return string.Empty;
        }

        var hasCtrl = false;
        var hasShift = false;
        string keyPart = string.Empty;

        foreach (var part in parts)
        {
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                hasCtrl = true;
                continue;
            }

            if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                hasShift = true;
                continue;
            }

            keyPart = part;
        }

        if (string.IsNullOrWhiteSpace(keyPart))
        {
            return string.Empty;
        }

        if (Enum.TryParse<Key>(keyPart, ignoreCase: true, out var parsedKey))
        {
            keyPart = parsedKey.ToString();
        }

        if (hasCtrl && hasShift)
        {
            return $"Ctrl+Shift+{keyPart}";
        }

        if (hasCtrl)
        {
            return $"Ctrl+{keyPart}";
        }

        if (hasShift)
        {
            return $"Shift+{keyPart}";
        }

        return keyPart;
    }

    public static bool TryBuildHotkeyString(Key key, ModifierKeys modifiers, out string hotkey)
    {
        hotkey = string.Empty;

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            return false;
        }

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift)
        {
            return false;
        }

        var keyText = key.ToString();
        if (string.IsNullOrWhiteSpace(keyText))
        {
            return false;
        }

        var hasCtrl = modifiers.HasFlag(ModifierKeys.Control);
        var hasShift = modifiers.HasFlag(ModifierKeys.Shift);
        if (!hasCtrl && !hasShift)
        {
            hotkey = keyText;
            return true;
        }

        if (hasCtrl && hasShift)
        {
            hotkey = $"Ctrl+Shift+{keyText}";
            return true;
        }

        if (hasCtrl)
        {
            hotkey = $"Ctrl+{keyText}";
            return true;
        }

        hotkey = $"Shift+{keyText}";
        return true;
    }
}
