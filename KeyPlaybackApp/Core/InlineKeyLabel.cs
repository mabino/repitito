using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Windows.Input;

namespace Repitito.Core;

/// <summary>
/// Utility helpers for formatting and parsing inline key labels used by the recording grid.
/// </summary>
public static class InlineKeyLabel
{
    private static readonly TypeConverter KeyConverter = TypeDescriptor.GetConverter(typeof(Key));
    private static readonly Dictionary<string, ModifierKeys> ModifierLookup = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Control"] = ModifierKeys.Control,
        ["Ctrl"] = ModifierKeys.Control,
        ["Alt"] = ModifierKeys.Alt,
        ["Shift"] = ModifierKeys.Shift,
        ["Win"] = ModifierKeys.Windows,
        ["Windows"] = ModifierKeys.Windows,
        ["Meta"] = ModifierKeys.Windows
    };

    public static string Format(Key key, ModifierKeys modifiers, char? character)
    {
        var builder = new StringBuilder();
        var modifierNames = CollectModifierNames(modifiers);
        if (modifierNames.Count > 0)
        {
            builder.Append(string.Join('+', modifierNames));
            builder.Append('+');
        }

        builder.Append(key);

        if (character.HasValue)
        {
            builder.Append(" (\"");
            builder.Append(character.Value);
            builder.Append("\")");
        }

        return builder.ToString();
    }

    public static bool TryParse(string? input, out Key key, out ModifierKeys modifiers, out char? character, out string displayLabel, out string error)
    {
        key = Key.None;
        modifiers = ModifierKeys.None;
        character = null;
        displayLabel = string.Empty;
        error = "Enter a key name.";

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var trimmed = input.Trim();
        var keyPortion = trimmed;
        var markerIndex = trimmed.IndexOf("(\"", StringComparison.Ordinal);
        if (markerIndex >= 0)
        {
            var closingIndex = trimmed.IndexOf("\")", markerIndex, StringComparison.Ordinal);
            if (closingIndex < 0)
            {
                error = "Character literal must end with \")\".";
                return false;
            }

            var charStart = markerIndex + 2;
            var charLength = closingIndex - charStart;
            if (charLength != 1)
            {
                error = "Character literal must contain exactly one character.";
                return false;
            }

            character = trimmed.Substring(charStart, charLength)[0];
            keyPortion = trimmed.Substring(0, markerIndex).Trim();

            var trailing = trimmed.Substring(closingIndex + 2).Trim();
            if (trailing.Length > 0)
            {
                error = "Remove extra text after the character literal.";
                return false;
            }
        }

        if (string.IsNullOrWhiteSpace(keyPortion))
        {
            error = "Enter the name of the key to send.";
            return false;
        }

        if (!TryParseModifiersAndKey(keyPortion, out key, out modifiers, out error))
        {
            return false;
        }

        displayLabel = Format(key, modifiers, character);
        error = string.Empty;
        return true;
    }

    private static bool TryParseModifiersAndKey(string input, out Key key, out ModifierKeys modifiers, out string error)
    {
        modifiers = ModifierKeys.None;
        key = Key.None;
        error = string.Empty;

        var parts = input.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            error = "Enter the name of the key to send.";
            return false;
        }

        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (!ModifierLookup.TryGetValue(parts[i], out var modifier))
            {
                error = $"Unknown modifier '{parts[i]}'.";
                return false;
            }

            modifiers |= modifier;
        }

        var keySegment = parts[^1];
        if (string.IsNullOrWhiteSpace(keySegment))
        {
            error = "Enter the name of the key to send.";
            return false;
        }

        try
        {
            var converted = KeyConverter.ConvertFromString(null, CultureInfo.InvariantCulture, keySegment);
            if (converted is not Key parsedKey)
            {
                error = "Unknown key name.";
                return false;
            }

            if (parsedKey == Key.None)
            {
                error = "Key name must resolve to a specific key.";
                return false;
            }

            key = parsedKey;
        }
        catch (Exception)
        {
            error = $"Unknown key '{keySegment}'.";
            return false;
        }

        return true;
    }

    private static IReadOnlyList<string> CollectModifierNames(ModifierKeys modifiers)
    {
        var names = new List<string>(4);
        if ((modifiers & ModifierKeys.Control) != 0)
        {
            names.Add("Ctrl");
        }

        if ((modifiers & ModifierKeys.Shift) != 0)
        {
            names.Add("Shift");
        }

        if ((modifiers & ModifierKeys.Alt) != 0)
        {
            names.Add("Alt");
        }

        if ((modifiers & ModifierKeys.Windows) != 0)
        {
            names.Add("Win");
        }

        return names;
    }
}
