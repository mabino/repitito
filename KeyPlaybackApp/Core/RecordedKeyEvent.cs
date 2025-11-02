using System;
using System.Windows.Input;

namespace KeyPlaybackApp.Core;

/// <summary>
/// Represents a key press captured during recording.
/// </summary>
public sealed record RecordedKeyEvent(Key Key, TimeSpan DelaySincePrevious)
{
    public static RecordedKeyEvent First(Key key) => new(key, TimeSpan.Zero);
}
