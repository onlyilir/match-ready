# Security

Match Ready runs as administrator, because pausing Windows services needs it. Admin rights are only ever used for the actions below, and every one of them goes through a safety check first.

## What it does with admin rights

| Action | Limit |
|---|---|
| Close apps | Only process names on the allow-list in `config.json`, only in your own Windows session, never from `C:\Windows\System32` or `SysWOW64`. A polite close first; force-close only if the app ignores it for 3 seconds. |
| Pause services | Only allow-listed services, and only **stopped**, never disabled or reconfigured. A service with running dependents is skipped. |
| Power plan | Switch with `powercfg` and switch back. A temporary copy of High performance is deleted on restore. |
| Settings | Two per-user values (Game Mode, optionally notifications), recorded before the change and put back on restore. |

## The protected list

[`src/MatchReady/Safety/ProtectedList.cs`](src/MatchReady/Safety/ProtectedList.cs) is hard-coded and can't be overridden by `config.json`. It covers the game, Steam, FACEIT and other anti-cheats, Discord, core Windows processes, Windows Security and Norton, GPU/audio/chipset drivers, peripheral core services and game overlays. Every call that closes, kills, stops or starts something goes through `Guard`, which throws **before** any Windows API is called. Tests run the real kill/stop code against 50+ protected names and fail the build if any gets through.

## Network

None, except the optional update check (a button in Help, or a daily check you can turn on). It sends one request to `api.github.com` for the newest release of this repository, with a generic user agent and no personal data. Nothing is downloaded or installed automatically.

## Crash safety

Every change is written to `%LOCALAPPDATA%\MatchReady\session.json` **before** it is made. If the app is killed mid-boost, it puts everything back on its next start. A reboot also undoes every paused service.

## Reporting a problem

Please use GitHub's **private vulnerability reporting** (Security tab → Report a vulnerability), or open an issue if it isn't sensitive. Include the output of **Help → Copy log**.
