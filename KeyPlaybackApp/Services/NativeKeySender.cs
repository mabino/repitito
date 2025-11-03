using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace KeyPlaybackApp.Services;

/// <summary>
/// Sends key strokes using the Win32 SendInput API.
/// </summary>
public sealed class NativeKeySender : IKeySender
{
    private readonly INativeKeyboard _nativeKeyboard;
    private static readonly int InputStructSize = Marshal.SizeOf<INPUT>();

    public NativeKeySender()
        : this(new User32NativeKeyboard())
    {
    }

    internal NativeKeySender(INativeKeyboard nativeKeyboard)
    {
        _nativeKeyboard = nativeKeyboard ?? throw new ArgumentNullException(nameof(nativeKeyboard));
    }

    internal static int InputStructSizeForTests => InputStructSize;

    public void SendKeyPress(Key key)
    {
        var virtualKey = (ushort)KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0)
        {
            return;
        }

    var failures = new List<string>();
        string? unicodeFailure = null;
        string? vkFailure = null;
        string? secondaryUnicodeFailure = null;
        var charCode = _nativeKeyboard.MapVirtualKeyToChar(virtualKey);
        var hasCharacter = charCode != 0;

        if (hasCharacter && TrySendUnicode((char)charCode, out unicodeFailure))
        {
            return;
        }
        if (unicodeFailure is not null)
        {
            failures.Add(unicodeFailure);
        }

        var scanResult = _nativeKeyboard.MapVirtualKey(virtualKey);
        var scanCode = (ushort)(scanResult & 0xFF);
        var mappedExtended = (scanResult & 0x0100) != 0;

        if (scanCode == 0)
        {
            if (!TrySendVirtualKey(virtualKey, out vkFailure)
                && !(hasCharacter && TrySendUnicode((char)charCode, out secondaryUnicodeFailure)))
            {
                if (vkFailure is not null)
                {
                    failures.Add(vkFailure);
                }
                if (secondaryUnicodeFailure is not null)
                {
                    failures.Add(secondaryUnicodeFailure);
                }

                throw Failure("SendInput failed to map virtual key " + virtualKey + ".", failures);
            }

            return;
        }

        var isExtended = mappedExtended || IsExtendedKey(virtualKey);

        if (!TrySendScanCode(virtualKey, scanCode, isExtended, out var scanFailure))
        {
            if (scanFailure is not null)
            {
                failures.Add(scanFailure);
            }

            var vkSuccess = TrySendVirtualKey(virtualKey, out vkFailure);
            if (!vkSuccess)
            {
                if (vkFailure is not null)
                {
                    failures.Add(vkFailure);
                }
            }

            var unicodeSuccess = hasCharacter && TrySendUnicode((char)charCode, out secondaryUnicodeFailure);
            if (!unicodeSuccess && secondaryUnicodeFailure is not null)
            {
                failures.Add(secondaryUnicodeFailure);
            }

            if (!vkSuccess && !unicodeSuccess)
            {
                throw Failure("SendInput failed for virtual key " + virtualKey + " using all strategies.", failures);
            }
        }
    }

    private bool TrySendScanCode(ushort virtualKey, ushort scanCode, bool isExtended, out string? failure)
    {
        try
        {
            _nativeKeyboard.SendScanCodeEvent(scanCode, isKeyUp: false, isExtended, virtualKey);
            _nativeKeyboard.SendScanCodeEvent(scanCode, isKeyUp: true, isExtended, virtualKey);
            failure = null;
            return true;
        }
        catch (InvalidOperationException ex)
        {
            failure = "ScanCode: " + ex.Message;
            if (!IsInvalidParameterError(ex))
            {
                throw;
            }
            return false;
        }
    }

    private bool TrySendVirtualKey(ushort virtualKey, out string? failure)
    {
        try
        {
            _nativeKeyboard.SendVirtualKeyEvent(virtualKey, isKeyUp: false);
            _nativeKeyboard.SendVirtualKeyEvent(virtualKey, isKeyUp: true);
            failure = null;
            return true;
        }
        catch (InvalidOperationException ex)
        {
            failure = "VirtualKey: " + ex.Message;
            if (!IsInvalidParameterError(ex))
            {
                throw;
            }
            return false;
        }
    }

    private bool TrySendUnicode(char character, out string? failure)
    {
        try
        {
            _nativeKeyboard.SendUnicodeEvent(character, isKeyUp: false);
            _nativeKeyboard.SendUnicodeEvent(character, isKeyUp: true);
            failure = null;
            return true;
        }
        catch (InvalidOperationException ex)
        {
            failure = "Unicode: " + ex.Message;
            if (!IsInvalidParameterError(ex))
            {
                throw;
            }
            return false;
        }
    }

    private static bool IsInvalidParameterError(InvalidOperationException ex)
    {
        return ex.Message.Contains("error code 87", StringComparison.OrdinalIgnoreCase);
    }

    private static InvalidOperationException Failure(string message, List<string> failures)
    {
        failures.Insert(0, "INPUT.cbSize=" + InputStructSize);
        if (failures.Count == 0)
        {
            return new InvalidOperationException(message);
        }

        return new InvalidOperationException(message + " Details: " + string.Join(" | ", failures));
    }

    private static bool IsExtendedKey(ushort vk) => vk switch
    {
        0x21 or 0x22 or 0x23 or 0x24 => true, // PageUp/PageDown/End/Home
        0x25 or 0x26 or 0x27 or 0x28 => true, // Arrow keys
        0x2D or 0x2E or 0x2C => true,         // Insert/Delete/PrintScreen
        0x5B or 0x5C or 0x5D => true,         // Windows/App keys
        0x90 or 0x91 => true,                 // NumLock/ScrollLock
        0xA3 or 0xA5 => true,                 // Right Ctrl / Right Alt
        0xA2 or 0xA4 => true,                 // Left Ctrl / Left Alt
        0xA6 or 0xA7 or 0xA8 or 0xA9 => true,
        0xAA or 0xAB or 0xAC or 0xAD or 0xAE or 0xAF => true,
        0xB0 or 0xB1 or 0xB2 or 0xB3 or 0xB4 or 0xB5 => true,
        _ => false
    };

    internal interface INativeKeyboard
    {
        uint MapVirtualKey(uint virtualKey);
        uint MapVirtualKeyToChar(uint virtualKey);
        void SendScanCodeEvent(ushort scanCode, bool isKeyUp, bool isExtended, ushort originalVirtualKey);
        void SendVirtualKeyEvent(ushort virtualKey, bool isKeyUp);
        void SendUnicodeEvent(char character, bool isKeyUp);
    }

    private sealed class User32NativeKeyboard : INativeKeyboard
    {
        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_SCANCODE = 0x0008;
        private const uint KEYEVENTF_UNICODE = 0x0004;
        private const uint MAPVK_VK_TO_VSC = 0x0;
        private const uint MAPVK_VK_TO_CHAR = 0x2;

        public uint MapVirtualKey(uint virtualKey)
        {
            return NativeMethods.MapVirtualKey(virtualKey, MAPVK_VK_TO_VSC);
        }

        public uint MapVirtualKeyToChar(uint virtualKey)
        {
            return NativeMethods.MapVirtualKey(virtualKey, MAPVK_VK_TO_CHAR);
        }

        public void SendScanCodeEvent(ushort scanCode, bool isKeyUp, bool isExtended, ushort originalVirtualKey)
        {
            uint flags = KEYEVENTF_SCANCODE;
            if (isKeyUp)
            {
                flags |= KEYEVENTF_KEYUP;
            }

            if (isExtended)
            {
                flags |= KEYEVENTF_EXTENDEDKEY;
            }

            var input = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = scanCode,
                        dwFlags = flags,
                        dwExtraInfo = IntPtr.Zero,
                        time = 0
                    }
                }
            };

            if (NativeMethods.SendInput(1, new[] { input }, InputStructSize) == 0)
            {
                var error = Marshal.GetLastWin32Error();
                throw new InvalidOperationException("SendInput failed with error code " + error + " (VK=" + originalVirtualKey + ")");
            }
        }

        public void SendVirtualKeyEvent(ushort virtualKey, bool isKeyUp)
        {
            uint flags = isKeyUp ? KEYEVENTF_KEYUP : 0;

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

            if (NativeMethods.SendInput(1, new[] { input }, InputStructSize) == 0)
            {
                var error = Marshal.GetLastWin32Error();
                throw new InvalidOperationException("SendInput VK fallback failed with error code " + error + " (VK=" + virtualKey + ")");
            }
        }

        public void SendUnicodeEvent(char character, bool isKeyUp)
        {
            uint flags = KEYEVENTF_UNICODE;
            if (isKeyUp)
            {
                flags |= KEYEVENTF_KEYUP;
            }

            var input = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = character,
                        dwFlags = flags,
                        dwExtraInfo = IntPtr.Zero,
                        time = 0
                    }
                }
            };

            if (NativeMethods.SendInput(1, new[] { input }, InputStructSize) == 0)
            {
                var error = Marshal.GetLastWin32Error();
                throw new InvalidOperationException("SendInput Unicode fallback failed with error code " + error + " (Char=" + (int)character + ")");
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KEYBDINPUT ki;
        [FieldOffset(0)]
        public MOUSEINPUT mi;
        [FieldOffset(0)]
        public HARDWAREINPUT hi;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern uint MapVirtualKey(uint uCode, uint uMapType);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    }
}
