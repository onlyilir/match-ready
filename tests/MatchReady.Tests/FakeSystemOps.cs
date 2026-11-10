using MatchReady.Safety;
using MatchReady.Sys;

namespace MatchReady.Tests;

/// <summary>In-memory Windows. Records every call that would change something.</summary>
public sealed class FakeSystemOps : ISystemOps
{
    public List<ProcInfo> Procs { get; } = new();
    public Dictionary<int, string> Paths { get; } = new();
    public HashSet<int> IgnoresClose { get; } = new();
    public Dictionary<string, string> Services { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> RefuseStop { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<PowerPlan> Plans { get; } = new()
    {
        new(new Guid("381b4222-f694-41f0-9685-ff5bb260df2e"), "Balanced"),
        new(new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"), "High performance"),
    };
    public Guid Active { get; set; } = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public Dictionary<string, int> Registry { get; } = new();
    public bool? Ssd { get; set; } = true;
    public bool PrintEmpty { get; set; } = true;

    /// <summary>Every mutating call, e.g. "close:OneDrive", "kill:OneDrive", "stop:WSearch".</summary>
    public List<string> Calls { get; } = new();
    /// <summary>Process names that reached a handle-opening call.</summary>
    public List<string> TouchedProcs { get; } = new();
    /// <summary>Service names that reached a handle-opening call.</summary>
    public List<string> TouchedServices { get; } = new();

    public IReadOnlyList<ProcInfo> ListProcesses() => Procs.ToList();

    public string? GetProcessPath(ProcInfo p)
    {
        TouchedProcs.Add(p.Name);
        return Paths.TryGetValue(p.Pid, out var path) ? path : $@"C:\Program Files\{p.Name}\{p.Name}.exe";
    }

    public bool RequestClose(ProcInfo p)
    {
        TouchedProcs.Add(p.Name);
        Calls.Add("close:" + p.Name);
        if (!IgnoresClose.Contains(p.Pid)) Procs.RemoveAll(x => x.Pid == p.Pid);
        return true;
    }

    public bool Kill(ProcInfo p)
    {
        TouchedProcs.Add(p.Name);
        Calls.Add("kill:" + p.Name);
        Procs.RemoveAll(x => x.Pid == p.Pid);
        return true;
    }

    public void LaunchUnelevated(string exePath)
    {
        Calls.Add("launch:" + exePath);
        var name = Path.GetFileNameWithoutExtension(exePath);
        Procs.Add(new ProcInfo(name, 50000 + Procs.Count, 1));
    }

    /// <summary>Exe paths whose manifest asks for admin (like Gigabyte Control Center).</summary>
    public HashSet<string> AdminExes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool ExeNeedsAdmin(string exePath) => AdminExes.Contains(exePath);

    public string? GetServiceStatus(string name) => Services.TryGetValue(name, out var s) ? s : null;

    public void StopService(string name, TimeSpan timeout)
    {
        TouchedServices.Add(name);
        Calls.Add("stop:" + name);
        if (RefuseStop.Contains(name)) throw new InvalidOperationException("Access is denied.");
        Services[name] = "Stopped";
    }

    public void StartService(string name, TimeSpan timeout)
    {
        TouchedServices.Add(name);
        Calls.Add("start:" + name);
        Services[name] = "Running";
    }

    public IReadOnlyList<PowerPlan> ListPowerPlans() => Plans.ToList();
    public Guid? GetActivePowerPlan() => Active;
    public void SetActivePowerPlan(Guid id) { Calls.Add("power:" + id); Active = id; }

    public Guid? DuplicatePowerPlan(Guid source)
    {
        var id = Guid.NewGuid();
        Plans.Add(new PowerPlan(id, "High performance"));
        Calls.Add("dup:" + id);
        return id;
    }

    public void DeletePowerPlan(Guid id) { Calls.Add("delete:" + id); Plans.RemoveAll(p => p.Id == id); }

    public RegValue ReadUserDword(string key, string name) =>
        Registry.TryGetValue(key + "|" + name, out var v) ? new RegValue(true, v) : new RegValue(false, 0);

    public void WriteUserDword(string key, string name, int value) { Calls.Add($"reg:{name}={value}"); Registry[key + "|" + name] = value; }
    public void DeleteUserValue(string key, string name) { Calls.Add($"regdel:{name}"); Registry.Remove(key + "|" + name); }

    /// <summary>Free RAM: each closed app gives back 500 MB in the fake.</summary>
    public long AvailableMemoryBytes() => 8L * 1024 * 1024 * 1024 + Calls.Count(x => x.StartsWith("close:") || x.StartsWith("kill:")) * 500L * 1024 * 1024;
    public bool? SystemDriveIsSsd() => Ssd;
    public bool PrintQueueIsEmpty() => PrintEmpty;

    public string StateKey() =>
        string.Join(";", Services.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")) + "|" + Active + "|" +
        string.Join(";", Registry.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")) + "|" +
        string.Join(";", Plans.Select(p => p.Id).OrderBy(g => g));

    public bool AnyProtectedTouched() =>
        TouchedProcs.Any(ProtectedList.IsProtectedProcess) || TouchedServices.Any(ProtectedList.IsProtectedService);
}
