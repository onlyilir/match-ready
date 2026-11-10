# Changelog

## 1.2.0 (2026-11-10): first public release, now called Match Ready

**Renamed** from CS2 Booster to Match Ready. Setup moves an existing CS2 Booster install over automatically (settings kept, old app removed), and the app brings over its log and any unfinished boost.

New
- Quick setup detects the PC (CPU, RAM, graphics card, drive type) and shows only the mouse/keyboard/headset/fan software that is actually installed.
- Full and Light profiles. Light only closes heavy apps and pauses no services.
- Self-check (Help, or `--self-check`): proves restore puts every service and setting back.
- "Freed X GB of RAM" after each boost.
- History of the last 10 boosts in "What was stopped".
- Add any running app to the close list; search box in Settings.
- Optional "Start with Windows" (opens in the tray).
- Optional update check against GitHub releases (off by default, never while CS2 runs).
- After CS2 closes, a 30-second wait before restoring, in case CS2 is only restarting.
- One short "restored" note in the tray after the game has closed. Never during play.
- Warnings (RTSS, Ryzen Master…) shown in their own card; last result in the tray menu; keyboard focus outlines and screen-reader names.
- `tools/Check-Handles.ps1`: proves with Sysinternals Handle that no handle to CS2/Steam/FACEIT is held.

Changed
- Device and lighting software is kept running by default for everyone; Quick setup decides.
- Notifications are no longer paused by default, so FACEIT "match found" alerts come through.
- The log is capped at 1 MB (one older part kept). A damaged settings file is kept as `config.json.bak` and defaults are used.

## 1.1.0 (2026-10-19)
- Quick setup, tray mode (green/gray icon), "Keep running next time" buttons, grouped Settings, Help with Copy log, dark title bar.

## 1.0.1 (2026-10-19)
- Closed apps stay closed after restore (no surprise windows). Apps that need admin are never reopened.

## 1.0.0 (2026-10-19)
- First version: one-button boost and restore, Auto mode, crash-safe journal, Pro tab, installer.
