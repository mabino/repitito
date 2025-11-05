using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;

namespace Repitito.Core;

/// <summary>
/// Handles import and export of recorded key sequences with schema validation.
/// </summary>
public static class RecordingSerializer
{
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(IEnumerable<RecordedKeyEvent> events)
    {
        if (events is null)
        {
            throw new ArgumentNullException(nameof(events));
        }

        var document = new RecordingDocument
        {
            Version = CurrentVersion,
            Entries = events.Select(e => new RecordingEntry
            {
                Key = e.Key.ToString(),
                Modifiers = CollectModifierNames(e.Modifiers).ToList(),
                DelayMilliseconds = (int)Math.Max(0, Math.Round(e.DelaySincePrevious.TotalMilliseconds)),
                Character = e.Character.HasValue ? e.Character.Value.ToString() : null,
                Comment = string.IsNullOrWhiteSpace(e.Comment) ? null : e.Comment
            }).ToList()
        };

        return JsonSerializer.Serialize(document, Options);
    }

    public static bool TryDeserialize(string json, out List<RecordedKeyEvent> events, out string error)
    {
        events = new List<RecordedKeyEvent>();
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "Recording file is empty.";
            return false;
        }

        RecordingDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<RecordingDocument>(json, Options);
        }
        catch (JsonException ex)
        {
            error = "Invalid JSON: " + ex.Message;
            return false;
        }

        if (document is null)
        {
            error = "Recording file is empty.";
            return false;
        }

        if (document.Version != CurrentVersion)
        {
            error = "Unsupported recording version: " + document.Version + ".";
            return false;
        }

        if (document.Entries is null || document.Entries.Count == 0)
        {
            error = "Recording file contains no entries.";
            return false;
        }

        var keyConverter = TypeDescriptor.GetConverter(typeof(Key));
        for (var i = 0; i < document.Entries.Count; i++)
        {
            var entry = document.Entries[i];
            var entryNumber = i + 1;

            if (string.IsNullOrWhiteSpace(entry.Key))
            {
                error = $"Entry {entryNumber} is missing a key.";
                return false;
            }

            Key key;
            try
            {
                var converted = keyConverter.ConvertFromString(null, CultureInfo.InvariantCulture, entry.Key);
                if (converted is not Key parsedKey)
                {
                    error = $"Entry {entryNumber} has unknown key '{entry.Key}'.";
                    return false;
                }

                key = parsedKey;
            }
            catch (Exception)
            {
                error = $"Entry {entryNumber} has unknown key '{entry.Key}'.";
                return false;
            }

            if (entry.DelayMilliseconds < 0)
            {
                error = $"Entry {entryNumber} has a negative delay.";
                return false;
            }

            if (!TryParseModifiers(entry.Modifiers, out var modifiers, out error))
            {
                error = $"Entry {entryNumber}: {error}";
                return false;
            }

            char? character = null;
            if (!string.IsNullOrEmpty(entry.Character))
            {
                if (entry.Character!.Length != 1)
                {
                    error = $"Entry {entryNumber} has an invalid character literal.";
                    return false;
                }

                character = entry.Character[0];
            }

            var comment = string.IsNullOrWhiteSpace(entry.Comment) ? null : entry.Comment;
            events.Add(new RecordedKeyEvent(key, TimeSpan.FromMilliseconds(entry.DelayMilliseconds), modifiers, character, comment));
        }

        return true;
    }

    private static IReadOnlyList<string> CollectModifierNames(ModifierKeys modifiers)
    {
        var names = new List<string>(4);
        if ((modifiers & ModifierKeys.Control) != 0)
        {
            names.Add("Control");
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
            names.Add("Windows");
        }

        return names;
    }

    private static bool TryParseModifiers(IReadOnlyList<string>? values, out ModifierKeys modifiers, out string error)
    {
        modifiers = ModifierKeys.None;
        error = string.Empty;

        if (values is null)
        {
            return true;
        }

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                error = "Modifier name cannot be blank.";
                return false;
            }

            switch (value.Trim())
            {
                case "Control":
                case "Ctrl":
                    modifiers |= ModifierKeys.Control;
                    break;
                case "Shift":
                    modifiers |= ModifierKeys.Shift;
                    break;
                case "Alt":
                    modifiers |= ModifierKeys.Alt;
                    break;
                case "Windows":
                case "Win":
                case "Meta":
                    modifiers |= ModifierKeys.Windows;
                    break;
                default:
                    error = "Unknown modifier '" + value + "'.";
                    return false;
            }
        }

        return true;
    }

    private sealed class RecordingDocument
    {
        public int Version { get; set; }

        public List<RecordingEntry>? Entries { get; set; }
    }

    private sealed class RecordingEntry
    {
        public string? Key { get; set; }

        public List<string>? Modifiers { get; set; }

        public int DelayMilliseconds { get; set; }

        public string? Character { get; set; }

        public string? Comment { get; set; }
    }
}
