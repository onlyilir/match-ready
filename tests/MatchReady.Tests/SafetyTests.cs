using MatchReady.Config;
using MatchReady.Safety;
using MatchReady.Sys;

namespace MatchReady.Tests;

/// <summary>The kill/stop code must refuse protected names, whatever the config says.</summary>
[Collection("engine")]
public class SafetyTests
{
    // Every name from the original protected list, plus common antivirus/peripheral/overlay software.
    public static readonly string[] ProtectedProcesses =
    {
        "cs2", "cs2.exe", "CS2.EXE", "csgo", "steam", "steamwebhelper", "steamservice", "steamerrorreporter",
        "faceit", "faceitclient", "faceit-ac", "faceit-ac-service", "FACEITService", "FaceitClient.exe",
        "discord", "DiscordPTB", "explorer", "dwm", "csrss", "winlogon", "lsass", "services", "svchost", "smss",
        "wininit", "audiodg", "MsMpEng", "SecurityHealthService", "SecurityHealthSystray", "nvcontainer",
        "NVDisplay.Container", "RadeonSoftware", "AMDRSServ", "AMDRSSrcExt", "igfxEM", "igfxCUIService",
        "RealtekAudioService", "RtkAudUService64", "Razer Synapse Service", "RazerCentralService",
        "lghub_updater", "NortonUI", "NortonSvc", "nsWscSvc", "RTSS", "RTSSHooksLoader64", "EncoderServer64",
        "MSIAfterburner", "AMD Ryzen Master", "EasyAntiCheat", "vgc", "MatchReady", "", "   ",
    };

    public static readonly string[] ProtectedServices =
    {
        "FACEIT", "faceit", "Steam Client Service", "WinDefend", "Sense", "SecurityHealthService", "Audiosrv",
        "AudioEndpointBuilder", "Dhcp", "Dnscache", "NlaSvc", "nsi", "RpcSs", "PlugPlay", "Power", "EventLog",
        "gpsvc", "NVDisplay.ContainerLocalSystem", "NVIDIA LocalSystem Container", "AMD External Events Utility",
        "AMDRyzenMasterDriverV22", "mpssvc", "BFE", "NortonSvc", "Razer Synapse Service", "RtkAudioUniversalService",
        "WlanSvc",
    };

    public static IEnumerable<object[]> ProcessCases() => ProtectedProcesses.Select(n => new object[] { n });
    public static IEnumerable<object[]> ServiceCases() => ProtectedServices.Select(n => new object[] { n });

    [Theory, MemberData(nameof(ProcessCases))]
    public void Protected_process_names_are_protected(string name) =>
        Assert.True(ProtectedList.IsProtectedProcess(name), name);

    [Theory, MemberData(nameof(ServiceCases))]
    public void Protected_service_names_are_protected(string name) =>
        Assert.True(ProtectedList.IsProtectedService(name), name);

    [Theory, MemberData(nameof(ProcessCases))]
    public void Real_kill_close_and_path_calls_refuse_protected_processes(string name)
    {
        // PID -1: if the guard did not throw first, Windows would fail differently (and nothing real is touched).
        var os = new WindowsSystemOps();
        var p = new ProcInfo(name, -1, 1);
        Assert.Throws<ProtectedTargetException>(() => os.Kill(p));
        Assert.Throws<ProtectedTargetException>(() => os.RequestClose(p));
        Assert.Throws<ProtectedTargetException>(() => os.GetProcessPath(p));
    }

    [Theory, MemberData(nameof(ServiceCases))]
    public void Real_stop_and_start_calls_refuse_protected_services(string name)
    {
        var os = new WindowsSystemOps();
        Assert.Throws<ProtectedTargetException>(() => os.StopService(name, TimeSpan.FromSeconds(1)));
        Assert.Throws<ProtectedTargetException>(() => os.StartService(name, TimeSpan.FromSeconds(1)));
    }

    [Theory]
    [InlineData("AMDNoiseSuppression")] // toggle, default keep
    [InlineData("RazerAppEngine")]      // toggle "I don't use THX Spatial Audio"
    [InlineData("NortonBrowser")]       // Norton Private Browser is a browser, not the antivirus
    [InlineData("OneDrive")]
    [InlineData("ollama app")]
    [InlineData("wallpaper64")]
    [InlineData("lghub")]
    [InlineData("iCUE")]
    [InlineData("NZXT CAM")]
    public void Closable_apps_are_not_protected(string name) =>
        Assert.False(ProtectedList.IsProtectedProcess(name));

    [Fact]
    public void Every_default_allow_list_entry_is_closable_and_every_default_service_stoppable()
    {
        var cfg = Defaults.Create();
        foreach (var n in cfg.Apps.SelectMany(a => a.Processes))
            Assert.False(ProtectedList.IsProtectedProcess(n), $"default app list contains protected '{n}'");
        foreach (var s in cfg.Services)
            Assert.False(ProtectedList.IsProtectedService(s.Name), $"default service list contains protected '{s.Name}'");
    }

    [Fact]
    public void Steam_Discord_and_FACEIT_are_never_in_the_default_lists()
    {
        var cfg = Defaults.Create();
        var all = cfg.Apps.SelectMany(a => a.Processes).Concat(cfg.Services.Select(s => s.Name)).ToList();
        Assert.DoesNotContain(all, n => n.StartsWith("steam", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(all, n => n.StartsWith("discord", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(all, n => n.StartsWith("faceit", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(all, n => n.Equals("cs2", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Device_software_is_kept_running_by_default()
    {
        var cfg = Defaults.Create();
        foreach (var (id, _, _) in QuickSetup.Devices)
            Assert.False(cfg.Apps.Single(a => a.Id == id).Enabled, $"{id} should stay running by default");
    }

    [Fact]
    public void System32_paths_are_refused()
    {
        var sys = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "OneDrive.exe");
        Assert.Throws<ProtectedTargetException>(() => Guard.EnsureProcessPathAllowed("OneDrive", sys));
        Assert.Throws<ProtectedTargetException>(() => Guard.EnsureProcessPathAllowed("OneDrive", null));
        Guard.EnsureProcessPathAllowed("OneDrive", @"C:\Program Files\Microsoft OneDrive\OneDrive.exe");
    }

    [Fact]
    public void Config_file_cannot_sneak_in_protected_names()
    {
        var dir = Directory.CreateTempSubdirectory("cs2b-cfg").FullName;
        AppPaths.OverrideDataDir(dir);
        var path = Path.Combine(dir, "config.json");
        File.WriteAllText(path, """
        {
          "apps": [ { "id": "evil", "label": "Evil", "processes": ["cs2", "steam", "faceitclient", "discord", "Notepad++"], "enabled": true } ],
          "services": [ { "name": "FACEIT", "label": "x" }, { "name": "WinDefend", "label": "y" } ]
        }
        """);
        var cfg = AppConfig.Load(path);
        Assert.Equal(new[] { "Notepad++" }, cfg.Apps.Single(a => a.Id == "evil").Processes);
        Assert.DoesNotContain(cfg.Services, s => s.Name is "FACEIT" or "WinDefend");
    }
}
