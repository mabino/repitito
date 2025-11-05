using System;
using System.Windows.Input;

namespace Repitito.Core;

/// <summary>
/// Represents a key press captured during recording, including optional character data, modifiers, and comments for playback.
/// </summary>
public sealed record RecordedKeyEvent(Key Key, TimeSpan DelaySincePrevious, ModifierKeys Modifiers = ModifierKeys.None, char? Character = null, string? Comment = null)
{
    public static RecordedKeyEvent First(Key key, ModifierKeys modifiers = ModifierKeys.None, char? character = null, string? comment = null) => new(key, TimeSpan.Zero, modifiers, character, comment);

    public RecordedKeyEvent WithCharacter(char character) => this with { Character = character };

    public RecordedKeyEvent WithModifiers(ModifierKeys modifiers) => this with { Modifiers = modifiers };

    public RecordedKeyEvent WithComment(string? comment) => this with { Comment = comment };
}
