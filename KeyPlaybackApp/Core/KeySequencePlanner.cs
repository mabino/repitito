using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace Repitito.Core;

/// <summary>
/// Generates the concrete playback sequence from the captured events and playback settings.
/// </summary>
public sealed class KeySequencePlanner
{
    private readonly IRandomSource _randomSource;

    public KeySequencePlanner(IRandomSource? randomSource = null)
    {
        _randomSource = randomSource ?? new SystemRandomSource();
    }

    public IReadOnlyList<KeyPlaybackAction> BuildPlan(IEnumerable<RecordedKeyEvent> recorded, PlaybackSettings settings)
    {
        if (recorded is null)
        {
            throw new ArgumentNullException(nameof(recorded));
        }

        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        settings.Validate();

        var materialized = recorded.ToList();
        if (materialized.Count == 0)
        {
            return Array.Empty<KeyPlaybackAction>();
        }

        var ordered = settings.RandomizeOrder
            ? Shuffle(materialized)
            : materialized;

        var actions = new List<KeyPlaybackAction>(ordered.Count);
        foreach (var evt in ordered)
        {
            var delayMillis = ComputeDelayMilliseconds(evt, settings);
            actions.Add(new KeyPlaybackAction(evt.Key, evt.Modifiers, evt.Character, delayMillis));
        }

        return actions;
    }

    private List<RecordedKeyEvent> Shuffle(List<RecordedKeyEvent> events)
    {
        var list = new List<RecordedKeyEvent>(events);
        for (var i = list.Count - 1; i > 0; i--)
        {
            var swapIndex = _randomSource.Next(0, i + 1);
            (list[i], list[swapIndex]) = (list[swapIndex], list[i]);
        }

        return list;
    }

    private int ComputeDelayMilliseconds(RecordedKeyEvent evt, PlaybackSettings settings)
    {
        var baseDelay = evt.DelaySincePrevious.TotalMilliseconds;
        if (baseDelay < 0)
        {
            baseDelay = 0;
        }

        var scaled = baseDelay * settings.SpeedMultiplier;

        var variance = settings.VarianceMilliseconds;
        if (settings.EnableVarianceJitter && variance > 0 && settings.VarianceJitterPercent > 0)
        {
            var jitterRange = variance * (settings.VarianceJitterPercent / 100d);
            var jitter = (2 * _randomSource.NextDouble() - 1) * jitterRange;
            variance = Math.Max(0, variance + jitter);
        }

        var varianceOffset = variance > 0
            ? (2 * _randomSource.NextDouble() - 1) * variance
            : 0;

        var finalDelay = Math.Max(settings.MinimumDelayMilliseconds, scaled + varianceOffset);
        return (int)Math.Round(finalDelay, MidpointRounding.AwayFromZero);
    }
}

/// <summary>
/// Represents a single key action along with the delay before it should be invoked.
/// </summary>
public sealed record KeyPlaybackAction(Key Key, ModifierKeys Modifiers, char? Character, int DelayBeforeMilliseconds);
