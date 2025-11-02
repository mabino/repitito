# KeyPlaybackSuite Copilot Instructions

- Favor C# with WPF (`net8.0-windows`) for UI changes. Keep business logic inside `KeyPlaybackApp/Core` and platform specific interactions under `KeyPlaybackApp/Services` so it stays testable.
- Recording logic lives in `MainWindow.xaml.cs`; prefer extending functionality by extracting helpers into new classes instead of growing the code-behind when additions exceed a few dozen lines.
- Always keep keystroke replay logic deterministic and unit-testable via `KeySequencePlanner`. Inject randomness through `IRandomSource` implementations to keep tests reliable.
- Update the custom console test runner in `KeyPlaybackApp.Tests/Program.cs` with new assertions when features change. Ensure it exits with a non-zero code on failure.
- Use the provided `PlaybackSettings` validation patterns when adding new configuration options (validate inputs early, throw descriptive exceptions).
- Native key simulation uses `SendInput` in `NativeKeySender`. If you adjust low-level interop, wrap new P/Invoke signatures in dedicated types and add comments describing required privileges.
