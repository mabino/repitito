using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
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
    private InlineField _activeInlineField = InlineField.None;
    private Point _dragStartPoint;
    private RecordingRow? _dragSourceRow;

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

        if (_recordedEvents.Count > 0)
        {
            var message = _recordedEvents.Count == 1
                ? "Starting a new recording will clear 1 captured entry. Continue?"
                : $"Starting a new recording will clear {_recordedEvents.Count} captured entries. Continue?";

            var result = MessageBox.Show(this, message, "Replace Existing Recording?", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (result != MessageBoxResult.OK)
            {
                StatusText.Text = "Recording unchanged.";
                return;
            }
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
        RecordingList.SelectedItem = null;
        StatusText.Text = "Recording cleared.";
        UpdateUiState();
    }

    private void DeleteRow(object sender, RoutedEventArgs e)
    {
        if (_isRecording || _isPlaying)
        {
            return;
        }

        if (RecordingList.SelectedItem is not RecordingRow row)
        {
            return;
        }

        var eventIndex = row.Index - 1;
        if (eventIndex < 0 || eventIndex >= _recordedEvents.Count)
        {
            return;
        }

        CancelActiveInlineEdit(true);

        _recordingRows.RemoveAt(eventIndex);
        _recordedEvents.RemoveAt(eventIndex);
        UpdateRowIndexes();

        if (_recordingRows.Count == 0)
        {
            RecordingList.SelectedItem = null;
        }
        else
        {
            var fallbackIndex = Math.Min(eventIndex, _recordingRows.Count - 1);
            RecordingList.SelectedItem = _recordingRows[fallbackIndex];
        }

        StatusText.Text = $"Deleted entry #{row.Index}.";
        UpdateUiState();
    }

    private void ImportRecording(object sender, RoutedEventArgs e)
    {
        if (_isRecording || _isPlaying)
        {
            StatusText.Text = "Stop recording or playback before importing.";
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "Repitito recordings (*.json)|*.json|All files (*.*)|*.*",
            Title = "Import Recording"
        };

        if (dialog.ShowDialog(this) != true)
        {
            StatusText.Text = "Import cancelled.";
            return;
        }

        string json;
        try
        {
            json = File.ReadAllText(dialog.FileName);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Failed to read recording: " + ex.Message;
            return;
        }

        if (!RecordingSerializer.TryDeserialize(json, out var events, out var error))
        {
            MessageBox.Show(this, error, "Import Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Import failed.";
            return;
        }

        ApplyRecording(events);
        StatusText.Text = $"Imported {events.Count} entries from {Path.GetFileName(dialog.FileName)}.";
        UpdateUiState();
    }

    private void ExportRecording(object sender, RoutedEventArgs e)
    {
        if (_recordedEvents.Count == 0)
        {
            StatusText.Text = "Nothing to export.";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "Repitito recordings (*.json)|*.json|All files (*.*)|*.*",
            Title = "Export Recording",
            FileName = "recording.json"
        };

        if (dialog.ShowDialog(this) != true)
        {
            StatusText.Text = "Export cancelled.";
            return;
        }

        var payload = RecordingSerializer.Serialize(_recordedEvents);

        try
        {
            File.WriteAllText(dialog.FileName, payload);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Failed to write recording: " + ex.Message;
            return;
        }

        StatusText.Text = $"Recording saved to {Path.GetFileName(dialog.FileName)}.";
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

    private void ApplyRecording(IReadOnlyList<RecordedKeyEvent> events)
    {
        CancelActiveInlineEdit(false);
        _recordedEvents.Clear();
        _recordedEvents.AddRange(events);
        RebuildRecordingRows();
        _stopwatch.Reset();
        _lastRecordedElapsed = TimeSpan.Zero;
        RecordingList.SelectedItem = _recordingRows.Count > 0 ? _recordingRows[0] : null;
        UpdateUiState();
    }

    private void RebuildRecordingRows()
    {
        _recordingRows.Clear();
        for (var i = 0; i < _recordedEvents.Count; i++)
        {
            var recordedEvent = _recordedEvents[i];
            var displayKey = InlineKeyLabel.Format(recordedEvent.Key, recordedEvent.Modifiers, recordedEvent.Character);
            var delayMilliseconds = Convert.ToInt32(Math.Round(recordedEvent.DelaySincePrevious.TotalMilliseconds));
            _recordingRows.Add(new RecordingRow(i + 1, displayKey, delayMilliseconds));
        }
    }

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

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        var keyModifier = ToModifierFlag(key);
        if (keyModifier != ModifierKeys.None)
        {
            modifiers &= ~keyModifier;
        }

        var currentElapsed = _stopwatch.Elapsed;
        var delay = _recordedEvents.Count == 0
            ? TimeSpan.Zero
            : currentElapsed - _lastRecordedElapsed;

        _lastRecordedElapsed = currentElapsed;
        var entry = _recordedEvents.Count == 0
            ? RecordedKeyEvent.First(key, modifiers)
            : new RecordedKeyEvent(key, delay, modifiers);

        _recordedEvents.Add(entry);
        var delayMilliseconds = Convert.ToInt32(Math.Round(delay.TotalMilliseconds));
        var displayKey = InlineKeyLabel.Format(entry.Key, entry.Modifiers, entry.Character);
        var row = new RecordingRow(_recordedEvents.Count, displayKey, delayMilliseconds);
        _recordingRows.Add(row);
        StatusText.Text = $"Captured: {displayKey}. Total {_recordedEvents.Count} keys.";
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
        var displayKey = InlineKeyLabel.Format(lastEvent.Key, lastEvent.Modifiers, character);
        _recordingRows[lastIndex].SetKey(displayKey);

        StatusText.Text = $"Captured: {displayKey}. Total {_recordedEvents.Count} keys.";
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
        DeleteRowButton.IsEnabled = !_isRecording && !_isPlaying && RecordingList.SelectedItem is RecordingRow;
        LoopPlaybackCheckBox.IsEnabled = !_isRecording && !_isPlaying;
        RandomizeOrderCheckBox.IsEnabled = !_isRecording && !_isPlaying;
        SpeedSlider.IsEnabled = !_isRecording;
        VarianceSlider.IsEnabled = !_isRecording;
        VarianceJitterCheckBox.IsEnabled = !_isRecording;
        JitterSlider.IsEnabled = !_isRecording && VarianceJitterCheckBox.IsChecked == true;
        MinimumDelaySlider.IsEnabled = !_isRecording;
        ImportRecordingButton.IsEnabled = !_isRecording && !_isPlaying;
        ExportRecordingButton.IsEnabled = !_isRecording && !_isPlaying && _recordedEvents.Count > 0;
    }

    private void RecordingList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateUiState();

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

        StartInlineEdit(row, InlineField.Delay);
    }

    private void KeyCell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
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
            StatusText.Text = "Finish recording or playback before editing keys.";
            return;
        }

        StartInlineEdit(row, InlineField.Key);
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

    private void KeyEditor_PreviewKeyDown(object sender, KeyEventArgs e)
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

    private void KeyEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.DataContext is not RecordingRow row)
        {
            return;
        }

        textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        EndInlineEdit(row, true);
    }

    private void StartInlineEdit(RecordingRow row, InlineField field)
    {
        var index = _recordingRows.IndexOf(row);
        if (index < 0)
        {
            return;
        }

        if (_editingRowIndex.HasValue)
        {
            if (_editingRowIndex.Value != index || _activeInlineField != field)
            {
                CancelActiveInlineEdit(false);
            }
            else
            {
                return;
            }
        }

        _editingRowIndex = index;
        _activeInlineField = field;
        row.IsDelayEditing = field == InlineField.Delay;
        row.IsKeyEditing = field == InlineField.Key;

        if (field == InlineField.Delay)
        {
            row.DelayText = row.DelayMilliseconds.ToString(CultureInfo.InvariantCulture);
        }
        else if (field == InlineField.Key)
        {
            row.KeyText = row.Key;
        }

        Dispatcher.InvokeAsync(() =>
        {
            var container = RecordingList.ItemContainerGenerator.ContainerFromIndex(index) as ListViewItem;
            if (container == null)
            {
                return;
            }

            var editorName = field == InlineField.Delay ? "DelayEditor" : "KeyEditor";
            var editor = FindVisualChild<TextBox>(container, editorName);
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
            ResetInlineState(row);
            return;
        }

        switch (_activeInlineField)
        {
            case InlineField.Delay:
                if (commit && int.TryParse(row.DelayText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedDelay))
                {
                    var clamped = Math.Max(0, parsedDelay);
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

                row.IsDelayEditing = false;
                break;

            case InlineField.Key:
                if (commit)
                {
                    if (InlineKeyLabel.TryParse(row.KeyText, out var parsedKey, out var parsedModifiers, out var parsedCharacter, out var displayLabel, out var error))
                    {
                        var eventIndex = row.Index - 1;
                        if (eventIndex >= 0 && eventIndex < _recordedEvents.Count)
                        {
                            var existing = _recordedEvents[eventIndex];
                            var appliedCharacter = parsedCharacter ?? existing.Character;
                            var updated = existing with
                            {
                                Key = parsedKey,
                                Modifiers = parsedModifiers,
                                Character = appliedCharacter
                            };
                            _recordedEvents[eventIndex] = updated;
                            displayLabel = InlineKeyLabel.Format(parsedKey, parsedModifiers, appliedCharacter);
                        }

                        row.SetKey(displayLabel);
                        StatusText.Text = $"Updated key for entry #{row.Index} to {displayLabel}.";
                    }
                    else
                    {
                        row.KeyText = row.Key;
                        StatusText.Text = error;
                    }
                }
                else
                {
                    row.KeyText = row.Key;
                }

                row.IsKeyEditing = false;
                break;

            default:
                return;
        }

        if (_editingRowIndex == index)
        {
            _editingRowIndex = null;
        }

        _activeInlineField = InlineField.None;
    }

    private void CancelActiveInlineEdit(bool commit)
    {
        if (!_editingRowIndex.HasValue || _activeInlineField == InlineField.None)
        {
            return;
        }

        var row = _recordingRows.ElementAtOrDefault(_editingRowIndex.Value);
        if (row != null)
        {
            EndInlineEdit(row, commit);
        }
        else
        {
            _editingRowIndex = null;
            _activeInlineField = InlineField.None;
        }
    }

    private void ResetInlineState(RecordingRow row)
    {
        row.IsDelayEditing = false;
        row.IsKeyEditing = false;
        _editingRowIndex = null;
        _activeInlineField = InlineField.None;
    }

    private static ModifierKeys ToModifierFlag(Key key) => key switch
    {
        Key.LeftCtrl or Key.RightCtrl => ModifierKeys.Control,
        Key.LeftShift or Key.RightShift => ModifierKeys.Shift,
        Key.LeftAlt or Key.RightAlt => ModifierKeys.Alt,
        Key.LWin or Key.RWin => ModifierKeys.Windows,
        _ => ModifierKeys.None
    };

    private void DragHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not RecordingRow row)
        {
            return;
        }

        _dragStartPoint = e.GetPosition(this);
        _dragSourceRow = row;
        e.Handled = true;
    }

    private void DragHandle_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragSourceRow = null;
        e.Handled = true;
    }

    private void DragHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragSourceRow is null)
        {
            return;
        }

        var currentPosition = e.GetPosition(this);
        if (Math.Abs(currentPosition.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(currentPosition.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop((DependencyObject)sender, _dragSourceRow, DragDropEffects.Move);
        _dragSourceRow = null;
        e.Handled = true;
    }

    private void RecordingList_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(RecordingRow)))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private void RecordingList_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(RecordingRow)))
        {
            return;
        }

        if (sender is not ListView listView)
        {
            return;
        }

        var draggedRow = e.Data.GetData(typeof(RecordingRow)) as RecordingRow;
        if (draggedRow is null)
        {
            return;
        }

        var dropTargetItem = FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject);
        var targetRow = dropTargetItem?.DataContext as RecordingRow;

        var oldIndex = _recordingRows.IndexOf(draggedRow);
        if (oldIndex < 0)
        {
            return;
        }

        var requestedIndex = targetRow != null
            ? _recordingRows.IndexOf(targetRow)
            : _recordingRows.Count;

        MoveRecordingRow(oldIndex, requestedIndex);
        listView.SelectedItem = draggedRow;
    }

    private void MoveRecordingRow(int oldIndex, int requestedIndex)
    {
        if (oldIndex < 0 || oldIndex >= _recordingRows.Count)
        {
            return;
        }

        var upperBound = _recordingRows.Count;
        var targetIndex = Math.Max(0, Math.Min(requestedIndex, upperBound));
        if (targetIndex == oldIndex || targetIndex == oldIndex + 1)
        {
            return;
        }

        CancelActiveInlineEdit(true);

        var row = _recordingRows[oldIndex];
        var recordedEvent = _recordedEvents[oldIndex];

        _recordingRows.RemoveAt(oldIndex);
        _recordedEvents.RemoveAt(oldIndex);

        if (targetIndex > oldIndex)
        {
            targetIndex--;
        }

        targetIndex = Math.Max(0, Math.Min(targetIndex, _recordingRows.Count));

        _recordingRows.Insert(targetIndex, row);
        _recordedEvents.Insert(targetIndex, recordedEvent);

        UpdateRowIndexes();

        StatusText.Text = $"Moved entry to position {row.Index}.";
    }

    private void UpdateRowIndexes()
    {
        for (var i = 0; i < _recordingRows.Count; i++)
        {
            _recordingRows[i].SetIndex(i + 1);
        }
    }

    private enum InlineField
    {
        None,
        Delay,
        Key
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
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
        private int _index;
        private string _key;
        private int _delayMilliseconds;
        private bool _isDelayEditing;
        private bool _isKeyEditing;
        private string _delayText;
        private string _keyText;

        public RecordingRow(int index, string key, int delayMilliseconds)
        {
            _index = index;
            _key = key;
            _delayMilliseconds = delayMilliseconds;
            _delayText = delayMilliseconds.ToString(CultureInfo.InvariantCulture);
            _keyText = key;
        }

        public int Index
        {
            get => _index;
            private set => SetField(ref _index, value, nameof(Index));
        }

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

        public string KeyText
        {
            get => _keyText;
            set => SetField(ref _keyText, value, nameof(KeyText));
        }

        public bool IsDelayEditing
        {
            get => _isDelayEditing;
            set => SetField(ref _isDelayEditing, value, nameof(IsDelayEditing));
        }

        public bool IsKeyEditing
        {
            get => _isKeyEditing;
            set => SetField(ref _isKeyEditing, value, nameof(IsKeyEditing));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public void SetDelay(int delayMilliseconds)
        {
            DelayMilliseconds = delayMilliseconds;
            DelayText = delayMilliseconds.ToString(CultureInfo.InvariantCulture);
        }

        public void SetKey(string key)
        {
            Key = key;
            KeyText = key;
        }

        public void SetIndex(int index) => Index = index;

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