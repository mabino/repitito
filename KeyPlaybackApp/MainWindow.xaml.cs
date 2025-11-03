using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using KeyPlaybackApp.Core;
using KeyPlaybackApp.Services;

namespace KeyPlaybackApp;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly ObservableCollection<RecordingRow> _recordingRows = new();
    private readonly List<RecordedKeyEvent> _recordedEvents = new();
    private readonly Stopwatch _stopwatch = new();
    private readonly PlaybackService _playbackService;
    private readonly PlaybackHotKeyController _hotKeyController;
    private TimeSpan _lastRecordedElapsed;
    private bool _isRecording;
    private CancellationTokenSource? _playbackCancellation;
    private bool _isPlaying;
    private GlobalHotKeyManager? _hotKeyManager;

    public MainWindow()
    {
        InitializeComponent();
        RecordingList.ItemsSource = _recordingRows;
        _playbackService = new PlaybackService(new NativeKeySender());
        SpeedValueText.Text = SpeedSlider.Value.ToString("0.0") + "x";
        VarianceValueText.Text = ((int)Math.Round(VarianceSlider.Value)).ToString() + " ms";
        MinimumDelayText.Text = ((int)Math.Round(MinimumDelaySlider.Value)).ToString() + " ms";
        JitterSlider.Value = 0;
        JitterValueText.Text = "0 %";
        UpdateUiState();
        _hotKeyController = new PlaybackHotKeyController(
            () => _isRecording,
            () => _isPlaying,
            () => _recordedEvents.Count,
            StopRecordingFromHotKey,
            StartPlaybackFromHotKey,
            CancelPlaybackFromHotKey);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            var helper = new WindowInteropHelper(this);
            _hotKeyManager = new GlobalHotKeyManager(helper, Key.F8, ModifierKeys.None);
            _hotKeyManager.HotKeyPressed += OnPlaybackHotKey;
            StatusText.Text = "Ready. Press F8 anywhere to toggle playback.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Hotkey unavailable: " + ex.Message;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _hotKeyManager?.Dispose();
        base.OnClosed(e);
    }

    private void StartRecording(object sender, RoutedEventArgs e)
    {
        if (_isPlaying)
        {
            return;
        }

        _recordedEvents.Clear();
        _recordingRows.Clear();
        _stopwatch.Restart();
        _lastRecordedElapsed = TimeSpan.Zero;
        _isRecording = true;
        UpdateUiState();
        StatusText.Text = "Recording… Press keys to capture.";
    }

    private void StopRecording(object sender, RoutedEventArgs e)
    {
        if (!_isRecording)
        {
            return;
        }

        _isRecording = false;
        _stopwatch.Stop();
        UpdateUiState();
        StatusText.Text = _recordedEvents.Count == 0
            ? "Recording stopped. No keys captured."
            : $"Recording stopped. {_recordedEvents.Count} keys captured.";
    }

    private void ClearRecording(object sender, RoutedEventArgs e)
    {
        if (_isRecording)
        {
            StopRecording(sender, e);
        }

        _recordedEvents.Clear();
        _recordingRows.Clear();
        StatusText.Text = "Recording cleared.";
        UpdateUiState();
    }

    private async void Playback(object sender, RoutedEventArgs e)
    {
        if (_recordedEvents.Count == 0)
        {
            StatusText.Text = "Nothing to play. Record some keys first.";
            return;
        }

        if (_isRecording)
        {
            StopRecording(sender, e);
        }

        if (_isPlaying)
        {
            return;
        }

        var settings = BuildSettings();
        try
        {
            _playbackCancellation = new CancellationTokenSource();
            _isPlaying = true;
            UpdateUiState();
            StatusText.Text = "Playing back sequence…";
            await _playbackService.PlayAsync(_recordedEvents, settings, _playbackCancellation.Token);
            StatusText.Text = "Playback finished.";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Playback cancelled.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Playback failed: " + ex.Message;
        }
        finally
        {
            _playbackCancellation?.Dispose();
            _playbackCancellation = null;
            _isPlaying = false;
            UpdateUiState();
        }
    }

    private void CancelPlayback(object sender, RoutedEventArgs e)
    {
        if (_isPlaying)
        {
            StatusText.Text = "Cancelling playback…";
        }

        _playbackCancellation?.Cancel();
    }

    private void OnPlaybackHotKey(object? sender, EventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            var result = _hotKeyController.HandleHotKey();
            switch (result)
            {
                case HotKeyResult.StartedPlayback:
                    StatusText.Text = "Starting playback via F8…";
                    break;
                case HotKeyResult.CancelledPlayback:
                    StatusText.Text = "Stopping playback via F8…";
                    break;
                case HotKeyResult.NoRecordingAvailable:
                    StatusText.Text = "F8 pressed, but no keys are recorded.";
                    break;
            }
        });
    }

    private void StopRecordingFromHotKey() => StopRecording(this, new RoutedEventArgs());

    private void StartPlaybackFromHotKey() => Playback(this, new RoutedEventArgs());

    private void CancelPlaybackFromHotKey() => CancelPlayback(this, new RoutedEventArgs());

    private PlaybackSettings BuildSettings()
    {
        var settings = new PlaybackSettings
        {
            RandomizeOrder = RandomizeOrderCheckBox.IsChecked == true,
            SpeedMultiplier = Math.Round(SpeedSlider.Value, 2),
            VarianceMilliseconds = Math.Round(VarianceSlider.Value, 2),
            EnableVarianceJitter = VarianceJitterCheckBox.IsChecked == true,
            VarianceJitterPercent = Math.Round(JitterSlider.Value, 2),
            MinimumDelayMilliseconds = Math.Round(MinimumDelaySlider.Value, 2)
        };

        return settings;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_isRecording || e.IsRepeat)
        {
            return;
        }

        var currentElapsed = _stopwatch.Elapsed;
        var delay = _recordedEvents.Count == 0
            ? TimeSpan.Zero
            : currentElapsed - _lastRecordedElapsed;

        _lastRecordedElapsed = currentElapsed;
        var entry = _recordedEvents.Count == 0
            ? RecordedKeyEvent.First(e.Key)
            : new RecordedKeyEvent(e.Key, delay);

        _recordedEvents.Add(entry);
        var delayMilliseconds = Convert.ToInt32(Math.Round(delay.TotalMilliseconds));
        _recordingRows.Add(new RecordingRow(_recordedEvents.Count, e.Key.ToString(), delayMilliseconds));
        StatusText.Text = $"Captured: {e.Key}. Total {_recordedEvents.Count} keys.";
    }

    private void Window_TextInput(object sender, TextCompositionEventArgs e)
    {
        if (!_isRecording)
        {
            return;
        }

        if (string.IsNullOrEmpty(e.Text) || e.Text.Length != 1)
        {
            return;
        }

        var character = e.Text[0];
        if (char.IsControl(character) || _recordedEvents.Count == 0)
        {
            return;
        }

        var lastIndex = _recordedEvents.Count - 1;
        var lastEvent = _recordedEvents[lastIndex];
        if (lastEvent.Character == character)
        {
            return;
        }

        if (lastIndex >= _recordingRows.Count)
        {
            return;
        }

        _recordedEvents[lastIndex] = lastEvent.WithCharacter(character);
        var displayKey = lastEvent.Key.ToString();
        var updatedDisplay = displayKey + " (\"" + character + "\")";
        var currentRow = _recordingRows[lastIndex];
        _recordingRows[lastIndex] = currentRow with { Key = updatedDisplay };

        StatusText.Text = $"Captured: {displayKey} (\"{character}\"). Total {_recordedEvents.Count} keys.";
    }

    private void SpeedSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SpeedValueText is null)
        {
            return;
        }

        SpeedValueText.Text = e.NewValue.ToString("0.0") + "x";
    }

    private void VarianceSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VarianceValueText is null)
        {
            return;
        }

        VarianceValueText.Text = ((int)Math.Round(e.NewValue)).ToString() + " ms";
    }

    private void JitterSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (JitterValueText is null)
        {
            return;
        }

        JitterValueText.Text = ((int)Math.Round(e.NewValue)).ToString() + " %";
    }

    private void MinimumDelaySliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MinimumDelayText is null)
        {
            return;
        }

        MinimumDelayText.Text = ((int)Math.Round(e.NewValue)).ToString() + " ms";
    }

    private void VarianceJitterChanged(object sender, RoutedEventArgs e)
    {
        var enabled = VarianceJitterCheckBox.IsChecked == true;
        JitterSlider.IsEnabled = enabled;
        if (!enabled)
        {
            JitterSlider.Value = 0;
        }
    }

    private void UpdateUiState()
    {
        StartRecordingButton.IsEnabled = !_isRecording && !_isPlaying;
        StopRecordingButton.IsEnabled = _isRecording;
        PlaybackButton.IsEnabled = !_isRecording && !_isPlaying && _recordedEvents.Count > 0;
        CancelPlaybackButton.IsEnabled = _isPlaying;
        ClearRecordingButton.IsEnabled = !_isRecording && !_isPlaying && _recordedEvents.Count > 0;
        RandomizeOrderCheckBox.IsEnabled = !_isRecording && !_isPlaying;
        SpeedSlider.IsEnabled = !_isRecording;
        VarianceSlider.IsEnabled = !_isRecording;
        VarianceJitterCheckBox.IsEnabled = !_isRecording;
        JitterSlider.IsEnabled = !_isRecording && VarianceJitterCheckBox.IsChecked == true;
        MinimumDelaySlider.IsEnabled = !_isRecording;
    }

    private sealed record RecordingRow(int Index, string Key, int DelayMilliseconds)
    {
        public string Delay => DelayMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}