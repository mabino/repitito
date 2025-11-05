---
layout: default
---

# Repitito

🎹 Repitito is a Windows desktop application that captures keyboard input and replays it on demand.

## Features {#features}

- 🎬 **One-click record & replay** – Capture timings, characters, and modifier combos, then play them back perfectly.
- 🎛️ **Inline editing galore** – Fix keys, tweak delays, or jot a comment without leaving the table.
- 🎲 **Playful randomness** – Speed, variance, and jitter controls keep your macros feeling human.
- 🧲 **Global hotkey** – Tap <kbd>F8</kbd> from anywhere to start or stop playback.
- 🧷 **Import & export** – Share recordings as tidy JSON files with schema validation.
- 🛡️ **Thorough tests** – A custom test harness keeps the SendInput wizardry reliable.

## Getting Started {#getting-started}

1. Install the **.NET 9 Desktop Runtime** (if you do not already have it).
2. Grab the latest build:
   - Run `pwsh -File scripts/package.ps1 -Configuration Release -Runtime win-x64`
   - Or download a prebuilt release (coming soon 🌈)
3. Launch the bundled `Repitito.exe` and start recording keyboard magic.
4. Double-click any delay, key, or comment cell to fine-tune your sequence.

> Tip: Use the **Delete Row** button or drag handles to curate your playlist of key events.

<div class="footer-note">Made with AFK in mind.</div>
