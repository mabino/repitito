using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KeyPlaybackApp.Core;
using KeyPlaybackApp.Services;
using System.Windows.Input;

namespace KeyPlaybackApp.Tests;

internal static class Program
{
	private static int Main()
	{
		var runner = new TestRunner();

		runner.Run("KeySequencePlanner.BasicScaling", Tests_KeySequencePlanner.BasicScaling);
		runner.Run("KeySequencePlanner.RandomizesOrder", Tests_KeySequencePlanner.RandomizesOrder);
		runner.Run("KeySequencePlanner.AppliesVariance", Tests_KeySequencePlanner.AppliesVariance);
		runner.Run("KeySequencePlanner.EnforcesMinimumDelay", Tests_KeySequencePlanner.EnforcesMinimumDelay);
		runner.RunAsync("PlaybackService.DispatchesKeys", Tests_PlaybackService.DispatchesKeys).GetAwaiter().GetResult();

		runner.PrintSummary();
		return runner.Failures == 0 ? 0 : 1;
	}
}

internal static class Tests_KeySequencePlanner
{
	public static void BasicScaling()
	{
		var events = new[]
		{
			RecordedKeyEvent.First(Key.A),
			new RecordedKeyEvent(Key.B, TimeSpan.FromMilliseconds(100)),
			new RecordedKeyEvent(Key.C, TimeSpan.FromMilliseconds(150))
		};

		var settings = new PlaybackSettings
		{
			SpeedMultiplier = 2,
			VarianceMilliseconds = 0,
			MinimumDelayMilliseconds = 0
		};

		var planner = new KeySequencePlanner(new DeterministicRandomSource());
		var plan = planner.BuildPlan(events, settings);

		Assert.Equal(3, plan.Count, "Expected three actions");
		Assert.Equal(0, plan[0].DelayBeforeMilliseconds, "First action should not wait");
		Assert.Equal(200, plan[1].DelayBeforeMilliseconds, "Second action scales to 200ms");
		Assert.Equal(300, plan[2].DelayBeforeMilliseconds, "Third action scales to 300ms");
	}

	public static void RandomizesOrder()
	{
		var events = new[]
		{
			RecordedKeyEvent.First(Key.A),
			new RecordedKeyEvent(Key.B, TimeSpan.FromMilliseconds(50)),
			new RecordedKeyEvent(Key.C, TimeSpan.FromMilliseconds(75))
		};

		var random = new DeterministicRandomSource(intSequence: new Queue<int>(new[] { 0, 0 }));
		var planner = new KeySequencePlanner(random);
		var settings = new PlaybackSettings
		{
			RandomizeOrder = true,
			MinimumDelayMilliseconds = 0
		};

		var plan = planner.BuildPlan(events, settings);

		// When shuffle draws zero for each swap the final list becomes B, C, A.
		Assert.SequenceEqual(new[] { Key.B, Key.C, Key.A }, plan.ToKeys(), "Expected deterministic shuffled order");
	}

	public static void AppliesVariance()
	{
		var events = new[]
		{
			RecordedKeyEvent.First(Key.A),
			new RecordedKeyEvent(Key.B, TimeSpan.FromMilliseconds(100))
		};

		var random = new DeterministicRandomSource(
			doubleSequence: new Queue<double>(new[] { 0.75, 0.25 }));

		var planner = new KeySequencePlanner(random);
		var settings = new PlaybackSettings
		{
			SpeedMultiplier = 1,
			VarianceMilliseconds = 40,
			EnableVarianceJitter = false,
			MinimumDelayMilliseconds = 0
		};

		var plan = planner.BuildPlan(events, settings);

		// First key uses variance offset (0.75 -> +20ms), second uses 0.25 -> -20ms
		Assert.Equal(20, plan[0].DelayBeforeMilliseconds, "Variance should adjust the first action");
		Assert.Equal(80, plan[1].DelayBeforeMilliseconds, "Variance should adjust the second action");
	}

	public static void EnforcesMinimumDelay()
	{
		var events = new[]
		{
			RecordedKeyEvent.First(Key.A)
		};

	var random = new DeterministicRandomSource(doubleSequence: new Queue<double>(new[] { 0.0 }));
		var planner = new KeySequencePlanner(random);
		var settings = new PlaybackSettings
		{
			MinimumDelayMilliseconds = 15,
			VarianceMilliseconds = 50
		};

		var plan = planner.BuildPlan(events, settings);
		Assert.Equal(15, plan[0].DelayBeforeMilliseconds, "Minimum delay should apply even with negative variance");
	}
}

internal static class Tests_PlaybackService
{
	public static async Task DispatchesKeys()
	{
		var events = new[]
		{
			RecordedKeyEvent.First(Key.A),
			new RecordedKeyEvent(Key.B, TimeSpan.Zero)
		};

		var fakeSender = new RecordingKeySender();
		var planner = new KeySequencePlanner(new DeterministicRandomSource());
		var service = new PlaybackService(fakeSender, planner);

		var settings = new PlaybackSettings
		{
			MinimumDelayMilliseconds = 0
		};

		await service.PlayAsync(events, settings, CancellationToken.None);

		Assert.SequenceEqual(new[] { Key.A, Key.B }, fakeSender.SentKeys, "Playback should dispatch keys in plan order");
	}
}

internal sealed class RecordingKeySender : IKeySender
{
	public List<Key> SentKeys { get; } = new();

	public void SendKeyPress(Key key)
	{
		SentKeys.Add(key);
	}
}

internal sealed class DeterministicRandomSource : IRandomSource
{
	private readonly Queue<double> _doubleSequence;
	private readonly Queue<int> _intSequence;

	public DeterministicRandomSource(Queue<double>? doubleSequence = null, Queue<int>? intSequence = null)
	{
		_doubleSequence = doubleSequence ?? new Queue<double>(new[] { 0.0 });
		_intSequence = intSequence ?? new Queue<int>(new[] { 0 });
	}

	public double NextDouble()
	{
		return _doubleSequence.Count > 0 ? _doubleSequence.Dequeue() : 0.0;
	}

	public int Next(int minInclusive, int maxExclusive)
	{
		if (_intSequence.Count == 0)
		{
			return minInclusive;
		}

		var value = _intSequence.Dequeue();
		if (value < minInclusive || value >= maxExclusive)
		{
			return minInclusive;
		}

		return value;
	}
}

internal static class Assert
{
	public static void Equal<T>(T expected, T actual, string message)
	{
		if (!EqualityComparer<T>.Default.Equals(expected, actual))
		{
			throw new TestFailureException($"{message}. Expected: {expected}, Actual: {actual}");
		}
	}

	public static void SequenceEqual<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, string message)
	{
		if (expected.Count != actual.Count)
		{
			throw new TestFailureException($"{message}. Sequence length mismatch. Expected {expected.Count}, Actual {actual.Count}");
		}

		for (var i = 0; i < expected.Count; i++)
		{
			if (!EqualityComparer<T>.Default.Equals(expected[i], actual[i]))
			{
				throw new TestFailureException($"{message}. First difference at index {i}: Expected {expected[i]}, Actual {actual[i]}");
			}
		}
	}
}

internal sealed class TestRunner
{
	private readonly List<string> _failures = new();
	private int _executed;

	public int Failures => _failures.Count;

	public void Run(string name, Action test)
	{
		try
		{
			test();
			Console.WriteLine($"[PASS] {name}");
		}
		catch (Exception ex)
		{
			_failures.Add($"{name}: {ex.Message}");
			Console.WriteLine($"[FAIL] {name}: {ex.Message}");
		}
		finally
		{
			_executed++;
		}
	}

	public async Task RunAsync(string name, Func<Task> test)
	{
		try
		{
			await test();
			Console.WriteLine($"[PASS] {name}");
		}
		catch (Exception ex)
		{
			_failures.Add($"{name}: {ex.Message}");
			Console.WriteLine($"[FAIL] {name}: {ex.Message}");
		}
		finally
		{
			_executed++;
		}
	}

	public void PrintSummary()
	{
		Console.WriteLine();
		Console.WriteLine($"Executed {_executed} tests. Failures: {_failures.Count}.");
		if (_failures.Count > 0)
		{
			Console.WriteLine("Failure details:");
			foreach (var failure in _failures)
			{
				Console.WriteLine(" - " + failure);
			}
		}
	}
}

internal static class PlanExtensions
{
	public static IReadOnlyList<Key> ToKeys(this IReadOnlyList<KeyPlaybackAction> actions)
	{
		var keys = new List<Key>(actions.Count);
		foreach (var action in actions)
		{
			keys.Add(action.Key);
		}

		return keys;
	}
}

internal sealed class TestFailureException : Exception
{
	public TestFailureException(string message) : base(message)
	{
	}
}
