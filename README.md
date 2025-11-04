# Repitito

Repitito is a Windows desktop application that captures keyboard input and replays it on demand. It is built with WPF on .NET 9 and comes with a lightweight test harness to guard the SendInput interop layer and the playback planner.

## Features

- **Record & Playback** – Capture keystrokes with timing metadata and replay them deterministically.
- **Global Hotkey** – Use `F8` anywhere to toggle playback, even when the main window is not focused.
- **Lowercase Preservation** – Recorded characters keep their original glyphs (including lowercase) through playback.
- **Loop Playback** – Continuously repeat recorded sequences (enabled by default) with a single checkbox toggle.
- **Playback Controls** – Configure randomization, minimum delay, speed multiplier, and variance jitter.
- **Diagnostics & Tests** – Native SendInput wrappers include guarded fallbacks with extensive unit tests.

## Getting Started

### Prerequisites

- Windows 10 or later
- .NET 9 SDK

### Building

```pwsh
# Restore and build the solution
 dotnet build KeyPlaybackSuite.sln
```

### Running the App

```pwsh
# Launch the WPF application
 dotnet run --project KeyPlaybackApp/KeyPlaybackApp.csproj
```

### Running Tests

The solution includes a custom console-based test runner. Execute it via:

```pwsh
# Run unit tests
 dotnet run --project KeyPlaybackApp.Tests/KeyPlaybackApp.Tests.csproj
```

## Project Structure

- `KeyPlaybackApp/` – WPF application (uses namespaces under `Repitito.*`).
  - `Core/` – Playback planner, randomization interfaces, and domain records.
  - `Services/` – Native keyboard interop, global hotkey management, playback services.
  - `MainWindow.xaml` – UI layer for recording and playback controls.
- `KeyPlaybackApp.Tests/` – Console test harness covering planner logic and interop fallbacks.
- `KeyPlaybackSuite.sln` – Solution file referencing the app and test projects.

## Hotkey Reference

- `F8` – Start playback if idle, stop playback if running, or dismiss recording mode.

## Contributing

1. Fork the repository and create a feature branch.
2. Run `dotnet run --project KeyPlaybackApp.Tests/KeyPlaybackApp.Tests.csproj` to ensure tests pass.
3. Submit a pull request describing your changes.
