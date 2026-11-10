using System.ServiceProcess;
using MatchReady.Config;
using MatchReady.Sys;

namespace MatchReady.Boost;

/// <summary>Read-only picture of everything the boost can change. Used to prove restore puts it all back.</summary>
public sealed class StateSnapshot
{
    public Dictionary<string, string> AllowListedServices { get; set; } = new();
    public List<string> RunningServices { get; set; } = new();
    public string? ActivePowerPlan { get; set; }
    public List<string> PowerPlans { get; set; } = new();
    public string GameMode { get; set; } = "";
    public string Notifications { get; set; } = "";

    public static StateSnapshot Capture(ISystemOps os, AppConfig cfg)
    {
        var s = new StateSnapshot();
        foreach (var svc in cfg.Services)
            s.AllowListedServices[svc.Name] = os.GetServiceStatus(svc.Name) ?? "not installed";

        // Service Control Manager enumeration only: no handle to any individual service.
        foreach (var sc in ServiceController.GetServices())
        {
            using (sc)
            {
                if (sc.Status == ServiceControllerStatus.Running) s.RunningServices.Add(sc.ServiceName);
            }
        }
        s.RunningServices.Sort(StringComparer.OrdinalIgnoreCase);

        s.ActivePowerPlan = os.GetActivePowerPlan()?.ToString();
        s.PowerPlans = os.ListPowerPlans().Select(p => $"{p.Id} {p.Name}").ToList();
        s.GameMode = Show(os.ReadUserDword(RegistryTargets.GameBarKey, RegistryTargets.GameModeValue));
        s.Notifications = Show(os.ReadUserDword(RegistryTargets.PushKey, RegistryTargets.ToastValue));
        return s;
    }

    static string Show(RegValue v) => v.Exists ? v.Value.ToString() : "not set";
}
