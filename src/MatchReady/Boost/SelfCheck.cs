using MatchReady.Config;
using MatchReady.Restore;
using MatchReady.Sys;

namespace MatchReady.Boost;

/// <summary>
/// One-click proof that restore really puts everything back: snapshot, boost (services and settings only,
/// no apps are closed), snapshot, restore, snapshot, compare. Writes self-check.txt next to the log.
/// </summary>
public static class SelfCheck
{
    public sealed record Result(bool Passed, IReadOnlyList<string> Lines, string ReportPath);

    public static Result Run(ISystemOps os, AppConfig cfg, Action<string>? report = null)
    {
        var lines = new List<string>();
        void Say(string s)
        {
            lines.Add(s);
            report?.Invoke(s);
        }

        var reportPath = Path.Combine(AppPaths.DataDir, "self-check.txt");
        Say($"Match Ready self-check, {DateTime.Now:yyyy-MM-dd HH:mm}. Windows build {WindowsInfo.BuildText()}.");

        if (SessionJournal.TryLoad(AppPaths.SessionFile) != null)
            return Finish(false, "A boost is active. Restore it first, then run the self-check.");
        if (BoostEngine.IsGameRunning(os.ListProcesses()))
            return Finish(false, "CS2 is running. Close it first: the self-check changes services and settings.");

        var before = StateSnapshot.Capture(os, cfg);
        Say("1. Saved how things are now.");

        var j = new BoostEngine(os, cfg).Run(new BoostOptions { SkipApps = true, JournalPath = AppPaths.SessionFile });
        j.IsSelfCheck = true;
        j.Save();
        var during = StateSnapshot.Capture(os, cfg);
        var changed = Differences(before, during).Count;
        Say($"2. Boosted without closing any apps: {j.ServicesPaused} services paused, {changed} things changed.");

        new RestoreEngine(os).Restore(j, reopenApps: false);
        var after = StateSnapshot.Capture(os, cfg);
        Say("3. Restored.");

        var diffs = Differences(before, after);
        foreach (var d in diffs) Say("   NOT BACK: " + d);

        var extra = after.RunningServices.Except(before.RunningServices, StringComparer.OrdinalIgnoreCase).ToList();
        if (extra.Count > 0)
            Say("   (Windows started these by itself meanwhile; that's normal: " + string.Join(", ", extra) + ")");

        return Finish(diffs.Count == 0, diffs.Count == 0
            ? "PASS: every service and setting is exactly as it was before."
            : $"FAIL: {diffs.Count} thing(s) did not come back. A reboot puts them back; please send this report.");

        Result Finish(bool passed, string verdict)
        {
            Say(verdict);
            try
            {
                Directory.CreateDirectory(AppPaths.DataDir);
                File.WriteAllLines(reportPath, lines);
            }
            catch (Exception ex)
            {
                Log.Write("Could not save self-check.txt: " + ex.Message);
            }
            Log.Write("Self-check: " + verdict);
            return new Result(passed, lines, reportPath);
        }
    }

    /// <summary>What differs between two snapshots, in plain words.</summary>
    public static List<string> Differences(StateSnapshot a, StateSnapshot b)
    {
        var d = new List<string>();
        foreach (var (name, status) in a.AllowListedServices)
        {
            b.AllowListedServices.TryGetValue(name, out var now);
            if (!string.Equals(status, now, StringComparison.OrdinalIgnoreCase)) d.Add($"service {name}: was {status}, now {now}");
        }
        if (a.ActivePowerPlan != b.ActivePowerPlan) d.Add($"power plan: was {a.ActivePowerPlan}, now {b.ActivePowerPlan}");
        if (!a.PowerPlans.SequenceEqual(b.PowerPlans)) d.Add("the list of power plans changed");
        if (a.GameMode != b.GameMode) d.Add($"Game Mode setting: was {a.GameMode}, now {b.GameMode}");
        if (a.Notifications != b.Notifications) d.Add($"notifications setting: was {a.Notifications}, now {b.Notifications}");
        return d;
    }
}
