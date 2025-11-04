using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Repitito.Core;

namespace Repitito.Services;

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
        if (events is null)
        {
            throw new ArgumentNullException(nameof(events));
        }

        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var plan = _planner.BuildPlan(events, settings);
            if (plan.Count == 0)
            {
                return;
            }

            foreach (var action in plan)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(action.DelayBeforeMilliseconds, cancellationToken).ConfigureAwait(false);
                _keySender.SendKeyPress(action.Key, action.Character);
            }

            if (!settings.LoopPlayback)
            {
                break;
            }
        }
    }
}
