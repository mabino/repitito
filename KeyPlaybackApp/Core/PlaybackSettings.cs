using System;

namespace Repitito.Core;

/// <summary>
/// Configures how recorded key events should be replayed.
/// </summary>
public sealed class PlaybackSettings
{
    public bool RandomizeOrder { get; init; }
    public double SpeedMultiplier { get; init; } = 1.0;
    public double VarianceMilliseconds { get; init; }
    public bool EnableVarianceJitter { get; init; }
    public double VarianceJitterPercent { get; init; }
    public double MinimumDelayMilliseconds { get; init; } = 5;
    public bool LoopPlayback { get; init; } = true;

    public void Validate()
    {
        if (SpeedMultiplier <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(SpeedMultiplier), "Speed multiplier must be positive.");
        }

        if (VarianceMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(VarianceMilliseconds), "Variance must be non-negative.");
        }

        if (VarianceJitterPercent < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(VarianceJitterPercent), "Jitter percent must be non-negative.");
        }

        if (MinimumDelayMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumDelayMilliseconds), "Minimum delay must be non-negative.");
        }
    }
}
