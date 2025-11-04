using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;

namespace Repitito.Core;

/// <summary>
/// Utility helpers for formatting and parsing inline key labels used by the recording grid.
/// </summary>
public static class InlineKeyLabel
{
    private static readonly TypeConverter KeyConverter = TypeDescriptor.GetConverter(typeof(Key));

    public static string Format(Key key, char? character) =>
        character.HasValue
            ? key + " (\"" + character.Value + "\")"
            : key.ToString();

    public static bool TryParse(string? input, out Key key, out char? character, out string displayLabel, out string error)
    {
        key = Key.None;
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

        try
        {
            var converted = KeyConverter.ConvertFromString(null, CultureInfo.InvariantCulture, keyPortion);
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
            error = $"Unknown key '{keyPortion}'.";
            return false;
        }

        displayLabel = Format(key, character);
        error = string.Empty;
        return true;
    }
}
