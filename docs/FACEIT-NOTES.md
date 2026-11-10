# FACEIT notes

Why Match Ready is designed the way it is, what FACEIT's own pages say, and how to check the safety claims yourself.

FACEIT's rules were last checked on **2026-10-19**. FACEIT can change its rules at any time: if anything here disagrees with FACEIT's current support pages, **FACEIT's pages win**.

## 1. What FACEIT's official pages say

| Page | Summary |
|---|---|
| [What is deemed to be a cheat?](https://support.faceit.com/hc/en-us/articles/360015788779-What-is-deemed-to-be-a-cheat) | Banned: cheats and cheat loaders/drivers, external scripts or macros for an advantage, exploits that bypass the anti-cheat, game exploits, debugging or modifying the anti-cheat, **running the anti-cheat in a virtual machine or cloud gaming**, ban evasion. Listed as safe: VibranceGUI, font changes, `.cfg` changes, simple radar, Timer Resolution, Intelligent standby list cleaner. |
| [What is FACEIT Anti-cheat and how does it work?](https://support.faceit.com/hc/en-us/articles/9394666828188-What-is-FACEIT-Anti-cheat-and-how-does-it-work) | A client app, a kernel driver loaded at boot, and a server-side part. Needs Secure Boot and TPM 2.0. Windows 10 and 11 only. |
| [Windows Security Requirements FAQ](https://support.faceit.com/hc/en-us/articles/23117181142556-Windows-Security-Requirements-FAQ) | Secure Boot and TPM 2.0 required; IOMMU plus VBS enforced in waves. From 14 Oct 2026, Windows 10 needs 22H2 with Extended Security Updates; Windows 11 is required from Oct 2027. |
| [Anticheat Compatibility with Windows Insider builds](https://support.faceit.com/hc/en-us/articles/360015781019-Anticheat-Compatibility-with-Windows-Insider-builds) | The anti-cheat is not supported on Windows Insider (preview) builds and can crash or blue-screen there. |
| [Performance issues with AMD CPUs](https://support.faceit.com/hc/en-us/articles/14605964372124-Performance-issues-with-AMD-CPUs) | Some AMD CPUs run badly with the anti-cheat because HPET is forced on. FACEIT's fix: `bcdedit /set useplatformclock false` as admin, then reboot. |

Secondhand reports (weaker evidence): the anti-cheat blocks **drivers with known security vulnerabilities**, often installed by hardware monitoring or RGB tools ([ASUS ROG forum](https://rog-forum.asus.com:443/t5/asus-software/can-t-open-asio-sys-ai-suit-3-vi-gene/td-p/865975), [EVGA forum](https://forums.evga.com/EVGA-Update-all-of-your-programs-because-they-have-vulnerability-FIXED-with-PX1-v-107-m3050791.aspx)).

## 2. Design decisions that follow from this

- **Nothing touches the game or the anti-cheat.** No reading or writing game memory or files, no priority or affinity changes, no injection, no overlays, no macros. CS2 is only seen in the process list.
- **Only ordinary Windows actions:** closing other apps, pausing (never disabling) allow-listed Windows services, switching the power plan, and two per-user settings (Game Mode, optionally notifications). All of it is recorded first and undone on restore.
- **No drivers and no third-party "optimizer" libraries.** Only .NET and documented Windows APIs.
- **No changes to Windows security** (Defender, firewall, Secure Boot, TPM, VBS, UAC).
- **The HPET fix stays manual.** It changes the boot configuration, which is permanent, so the app only links to FACEIT's article in the Pro tab.
- **Fullscreen optimizations stay manual.** FACEIT has no guidance on it. It's a per-user Windows compatibility flag, not a game-file change, but setting it would mean acting on the game's exe, so the app doesn't.
- **No RAM "cleaning".** FACEIT lists standby-list cleaners as safe, but they reach into other processes' memory. The app only reports how much RAM closing apps freed.
- **Testing in a VM:** FACEIT bans running its anti-cheat in a virtual machine. Test Match Ready in a VM only if the VM has **no** FACEIT anti-cheat installed.

## 3. Software the app warns about

| Software | What's known | What Match Ready does |
|---|---|---|
| RivaTuner Statistics Server (RTSS), MSI Afterburner | No official FACEIT statement. RTSS hooks an overlay into games, which anti-cheats tend to dislike. | **Warns only.** Never closes it: closing a hook while the game runs can crash the game. |
| AMD Ryzen Master | No FACEIT statement. It loads a kernel driver; an older version had a known driver vulnerability (CVE-2020-12928). | **Warns.** Keep it updated, or close it before queueing. |
| OBS Studio | Widely used by FACEIT streamers. | Asks before closing it, or never closes it if you said you record. |
| Mouse, keyboard, headset, lighting software | No FACEIT statements found for iCUE, Synapse, G HUB, SteelSeries GG, NZXT CAM. | Kept running unless you choose otherwise in Quick setup. |

## 4. Windows versions

FACEIT's warning is about **Insider (Dev/Canary/Beta) preview builds**. Match Ready shows a yellow note if it detects one (from the Windows Insider enrollment settings). A new build number alone is not a sign of an Insider build: for example, Windows 11 26H2 (build 26300) is delivered as an enablement package on the same base as 24H2 and 25H2.

## 5. Proof that the app never opens a handle to the game

**How the code guarantees it**
- CS2 is detected with one system-wide process snapshot (`Process.GetProcesses()`, which uses `NtQuerySystemInformation`). It opens **no** handle to any process. Only the name, PID and session ID are used.
- Every call that opens a process handle (reading an exe path, `WM_CLOSE`, `TerminateProcess`) first calls `Guard.EnsureProcessAllowed`. `cs2`, `csgo`, `steam*`, `faceit*`, `discord*` and the rest of [`ProtectedList.cs`](../src/MatchReady/Safety/ProtectedList.cs) are refused before any Windows call.
- Services are listed through the Service Control Manager. A handle to a single service is only opened for allow-listed services, after `Guard.EnsureServiceAllowed`.

**Automated tests (run on every build)**
- `NoGameHandleTests`: starts a stand-in game (Windows' `PING.EXE` renamed `cs2.exe`), runs auto-mode detection, a full dry-run boost and a state snapshot, then checks every handle the test process owns. Result: **0 handles to cs2.exe**. A positive control proves the scan finds a handle when one is really open.
- `SafetyTests`: the real close, kill and path calls refuse `cs2` and 50+ other protected names, and protected names typed into `config.json` are ignored.

## 6. Check it yourself

- **Self-check:** Help → **Run self-check** (or `MatchReady.exe --self-check` from an admin terminal). It boosts services and settings without closing apps, restores, and proves everything is exactly as before. A report is saved to `%LOCALAPPDATA%\MatchReady\self-check.txt`.
- **Handle check:** run [`tools/Check-Handles.ps1`](../tools/Check-Handles.ps1) while CS2 and a boost are running. It uses Microsoft's Sysinternals Handle tool and lists any handle Match Ready holds to CS2, Steam or FACEIT. The expected result is none.
- **Network:** Match Ready makes **no network connections**, unless you click "Check for updates" or turn on the daily update check. That only asks GitHub for the newest version number. You can confirm with `netstat -bno` during a boost.
