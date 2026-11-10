# Contributing

Thanks for helping. The one rule that outranks everything: **Match Ready must never interact with the game or an anti-cheat.** If a change could look like tampering to FACEIT, leave it out.

## Build and test

```powershell
dotnet test MatchReady.sln
powershell -ExecutionPolicy Bypass -File build\Build-Setup.ps1   # tests + installer in dist\
```

Screenshots of every window (no admin, nothing changed): set `MR_SCREENSHOTS` to a folder and run the tests.

## Rules for changes

- **Never** open a handle to, read, write, inject into, or change the priority of `cs2`, Steam, FACEIT or any anti-cheat process or service. CS2 is detected from the process list only.
- New apps or services to close go into `Config/Defaults.cs`, never into code paths that bypass `Safety/Guard.cs`. Run the tests: `SafetyTests` fails if a default entry is on the protected list.
- Every change to Windows must be journaled in `SessionJournal` **before** it happens and undone by `RestoreEngine`. Add a round-trip test in `EngineTests`.
- No drivers, no unsigned third-party binaries, no packages beyond .NET and `System.ServiceProcess.ServiceController`.
- No network code except `Sys/UpdateChecker.cs`, which stays opt-in.
- No scheduled tasks, registry Run keys or hidden background processes (antivirus treats them as malware persistence).

## Text and translations

The app is English-only for now. Long texts live in `src/MatchReady/UI/Texts.cs`; keep new user-facing text short and plain, so translations can be added later.
