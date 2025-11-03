using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Input;
using System.Windows.Interop;

namespace Repitito.Services;

/// <summary>
/// Registers a system-wide hotkey and raises <see cref="HotKeyPressed"/> when triggered.
/// </summary>
internal sealed class GlobalHotKeyManager : IDisposable
{
    private const int WmHotKey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    private static int _idSeed;

    private readonly IntPtr _windowHandle;
    private readonly HwndSource _hwndSource;
    private readonly int _hotKeyId;
    private bool _disposed;

    public GlobalHotKeyManager(WindowInteropHelper windowHelper, Key key, ModifierKeys modifiers)
    {
        if (windowHelper.Handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Window handle is not created yet. Call after SourceInitialized.");
        }

        _windowHandle = windowHelper.Handle;
        _hwndSource = HwndSource.FromHwnd(_windowHandle) ?? throw new InvalidOperationException("Failed to acquire HWND source.");
        _hotKeyId = Interlocked.Increment(ref _idSeed);

        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        var modifierFlags = ConvertModifiers(modifiers);

        if (!RegisterHotKey(_windowHandle, _hotKeyId, modifierFlags, virtualKey))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to register global hotkey.");
        }

        _hwndSource.AddHook(WndProc);
    }

    public event EventHandler? HotKeyPressed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _hwndSource.RemoveHook(WndProc);
        UnregisterHotKey(_windowHandle, _hotKeyId);
        GC.SuppressFinalize(this);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotKey && wParam.ToInt32() == _hotKeyId)
        {
            handled = true;
            HotKeyPressed?.Invoke(this, EventArgs.Empty);
        }

        return IntPtr.Zero;
    }

    private static uint ConvertModifiers(ModifierKeys modifiers)
    {
        uint result = 0;
        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            result |= ModAlt;
        }
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            result |= ModControl;
        }
        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            result |= ModShift;
        }
        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            result |= ModWin;
        }

        return result;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
