using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Repitito.Core;
using Repitito.Services;
using System.Windows.Input;

namespace Repitito.Tests;

internal static class Program
{
	private static int Main()
	{
		var runner = new TestRunner();

		runner.Run("KeySequencePlanner.BasicScaling", Tests_KeySequencePlanner.BasicScaling);
		runner.Run("KeySequencePlanner.RandomizesOrder", Tests_KeySequencePlanner.RandomizesOrder);
		runner.Run("KeySequencePlanner.AppliesVariance", Tests_KeySequencePlanner.AppliesVariance);
		runner.Run("KeySequencePlanner.EnforcesMinimumDelay", Tests_KeySequencePlanner.EnforcesMinimumDelay);
		runner.Run("KeySequencePlanner.PreservesRecordedCharacters", Tests_KeySequencePlanner.PreservesRecordedCharacters);
		runner.RunAsync("PlaybackService.DispatchesKeys", Tests_PlaybackService.DispatchesKeys).GetAwaiter().GetResult();
		runner.Run("PlaybackHotKeyController.StartsPlaybackWhenIdle", Tests_PlaybackHotKeyController.StartsPlaybackWhenIdle);
		runner.Run("PlaybackHotKeyController.CancelsWhenAlreadyPlaying", Tests_PlaybackHotKeyController.CancelsWhenAlreadyPlaying);
		runner.Run("PlaybackHotKeyController.IgnoresWhenEmpty", Tests_PlaybackHotKeyController.IgnoresWhenEmpty);
		runner.Run("NativeKeySender.ThrowsWhenScanCodeMissing", Tests_NativeKeySender.ThrowsWhenScanCodeMissing);
		runner.Run("NativeKeySender.SendsDownThenUpWithExtendedFlag", Tests_NativeKeySender.SendsDownThenUpWithExtendedFlag);
		runner.Run("NativeKeySender.BubblesSendFailures", Tests_NativeKeySender.BubblesSendFailures);
		runner.Run("NativeKeySender.FallsBackToVirtualKeyOnInvalidParameter", Tests_NativeKeySender.FallsBackToVirtualKeyOnInvalidParameter);
		runner.Run("NativeKeySender.FallsBackToUnicodeWhenVirtualKeyFails", Tests_NativeKeySender.FallsBackToUnicodeWhenVirtualKeyFails);
		runner.Run("NativeKeySender.UsesUnicodeWhenAvailable", Tests_NativeKeySender.UsesUnicodeWhenAvailable);
		runner.Run("NativeKeySender.PrefersRecordedCharacter", Tests_NativeKeySender.PrefersRecordedCharacter);
		runner.Run("NativeKeySender.InputStructLayoutMatches", Tests_NativeKeySender.InputStructLayoutMatches);

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

	public static void PreservesRecordedCharacters()
	{
		var events = new[]
		{
			RecordedKeyEvent.First(Key.A, 'a'),
			new RecordedKeyEvent(Key.B, TimeSpan.FromMilliseconds(120), 'B')
		};

		var planner = new KeySequencePlanner(new DeterministicRandomSource());
		var settings = new PlaybackSettings
		{
			MinimumDelayMilliseconds = 0
		};

		var plan = planner.BuildPlan(events, settings);
		Assert.Equal('a', plan[0].Character, "Planner should keep lowercase character data.");
		Assert.Equal('B', plan[1].Character, "Planner should keep uppercase character data.");
		Assert.SequenceEqual(new[] { Key.A, Key.B }, plan.ToKeys(), "Planner must keep key ordering.");
	}
}

internal static class Tests_PlaybackService
{
	public static async Task DispatchesKeys()
	{
		var events = new[]
		{
			RecordedKeyEvent.First(Key.A, 'a'),
			new RecordedKeyEvent(Key.B, TimeSpan.Zero, 'b')
		};

		var fakeSender = new RecordingKeySender();
		var planner = new KeySequencePlanner(new DeterministicRandomSource());
		var service = new PlaybackService(fakeSender, planner);

		var settings = new PlaybackSettings
		{
			MinimumDelayMilliseconds = 0
		};

		await service.PlayAsync(events, settings, CancellationToken.None);

		var sent = fakeSender.SentKeys;
		Assert.SequenceEqual(new[] { Key.A, Key.B }, sent.Select(s => s.Key).ToList(), "Playback should dispatch keys in plan order");
		Assert.SequenceEqual(new char?[] { 'a', 'b' }, sent.Select(s => s.Character).ToList(), "Playback should pass along recorded characters");
	}
}

internal static class Tests_PlaybackHotKeyController
{
	public static void StartsPlaybackWhenIdle()
	{
		var startInvoked = false;
		var controller = new PlaybackHotKeyController(
			isRecording: () => false,
			isPlaying: () => false,
			recordedCount: () => 2,
			stopRecording: () => Assert.Fail("Stop should not be called when idle."),
			startPlayback: () => startInvoked = true,
			cancelPlayback: () => Assert.Fail("Cancel should not be called when idle."));

		var result = controller.HandleHotKey();
		Assert.True(startInvoked, "Hotkey should kick off playback when idle.");
		Assert.Equal(HotKeyResult.StartedPlayback, result, "Expected StartedPlayback result.");
	}

	public static void CancelsWhenAlreadyPlaying()
	{
		var cancelCount = 0;
		var controller = new PlaybackHotKeyController(
			isRecording: () => false,
			isPlaying: () => true,
			recordedCount: () => 2,
			stopRecording: () => Assert.Fail("Stop should not be called when already playing."),
			startPlayback: () => Assert.Fail("Should not start playback when already playing."),
			cancelPlayback: () => cancelCount++);

		var result = controller.HandleHotKey();
		Assert.Equal(1, cancelCount, "Cancel should fire exactly once.");
		Assert.Equal(HotKeyResult.CancelledPlayback, result, "Expected CancelledPlayback result.");
	}

	public static void IgnoresWhenEmpty()
	{
		var controller = new PlaybackHotKeyController(
			isRecording: () => false,
			isPlaying: () => false,
			recordedCount: () => 0,
			stopRecording: () => Assert.Fail("Stop should not be called when nothing is recorded."),
			startPlayback: () => Assert.Fail("Start should not be called when nothing is recorded."),
			cancelPlayback: () => Assert.Fail("Cancel should not be called when nothing is recorded."));

		var result = controller.HandleHotKey();
		Assert.Equal(HotKeyResult.NoRecordingAvailable, result, "Expected NoRecordingAvailable result.");
	}
}

internal static class Tests_NativeKeySender
{
	public static void ThrowsWhenScanCodeMissing()
	{
		var fake = new FakeNativeKeyboard(mapResponses: new Queue<uint>(new[] { 0u }))
		{
			VirtualKeyExceptionFactory = () => new InvalidOperationException("SendInput VK fallback failed with error code 87 (VK=65)")
		};
		var sender = new NativeKeySender(fake);
		Assert.Throws<InvalidOperationException>(() => sender.SendKeyPress(Key.A), "map virtual key");
	}

	public static void SendsDownThenUpWithExtendedFlag()
	{
		var fake = new FakeNativeKeyboard(mapResponses: new Queue<uint>(new[] { 0x11Du }));
		var sender = new NativeKeySender(fake);
		sender.SendKeyPress(Key.RightCtrl);

		Assert.Equal(2, fake.ScanEvents.Count, "Expected key down and key up events.");
		Assert.True(!fake.ScanEvents[0].IsKeyUp, "First event should be key down.");
		Assert.True(fake.ScanEvents[1].IsKeyUp, "Second event should be key up.");
		Assert.Equal(fake.ScanEvents[0].ScanCode, fake.ScanEvents[1].ScanCode, "Scan codes should match between down and up.");
		Assert.True(fake.ScanEvents[0].IsExtended, "Extended flag should be set for RightCtrl.");
	}

	public static void BubblesSendFailures()
	{
		var fake = new FakeNativeKeyboard(mapResponses: new Queue<uint>(new[] { 0x1Eu }))
		{
			ScanExceptionFactory = () => new InvalidOperationException("Send failure")
		};
		var sender = new NativeKeySender(fake);
		Assert.Throws<InvalidOperationException>(() => sender.SendKeyPress(Key.A), "Send failure");
	}

	public static void FallsBackToVirtualKeyOnInvalidParameter()
	{
		var fake = new FakeNativeKeyboard(mapResponses: new Queue<uint>(new[] { 0x23u }))
		{
			ScanExceptionFactory = () => new InvalidOperationException("SendInput failed with error code 87 (VK=0)")
		};

		var sender = new NativeKeySender(fake);
		sender.SendKeyPress(Key.H);

		Assert.Equal(2, fake.VirtualKeyEvents.Count, "Expected fallback to send VK events.");
		Assert.Equal((ushort)KeyInterop.VirtualKeyFromKey(Key.H), fake.VirtualKeyEvents[0].VirtualKey, "Fallback should reuse original VK.");
		Assert.True(!fake.VirtualKeyEvents[0].IsKeyUp, "First VK event should be key down.");
		Assert.True(fake.VirtualKeyEvents[1].IsKeyUp, "Second VK event should be key up.");
	}

	public static void FallsBackToUnicodeWhenVirtualKeyFails()
	{
		var unicodeFailed = false;
		var fake = new FakeNativeKeyboard(
			mapResponses: new Queue<uint>(new[] { 0x39u }),
			charResponses: new Queue<uint>(new[] { 0x20u }))
		{
			UnicodeExceptionFactory = () =>
			{
				if (!unicodeFailed)
				{
					unicodeFailed = true;
					return new InvalidOperationException("SendInput Unicode fallback failed with error code 87 (Char=32)");
				}

				return null;
			},
			ScanExceptionFactory = () => new InvalidOperationException("SendInput failed with error code 87 (VK=32)"),
			VirtualKeyExceptionFactory = () => new InvalidOperationException("SendInput VK fallback failed with error code 87 (VK=32)")
		};

		var sender = new NativeKeySender(fake);
		sender.SendKeyPress(Key.Space);

		Assert.True(unicodeFailed, "Unicode path should be attempted and fail once before succeeding.");
		Assert.Equal(2, fake.UnicodeEvents.Count, "Final unicode retry should record two events.");
		Assert.True(fake.UnicodeEvents[0].Character == ' ' && !fake.UnicodeEvents[0].IsKeyUp, "Unicode down event should be first.");
		Assert.True(fake.UnicodeEvents[1].IsKeyUp, "Unicode up event should follow the down event.");
	}

	public static void UsesUnicodeWhenAvailable()
	{
		var fake = new FakeNativeKeyboard(
			mapResponses: new Queue<uint>(new[] { 0x1Eu }),
			charResponses: new Queue<uint>(new[] { 0x61u }));

		var sender = new NativeKeySender(fake);
		sender.SendKeyPress(Key.A);

		Assert.Equal(2, fake.UnicodeEvents.Count, "Expected unicode path to execute for character keys.");
		Assert.Equal('a', fake.UnicodeEvents[0].Character, "Unicode event should use lowercase character mapping when available.");
		Assert.Equal(0, fake.ScanEvents.Count, "Unicode path should avoid scan code events when it succeeds.");
		Assert.Equal(0, fake.VirtualKeyEvents.Count, "Unicode success should skip VK fallback.");
	}

	public static void PrefersRecordedCharacter()
	{
		var fake = new FakeNativeKeyboard(
			mapResponses: new Queue<uint>(new[] { 0x1Eu }),
			charResponses: new Queue<uint>(new[] { 0x41u }));

		var sender = new NativeKeySender(fake);
		sender.SendKeyPress(Key.A, 'a');

		Assert.Equal(2, fake.UnicodeEvents.Count, "Recorded character path should produce key down and up events.");
		Assert.Equal('a', fake.UnicodeEvents[0].Character, "Recorded character should be used before fallback mappings.");
		Assert.Equal('a', fake.UnicodeEvents[1].Character, "Recorded character should be used for key release as well.");
		Assert.Equal(0, fake.ScanEvents.Count, "Recorded character success should skip scan codes.");
		Assert.Equal(0, fake.VirtualKeyEvents.Count, "Recorded character success should skip VK fallback.");
	}

	public static void InputStructLayoutMatches()
	{
		var senderType = typeof(NativeKeySender);
		var inputType = senderType.GetNestedType("INPUT", BindingFlags.NonPublic);
		Assert.True(inputType is not null, "INPUT struct must exist on NativeKeySender.");

		var size = Marshal.SizeOf(inputType!);
		Assert.Equal(NativeKeySender.InputStructSizeForTests, size, "InputStructSize constant should reflect actual INPUT size.");
		Assert.True(size >= 40, "INPUT struct must be at least 40 bytes (Win32 x64 expectation).");

		const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		var typeField = inputType!.GetField("type", Flags);
		Assert.True(typeField is not null, "INPUT.type field missing.");
		Assert.Equal(typeof(uint), typeField!.FieldType, "INPUT.type must be a UInt32 to match Win32.");

		var unionField = inputType.GetField("U", Flags);
		Assert.True(unionField is not null, "INPUT union field U missing.");
		var unionType = unionField!.FieldType;
		Assert.True(unionType.GetField("ki", Flags) is not null, "Union must expose KEYBDINPUT.");
		Assert.True(unionType.GetField("mi", Flags) is not null, "Union must expose MOUSEINPUT.");
		Assert.True(unionType.GetField("hi", Flags) is not null, "Union must expose HARDWAREINPUT.");

		var keyboardType = senderType.GetNestedType("KEYBDINPUT", BindingFlags.NonPublic);
		Assert.True(keyboardType is not null, "KEYBDINPUT struct missing.");
		var extraInfoField = keyboardType!.GetField("dwExtraInfo", Flags);
		Assert.True(extraInfoField is not null, "KEYBDINPUT.dwExtraInfo missing.");
		Assert.Equal(typeof(IntPtr), extraInfoField!.FieldType, "dwExtraInfo must be IntPtr for pointer-sized field.");

		var hardwareType = senderType.GetNestedType("HARDWAREINPUT", BindingFlags.NonPublic);
		Assert.True(hardwareType is not null, "HARDWAREINPUT struct missing.");
		var mouseType = senderType.GetNestedType("MOUSEINPUT", BindingFlags.NonPublic);
		Assert.True(mouseType is not null, "MOUSEINPUT struct missing.");

		Assert.True(Marshal.SizeOf(keyboardType) >= 16, "KEYBDINPUT should be at least 16 bytes (pointer alignment).");
	}
}

internal sealed class FakeNativeKeyboard : NativeKeySender.INativeKeyboard
{
	private readonly Queue<uint> _scanResponses;
	private readonly Queue<uint> _charResponses;

	public FakeNativeKeyboard(Queue<uint> mapResponses, Queue<uint>? charResponses = null)
	{
		_scanResponses = mapResponses;
		_charResponses = charResponses ?? new Queue<uint>();
	}

	public Func<InvalidOperationException?>? ScanExceptionFactory { get; set; }
	public Func<InvalidOperationException?>? VirtualKeyExceptionFactory { get; set; }
	public Func<InvalidOperationException?>? UnicodeExceptionFactory { get; set; }

	public List<NativeEvent> ScanEvents { get; } = new();
	public List<VirtualKeyEvent> VirtualKeyEvents { get; } = new();
	public List<UnicodeEvent> UnicodeEvents { get; } = new();

	public uint MapVirtualKey(uint virtualKey)
	{
		return _scanResponses.Count > 0 ? _scanResponses.Dequeue() : 0;
	}

	public uint MapVirtualKeyToChar(uint virtualKey)
	{
		return _charResponses.Count > 0 ? _charResponses.Dequeue() : 0;
	}

	public void SendScanCodeEvent(ushort scanCode, bool isKeyUp, bool isExtended, ushort originalVirtualKey)
	{
		ScanEvents.Add(new NativeEvent(scanCode, isKeyUp, isExtended, originalVirtualKey));
		if (ScanExceptionFactory is not null)
		{
			var exception = ScanExceptionFactory();
			if (exception is not null)
			{
				throw exception;
			}
		}
	}

	public void SendVirtualKeyEvent(ushort virtualKey, bool isKeyUp)
	{
		if (VirtualKeyExceptionFactory is not null)
		{
			var exception = VirtualKeyExceptionFactory();
			if (exception is not null)
			{
				throw exception;
			}
		}

		VirtualKeyEvents.Add(new VirtualKeyEvent(virtualKey, isKeyUp));
	}

	public void SendUnicodeEvent(char character, bool isKeyUp)
	{
		if (UnicodeExceptionFactory is not null)
		{
			var exception = UnicodeExceptionFactory();
			if (exception is not null)
			{
				throw exception;
			}
		}

		UnicodeEvents.Add(new UnicodeEvent(character, isKeyUp));
	}
}

internal readonly record struct NativeEvent(ushort ScanCode, bool IsKeyUp, bool IsExtended, ushort OriginalVirtualKey);
internal readonly record struct VirtualKeyEvent(ushort VirtualKey, bool IsKeyUp);
internal readonly record struct UnicodeEvent(char Character, bool IsKeyUp);

internal sealed class RecordingKeySender : IKeySender
{
	public List<(Key Key, char? Character)> SentKeys { get; } = new();

	public void SendKeyPress(Key key, char? recordedCharacter = null)
	{
		SentKeys.Add((key, recordedCharacter));
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

	public static void True(bool condition, string message)
	{
		if (!condition)
		{
			throw new TestFailureException(message);
		}
	}

	public static void Fail(string message)
	{
		throw new TestFailureException(message);
	}

	public static void Throws<TException>(Action action, string messageContains) where TException : Exception
	{
		try
		{
			action();
			throw new TestFailureException($"Expected exception of type {typeof(TException).Name} but no exception was thrown.");
		}
		catch (TException ex)
		{
			if (!string.IsNullOrEmpty(messageContains) && !ex.Message.Contains(messageContains, StringComparison.OrdinalIgnoreCase))
			{
				throw new TestFailureException($"Expected exception message to contain '{messageContains}', but was '{ex.Message}'.");
			}
		}
		catch (Exception ex)
		{
			throw new TestFailureException($"Expected exception of type {typeof(TException).Name} but received {ex.GetType().Name}: {ex.Message}");
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
