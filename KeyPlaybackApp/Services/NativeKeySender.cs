using System;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace KeyPlaybackApp.Services;

/// <summary>
/// Sends key strokes using the Win32 SendInput API.
/// </summary>
public sealed class NativeKeySender : IKeySender
{
    public void SendKeyPress(Key key)
    {
        var virtualKey = (ushort)KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0)
        {
            return;
        }

        Send(virtualKey, 0);
        Send(virtualKey, KEYEVENTF_KEYUP);
    }

    private static void Send(ushort virtualKey, uint flags)
    {
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = virtualKey,
                    wScan = 0,
                    dwFlags = flags,
                    dwExtraInfo = IntPtr.Zero,
                    time = 0
                }
            }
        };

        if (SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>()) == 0)
        {
            throw new InvalidOperationException("SendInput failed with error code " + Marshal.GetLastWin32Error());
        }
    }

    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
}
