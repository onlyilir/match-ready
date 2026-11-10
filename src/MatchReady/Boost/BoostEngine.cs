using System.Diagnostics;
using MatchReady.Config;
using MatchReady.Restore;
using MatchReady.Safety;
using MatchReady.Sys;

namespace MatchReady.Boost;

public sealed class BoostOptions
{
    public bool DryRun { get; init; }
    public bool CloseBrowsers { get; init; }
    public bool CloseRecorder { get; init; }
    public bool Auto { get; init; }
    public string JournalPath { get; init; } = AppPaths.SessionFile;
    /// <summary>How long apps get to close on their own before they are force-closed.</summary>
    public TimeSpan GracePeriod { get; init; } = TimeSpan.FromSeconds(3);
    /// <summary>Self-check: test services and settings without closing any apps.</summary>
    public bool SkipApps { get; init; }
}

public static class RegistryTargets
{
    public const string GameBarKey = @"Software\Microsoft\GameBar";
    public const string GameModeValue = "AutoGameModeEnabled";
    public const string PushKey = @"Software\Microsoft\Windows\CurrentVersion\PushNotifications";
    public const string ToastValue = "ToastEnabled";
}

public static class PowerPlans
{
    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid UltimatePerformance = new("e9a42b02-d5df-448d-aa00-03f14749eb61");
}

/// <summary>
/// Runs the boost: snapshot, close allow-listed apps, pause allow-listed services,
/// power plan, notifications, Game Mode. Every change is journaled before it happens.
/// </summary>
public sealed class BoostEngine
{
    readonly ISystemOps _os;
    readonly AppConfig _cfg;
    readonly Action<string> _report;
    readonly int _mySession;

    public BoostEngine(ISystemOps os, AppConfig cfg, Action<string>? report = null, int? sessionId = null)
    {
        _os = os;
        _cfg = cfg;
        _report = report ?? (_ => { });
        _mySession = sessionId ?? Process.GetCurrentProcess().SessionId;
    }

    void Say(string msg)
    {
        Log.Write(msg);
        _report(msg);
    }

    public static bool IsGameRunning(IEnumerable<ProcInfo> procs) =>
        procs.Any(p => string.Equals(p.Name, "cs2", StringComparison.OrdinalIgnoreCase));

    /// <summary>Software FACEIT may dislike. We only warn; we never close these.</summary>
    public static List<string> Warnings(IEnumerable<ProcInfo> procs)
    {
        var names = new HashSet<string>(procs.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        var w = new List<string>();
        if (names.Contains("RTSS"))
            w.Add("RivaTuner (RTSS) is running. It hooks an overlay into games, which anti-cheats may flag. Consider closing it yourself before you queue.");
        if (names.Contains("MSIAfterburner"))
            w.Add("MSI Afterburner is running. Its overlay uses RTSS. Consider closing it before you queue.");
        if (names.Contains("AMD Ryzen Master"))
            w.Add("AMD Ryzen Master is open. It loads a kernel driver. Keep it updated, and close it before you queue.");
        return w;
    }

    /// <summary>Which running apps the boost would close, and which it must skip.</summary>
    public List<(AppEntry entry, List<ProcInfo> procs)> FindApps(IReadOnlyList<ProcInfo> procs, BoostOptions opt, List<string> skipped)
    {
        var result = new List<(AppEntry, List<ProcInfo>)>();
        var light = _cfg.Profile == BoostProfile.Light;
        foreach (var app in _cfg.Apps.Where(a => a.Enabled && (!light || a.Group == "Heavy apps")))
        {
            if (app.Kind == AppKind.Browser && !opt.CloseBrowsers) continue;
            if (app.Kind == AppKind.Recorder && !opt.CloseRecorder) continue;

            var matches = new List<ProcInfo>();
            foreach (var p in procs)
            {
                if (!app.Processes.Any(n => string.Equals(n, p.Name, StringComparison.OrdinalIgnoreCase))) continue;
                if (p.SessionId != _mySession) continue; // only this user's apps, never background service processes
                if (ProtectedList.IsProtectedProcess(p.Name))
                {
                    skipped.Add($"{p.Name}: protected, never touched");
                    continue;
                }
                if (app.PathContains != null)
                {
                    var path = SafePath(p);
                    if (path == null || !path.Contains(app.PathContains, StringComparison.OrdinalIgnoreCase)) continue;
                }
                matches.Add(p);
            }
            if (matches.Count > 0) result.Add((app, matches));
        }
        return result;
    }

    string? SafePath(ProcInfo p)
    {
        try { return _os.GetProcessPath(p); }
        catch (ProtectedTargetException) { return null; }
    }

    public SessionJournal Run(BoostOptions opt)
    {
        var dry = opt.DryRun;
        var tag = dry ? "[dry run] Would " : "";
        var j = new SessionJournal { Auto = opt.Auto, FilePath = dry ? null : opt.JournalPath };

        // 1. Snapshot
        Say(dry ? "[dry run] Nothing will be changed." : "Boost started.");
        var procs = _os.ListProcesses();
        if (IsGameRunning(procs)) Say("CS2 is running (seen in the process list only; the game itself is never touched).");
        foreach (var w in Warnings(procs)) Say("Warning: " + w);
        j.Save();

        // 2. Close background apps (Light profile: only the heavy ones). Measure what that frees.
        var ramBefore = _os.AvailableMemoryBytes();
        if (!opt.SkipApps) CloseApps(j, procs, opt, tag);
        if (!dry && j.AppsClosed > 0)
        {
            Thread.Sleep(1000); // Windows needs a moment to hand the memory back
            var ramAfter = _os.AvailableMemoryBytes();
            if (ramBefore > 0 && ramAfter > ramBefore) j.RamFreedBytes = ramAfter - ramBefore;
        }

        // 3. Pause services (not in the Light profile)
        if (_cfg.Profile == BoostProfile.Full) PauseServices(j, tag, dry);

        // 4. Power plan
        if (_cfg.SwitchPowerPlan) SwitchPower(j, tag, dry);

        // 5 + 6. Notifications off, Game Mode on
        if (_cfg.PauseNotifications)
            SetRegistry(j, RegistryTargets.PushKey, RegistryTargets.ToastValue, 0, "Notifications paused", tag, dry);
        if (_cfg.EnsureGameMode)
            SetRegistry(j, RegistryTargets.GameBarKey, RegistryTargets.GameModeValue, 1, "Windows Game Mode on", tag, dry);

        j.State = SessionState.Active;
        j.Save();
        Say(dry
            ? $"[dry run] Done. Would close {j.Apps.Count} apps and pause {j.Services.Count} services."
            : $"Done. {j.AppsClosed} apps closed, {j.ServicesPaused} services paused.");
        return j;
    }

    void CloseApps(SessionJournal j, IReadOnlyList<ProcInfo> procs, BoostOptions opt, string tag)
    {
        var targets = FindApps(procs, opt, j.Skipped);
        if (opt.DryRun)
        {
            foreach (var (app, ps) in targets)
            {
                var counts = ps.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.Count() == 1 ? $"{g.Key}.exe" : $"{g.Key}.exe x{g.Count()}");
                Say($"{tag}close {app.Label} ({string.Join(", ", counts)})");
                j.Apps.Add(new AppChange { Id = app.Id, Label = app.Label, Process = ps[0].Name });
            }
            foreach (var s in j.Skipped) Say("[dry run] Skip: " + s);
            return;
        }

        // Journal first: remember what was running and where it lives, so it can be reopened.
        var changes = new List<(AppChange change, List<ProcInfo> ps)>();
        foreach (var (app, ps) in targets)
        {
            var main = app.Processes
                .Select(n => ps.FirstOrDefault(p => string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase)))
                .First(p => p != null)!;
            var change = new AppChange
            {
                Id = app.Id,
                Label = app.Label,
                Process = main.Name,
                ExePath = SafePath(main),
                Reopen = app.Reopen && _cfg.ReopenAppsOnRestore,
            };
            j.Apps.Add(change);
            changes.Add((change, ps));
        }
        j.Save();

        // Graceful close first, like clicking X.
        foreach (var (_, ps) in changes)
        {
            foreach (var p in ps) Try(() => _os.RequestClose(p), $"close {p.Name}");
        }

        var deadline = DateTime.UtcNow + opt.GracePeriod;
        while (DateTime.UtcNow < deadline && changes.Any(c => AnyAlive(c.ps))) Thread.Sleep(250);

        // Force-close only what is still running (and still allowed).
        foreach (var (change, ps) in changes)
        {
            var alive = Alive(ps);
            if (alive.Count == 0)
            {
                change.Closed = true;
                change.How = "closed";
                Say($"Closed {change.Label}.");
                continue;
            }
            foreach (var p in alive) Try(() => _os.Kill(p), $"force-close {p.Name}");
        }

        var settle = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (DateTime.UtcNow < settle && changes.Any(c => !c.change.Closed && AnyAlive(c.ps))) Thread.Sleep(200);
        foreach (var (change, ps) in changes.Where(c => !c.change.Closed))
        {
            if (AnyAlive(ps))
            {
                change.How = "still running";
                j.Skipped.Add($"{change.Label}: would not close");
                Say($"Could not close {change.Label}. Skipped.");
            }
            else
            {
                change.Closed = true;
                change.How = "force-closed";
                Say($"Closed {change.Label} (it ignored the polite close).");
            }
        }
        j.Save();
    }

    List<ProcInfo> Alive(List<ProcInfo> ps)
    {
        var now = _os.ListProcesses();
        return ps.Where(p => now.Any(n => n.Pid == p.Pid && string.Equals(n.Name, p.Name, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    bool AnyAlive(List<ProcInfo> ps) => Alive(ps).Count > 0;

    void PauseServices(SessionJournal j, string tag, bool dry)
    {
        bool? ssd = null;
        foreach (var svc in _cfg.Services.Where(s => s.Enabled))
        {
            if (ProtectedList.IsProtectedService(svc.Name))
            {
                j.Skipped.Add($"{svc.Name}: protected service, never touched");
                continue;
            }
            var status = _os.GetServiceStatus(svc.Name);
            if (status == null) continue; // not installed
            if (status != "Running")
            {
                if (dry) Say($"[dry run] Skip {svc.Label}: already {status.ToLowerInvariant()}.");
                continue;
            }
            if (svc.Condition == "ssd")
            {
                ssd ??= _os.SystemDriveIsSsd();
                if (ssd != true)
                {
                    j.Skipped.Add($"{svc.Label}: system drive is not a confirmed SSD");
                    Say($"{(dry ? "[dry run] " : "")}Skip {svc.Label}: system drive is not a confirmed SSD.");
                    continue;
                }
            }
            if (svc.Condition == "no-print-jobs" && !_os.PrintQueueIsEmpty())
            {
                j.Skipped.Add($"{svc.Label}: print jobs are waiting");
                Say($"{(dry ? "[dry run] " : "")}Skip {svc.Label}: print jobs are waiting.");
                continue;
            }

            var change = new ServiceChange { Name = svc.Name, Label = svc.Label, WasRunning = true };
            j.Services.Add(change);
            if (dry)
            {
                Say($"{tag}pause service {svc.Label} ({svc.Name})");
                continue;
            }
            j.Save();
            try
            {
                _os.StopService(svc.Name, TimeSpan.FromSeconds(10));
                change.Stopped = true;
                Say($"Paused {svc.Label}.");
            }
            catch (Exception ex)
            {
                change.Note = Short(ex);
                j.Skipped.Add($"{svc.Label}: {change.Note}");
                Say($"{svc.Label} would not pause ({change.Note}). Skipped.");
            }
            j.Save();
        }
    }

    void SwitchPower(SessionJournal j, string tag, bool dry)
    {
        try
        {
            var active = _os.GetActivePowerPlan();
            if (active == null) { Say("Power plan: could not read the current plan. Skipped."); return; }
            var plans = _os.ListPowerPlans();
            var current = plans.FirstOrDefault(p => p.Id == active)?.Name ?? active.ToString()!;
            var target = plans.FirstOrDefault(p => p.Id == PowerPlans.UltimatePerformance)
                         ?? plans.FirstOrDefault(p => p.Id == PowerPlans.HighPerformance);
            if (target != null && target.Id == active)
            {
                Say($"Power plan is already {current}.");
                return;
            }
            var change = new PowerChange { Original = active.Value, OriginalName = current };
            if (dry)
            {
                Say($"{tag}switch power plan from {current} to {target?.Name ?? "High performance (temporary copy)"}");
                return;
            }
            if (target == null)
            {
                // High Performance is hidden on this PC: make a temporary copy, and delete it on restore.
                j.Power = change;
                j.Save();
                var copy = _os.DuplicatePowerPlan(PowerPlans.HighPerformance);
                if (copy == null) { j.Power = null; j.Save(); Say("Power plan: High performance is not available. Skipped."); return; }
                change.CreatedTemp = copy;
                target = new PowerPlan(copy.Value, "High performance (temporary)");
            }
            change.Applied = target.Id;
            change.AppliedName = target.Name;
            j.Power = change;
            j.Save();
            _os.SetActivePowerPlan(target.Id);
            change.Done = true;
            j.Save();
            Say($"Power plan: {current} → {target.Name}.");
        }
        catch (Exception ex)
        {
            j.Skipped.Add("Power plan: " + Short(ex));
            Say($"Power plan could not be switched ({Short(ex)}). Skipped.");
        }
    }

    void SetRegistry(SessionJournal j, string key, string name, int value, string label, string tag, bool dry)
    {
        try
        {
            var old = _os.ReadUserDword(key, name);
            if (old.Exists && old.Value == value)
            {
                Say($"{label}: already set.");
                return;
            }
            if (dry)
            {
                Say($"{tag}set {label} (HKCU\\{key}\\{name} = {value}, old: {(old.Exists ? old.Value.ToString() : "not set")})");
                return;
            }
            var change = new RegistryChange
            {
                Key = key, Name = name, Existed = old.Exists, OldValue = old.Value, NewValue = value, Label = label,
            };
            j.Registry.Add(change);
            j.Save();
            _os.WriteUserDword(key, name, value);
            change.Applied = true;
            j.Save();
            Say($"{label}.");
        }
        catch (Exception ex)
        {
            j.Skipped.Add($"{label}: {Short(ex)}");
            Say($"{label} failed ({Short(ex)}). Skipped.");
        }
    }

    void Try(Action a, string what)
    {
        try { a(); }
        catch (ProtectedTargetException ex) { Say(ex.Message); }
        catch (Exception ex) { Log.Write($"Could not {what}: {Short(ex)}"); }
    }

    void Try(Func<bool> a, string what) => Try(() => { a(); }, what);

    internal static string Short(Exception ex) =>
        (ex.InnerException?.Message ?? ex.Message).Split('\n')[0].Trim();
}
