using System.Windows.Input;

namespace Repitito.Services;

/// <summary>
/// Sends key strokes to the operating system.
/// </summary>
public interface IKeySender
{
    void SendKeyPress(Key key, ModifierKeys modifiers, char? recordedCharacter = null);
}
