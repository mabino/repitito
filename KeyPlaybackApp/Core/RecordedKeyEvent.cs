using System;
using System.Windows.Input;

namespace Repitito.Core;

/// <summary>
/// Represents a key press captured during recording, including optional character data for playback.
/// </summary>
public sealed record RecordedKeyEvent(Key Key, TimeSpan DelaySincePrevious, char? Character = null)
{
    public static RecordedKeyEvent First(Key key, char? character = null) => new(key, TimeSpan.Zero, character);

    public RecordedKeyEvent WithCharacter(char character) => this with { Character = character };
}
