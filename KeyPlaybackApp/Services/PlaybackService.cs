using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KeyPlaybackApp.Core;

namespace KeyPlaybackApp.Services;

/// <summary>
/// Plays back recorded key events according to the provided settings.
/// </summary>
public sealed class PlaybackService
{
    private readonly IKeySender _keySender;
    private readonly KeySequencePlanner _planner;

    public PlaybackService(IKeySender keySender, KeySequencePlanner? planner = null)
    {
        _keySender = keySender ?? throw new ArgumentNullException(nameof(keySender));
        _planner = planner ?? new KeySequencePlanner();
    }

    public async Task PlayAsync(IEnumerable<RecordedKeyEvent> events, PlaybackSettings settings, CancellationToken cancellationToken)
    {
        var plan = _planner.BuildPlan(events, settings);
        foreach (var action in plan)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(action.DelayBeforeMilliseconds, cancellationToken).ConfigureAwait(false);
            _keySender.SendKeyPress(action.Key);
        }
    }
}
