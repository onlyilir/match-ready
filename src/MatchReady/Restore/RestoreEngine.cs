using MatchReady.Boost;
using MatchReady.Safety;
using MatchReady.Sys;

namespace MatchReady.Restore;

/// <summary>
/// Puts back everything a journal says was changed. Safe to run more than once:
/// each step checks the current state first and only acts if it differs.
/// </summary>
public sealed class RestoreEngine
{
    readonly ISystemOps _os;
    readonly Action<string> _report;
    readonly int _mySession;

    public RestoreEngine(ISystemOps os, Action<string>? report = null, int? sessionId = null)
    {
        _os = os;
        _report = report ?? (_ => { });
        _mySession = sessionId ?? System.Diagnostics.Process.GetCurrentProcess().SessionId;
    }

    void Say(string msg)
    {
        Log.Write(msg);
        _report(msg);
    }

    /// <param name="shuttingDown">Windows is shutting down: only restore settings; services come back on reboot anyway.</param>
    public void Restore(SessionJournal j, bool shuttingDown = false, bool reopenApps = true)
    {
        Say(shuttingDown ? "Windows is shutting down: restoring settings." : "Restoring everything.");
        j.State = SessionState.Restoring;
        Save(j);

        // Settings first: these are the only changes that would survive a reboot.
        RestorePower(j);
        foreach (var r in Enumerable.Reverse(j.Registry)) RestoreRegistry(r);

        if (shuttingDown)
        {
            // Windows only gives us a few seconds. Services come back on reboot; after a logoff,
            // the next start of the booster finishes the job from session.json (without reopening apps).
            j.ShutdownRestore = true;
            Save(j);
            return;
        }

        foreach (var s in j.Services.Where(s => s.WasRunning)) RestoreService(s);
        if (reopenApps && !j.ShutdownRestore) ReopenApps(j);

        j.State = SessionState.Restored;
        j.EndedUtc = DateTime.UtcNow;
        Save(j);
        j.Archive(AppPaths.LastSessionFile);
        Say("Restored.");
    }

    void RestorePower(SessionJournal j)
    {
        var p = j.Power;
        if (p == null) return;
        try
        {
            if (_os.GetActivePowerPlan() != p.Original)
            {
                _os.SetActivePowerPlan(p.Original);
                Say($"Power plan back to {p.OriginalName}.");
            }
            if (p.CreatedTemp is Guid temp && _os.ListPowerPlans().Any(x => x.Id == temp))
            {
                _os.DeletePowerPlan(temp);
                Say("Removed the temporary High performance plan.");
            }
        }
        catch (Exception ex)
        {
            Say($"Power plan restore failed ({BoostEngine.Short(ex)}). Reboot or pick it in Settings > Power.");
        }
    }

    void RestoreRegistry(RegistryChange r)
    {
        try
        {
            var now = _os.ReadUserDword(r.Key, r.Name);
            if (r.Existed)
            {
                if (!now.Exists || now.Value != r.OldValue) _os.WriteUserDword(r.Key, r.Name, r.OldValue);
            }
            else if (now.Exists)
            {
                _os.DeleteUserValue(r.Key, r.Name);
            }
            Say($"{r.Label}: put back.");
        }
        catch (Exception ex)
        {
            Say($"{r.Label}: restore failed ({BoostEngine.Short(ex)}).");
        }
    }

    void RestoreService(ServiceChange s)
    {
        try
        {
            var status = _os.GetServiceStatus(s.Name);
            if (status == null || status == "Running") return;
            _os.StartService(s.Name, TimeSpan.FromSeconds(20));
            Say($"Started {s.Label} again.");
        }
        catch (ProtectedTargetException ex)
        {
            Say(ex.Message);
        }
        catch (Exception ex)
        {
            // Windows starts these on its own when needed, and always on reboot.
            Say($"{s.Label} did not start yet ({BoostEngine.Short(ex)}). Windows will start it when needed.");
        }
    }

    void ReopenApps(SessionJournal j)
    {
        var running = _os.ListProcesses();
        foreach (var a in j.Apps.Where(a => a.Closed && a.Reopen && !string.IsNullOrEmpty(a.ExePath)))
        {
            try
            {
                if (running.Any(p => p.SessionId == _mySession &&
                                     string.Equals(p.Name, a.Process, StringComparison.OrdinalIgnoreCase))) continue;
                if (!File.Exists(a.ExePath)) continue;
                Guard.EnsureProcessPathAllowed(a.Process, a.ExePath);
                if (_os.ExeNeedsAdmin(a.ExePath!))
                {
                    // Reopening it would pop up a Windows admin prompt. Windows starts it again at the next sign-in.
                    Say($"{a.Label} needs admin rights to start, so it wasn't reopened. It comes back at the next restart.");
                    continue;
                }
                _os.LaunchUnelevated(a.ExePath!);
                Say($"Reopened {a.Label}.");
                Thread.Sleep(300);
            }
            catch (Exception ex)
            {
                Say($"Could not reopen {a.Label} ({BoostEngine.Short(ex)}).");
            }
        }
    }

    static void Save(SessionJournal j)
    {
        try { j.Save(); }
        catch (Exception ex) { Log.Write($"session.json save failed: {ex.Message}"); }
    }
}
