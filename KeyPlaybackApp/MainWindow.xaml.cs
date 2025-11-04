using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Repitito.Core;
using Repitito.Services;

namespace Repitito;

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
    private int? _editingRowIndex;

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

        CancelActiveInlineEdit(false);
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

        CancelActiveInlineEdit(false);
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

        CancelActiveInlineEdit(true);

        var settings = BuildSettings();
        try
        {
            _playbackCancellation = new CancellationTokenSource();
            _isPlaying = true;
            UpdateUiState();
            StatusText.Text = settings.LoopPlayback
                ? "Playing back sequence in a loop… Press Cancel to stop."
                : "Playing back sequence…";
            await _playbackService.PlayAsync(_recordedEvents, settings, _playbackCancellation.Token);
            StatusText.Text = settings.LoopPlayback
                ? "Playback loop finished."
                : "Playback finished.";
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
                    StatusText.Text = LoopPlaybackCheckBox?.IsChecked != false
                        ? "Starting looping playback via F8…"
                        : "Starting playback via F8…";
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
            LoopPlayback = LoopPlaybackCheckBox?.IsChecked != false,
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
        var displayKey = entry.Character.HasValue
            ? entry.Key + " (\"" + entry.Character.Value + "\")"
            : entry.Key.ToString();
        var row = new RecordingRow(_recordedEvents.Count, displayKey, delayMilliseconds);
        _recordingRows.Add(row);
        StatusText.Text = $"Captured: {entry.Key}. Total {_recordedEvents.Count} keys.";
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
        var displayKey = lastEvent.Key + " (\"" + character + "\")";
        _recordingRows[lastIndex].SetKey(displayKey);

        StatusText.Text = $"Captured: {lastEvent.Key} (\"{character}\"). Total {_recordedEvents.Count} keys.";
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
        LoopPlaybackCheckBox.IsEnabled = !_isRecording && !_isPlaying;
        RandomizeOrderCheckBox.IsEnabled = !_isRecording && !_isPlaying;
        SpeedSlider.IsEnabled = !_isRecording;
        VarianceSlider.IsEnabled = !_isRecording;
        VarianceJitterCheckBox.IsEnabled = !_isRecording;
        JitterSlider.IsEnabled = !_isRecording && VarianceJitterCheckBox.IsChecked == true;
        MinimumDelaySlider.IsEnabled = !_isRecording;
    }

    private void DelayCell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2)
        {
            return;
        }

        if (sender is not FrameworkElement element || element.DataContext is not RecordingRow row)
        {
            return;
        }

        e.Handled = true;
        if (_isRecording || _isPlaying)
        {
            StatusText.Text = "Finish recording or playback before editing delays.";
            return;
        }

        StartInlineEdit(row);
    }

    private void DelayEditor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.DataContext is not RecordingRow row)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            EndInlineEdit(row, true);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            EndInlineEdit(row, false);
            e.Handled = true;
        }
    }

    private void DelayEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.DataContext is not RecordingRow row)
        {
            return;
        }

        textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        EndInlineEdit(row, true);
    }

    private void StartInlineEdit(RecordingRow row)
    {
        var index = _recordingRows.IndexOf(row);
        if (index < 0)
        {
            return;
        }

        if (_editingRowIndex.HasValue && _editingRowIndex.Value != index)
        {
            var current = _recordingRows.ElementAtOrDefault(_editingRowIndex.Value);
            if (current is not null)
            {
                EndInlineEdit(current, false);
            }
        }

        row.IsEditing = true;
        row.DelayText = row.DelayMilliseconds.ToString(CultureInfo.InvariantCulture);
        _editingRowIndex = index;

        Dispatcher.InvokeAsync(() =>
        {
            var container = RecordingList.ItemContainerGenerator.ContainerFromIndex(index) as ListViewItem;
            if (container == null)
            {
                return;
            }

            var editor = FindVisualChild<TextBox>(container, "DelayEditor");
            if (editor != null)
            {
                editor.Focus();
                editor.SelectAll();
            }
        }, DispatcherPriority.Background);
    }

    private void EndInlineEdit(RecordingRow row, bool commit)
    {
        var index = _recordingRows.IndexOf(row);
        if (index < 0)
        {
            return;
        }

        if (commit && int.TryParse(row.DelayText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            var clamped = Math.Max(0, parsed);
            var eventIndex = row.Index - 1;
            if (eventIndex >= 0 && eventIndex < _recordedEvents.Count)
            {
                var existing = _recordedEvents[eventIndex];
                _recordedEvents[eventIndex] = existing with { DelaySincePrevious = TimeSpan.FromMilliseconds(clamped) };
            }

            row.SetDelay(clamped);
            StatusText.Text = $"Updated delay for entry #{row.Index} to {clamped} ms.";
        }
        else
        {
            row.DelayText = row.DelayMilliseconds.ToString(CultureInfo.InvariantCulture);
        }

        row.IsEditing = false;
        if (_editingRowIndex == index)
        {
            _editingRowIndex = null;
        }
    }

    private void CancelActiveInlineEdit(bool commit)
    {
        if (!_editingRowIndex.HasValue)
        {
            return;
        }

        var row = _recordingRows.ElementAtOrDefault(_editingRowIndex.Value);
        if (row != null)
        {
            EndInlineEdit(row, commit);
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent, string name) where T : DependencyObject
    {
        if (parent == null)
        {
            return null;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed && child is FrameworkElement fe && fe.Name == name)
            {
                return typed;
            }

            var result = FindVisualChild<T>(child, name);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }

    private sealed class RecordingRow : INotifyPropertyChanged
    {
        private string _key;
        private int _delayMilliseconds;
        private bool _isEditing;
        private string _delayText;

        public RecordingRow(int index, string key, int delayMilliseconds)
        {
            Index = index;
            _key = key;
            _delayMilliseconds = delayMilliseconds;
            _delayText = delayMilliseconds.ToString(CultureInfo.InvariantCulture);
        }

        public int Index { get; }

        public string Key
        {
            get => _key;
            private set => SetField(ref _key, value, nameof(Key));
        }

        public int DelayMilliseconds
        {
            get => _delayMilliseconds;
            private set
            {
                if (SetField(ref _delayMilliseconds, value, nameof(DelayMilliseconds)))
                {
                    OnPropertyChanged(nameof(Delay));
                }
            }
        }

        public string Delay => DelayMilliseconds.ToString(CultureInfo.InvariantCulture);

        public string DelayText
        {
            get => _delayText;
            set => SetField(ref _delayText, value, nameof(DelayText));
        }

        public bool IsEditing
        {
            get => _isEditing;
            set => SetField(ref _isEditing, value, nameof(IsEditing));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public void SetDelay(int delayMilliseconds)
        {
            DelayMilliseconds = delayMilliseconds;
            DelayText = delayMilliseconds.ToString(CultureInfo.InvariantCulture);
        }

        public void SetKey(string key) => Key = key;

        private bool SetField<TValue>(ref TValue field, TValue value, params string[] propertyNames)
        {
            if (EqualityComparer<TValue>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            foreach (var propertyName in propertyNames)
            {
                OnPropertyChanged(propertyName);
            }

            return true;
        }

        private void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}