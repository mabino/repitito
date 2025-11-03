using System.Windows.Input;

namespace KeyPlaybackApp.Services;

/// <summary>
/// Sends key strokes to the operating system.
/// </summary>
public interface IKeySender
{
    void SendKeyPress(Key key, char? recordedCharacter = null);
}
