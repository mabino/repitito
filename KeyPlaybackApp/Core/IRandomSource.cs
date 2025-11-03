using System;

namespace Repitito.Core;

/// <summary>
/// Provides deterministic random values for the playback planner.
/// </summary>
public interface IRandomSource
{
    double NextDouble();
    int Next(int minInclusive, int maxExclusive);
}

/// <summary>
/// Default random source using <see cref="Random"/>.
/// </summary>
public sealed class SystemRandomSource : IRandomSource
{
    private readonly Random _random;

    public SystemRandomSource() : this(new Random())
    {
    }

    public SystemRandomSource(Random random) => _random = random ?? throw new ArgumentNullException(nameof(random));

    public double NextDouble() => _random.NextDouble();

    public int Next(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);
}
