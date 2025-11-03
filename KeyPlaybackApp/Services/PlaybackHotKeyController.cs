using System;

namespace Repitito.Services;

/// <summary>
/// Encapsulates the decision logic for responding to the playback toggle hotkey.
/// </summary>
internal sealed class PlaybackHotKeyController
{
    private readonly Func<bool> _isRecording;
    private readonly Func<bool> _isPlaying;
    private readonly Func<int> _recordedCount;
    private readonly Action _stopRecording;
    private readonly Action _startPlayback;
    private readonly Action _cancelPlayback;

    public PlaybackHotKeyController(
        Func<bool> isRecording,
        Func<bool> isPlaying,
        Func<int> recordedCount,
        Action stopRecording,
        Action startPlayback,
        Action cancelPlayback)
    {
        _isRecording = isRecording ?? throw new ArgumentNullException(nameof(isRecording));
        _isPlaying = isPlaying ?? throw new ArgumentNullException(nameof(isPlaying));
        _recordedCount = recordedCount ?? throw new ArgumentNullException(nameof(recordedCount));
        _stopRecording = stopRecording ?? throw new ArgumentNullException(nameof(stopRecording));
        _startPlayback = startPlayback ?? throw new ArgumentNullException(nameof(startPlayback));
        _cancelPlayback = cancelPlayback ?? throw new ArgumentNullException(nameof(cancelPlayback));
    }

    public HotKeyResult HandleHotKey()
    {
        if (_isRecording())
        {
            _stopRecording();
        }

        if (_isPlaying())
        {
            _cancelPlayback();
            return HotKeyResult.CancelledPlayback;
        }

        if (_recordedCount() == 0)
        {
            return HotKeyResult.NoRecordingAvailable;
        }

        _startPlayback();
        return HotKeyResult.StartedPlayback;
    }
}

internal enum HotKeyResult
{
    StartedPlayback,
    CancelledPlayback,
    NoRecordingAvailable
}
