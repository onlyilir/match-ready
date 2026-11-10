using MatchReady.Boost;
using MatchReady.Config;
using MatchReady.Restore;
using MatchReady.Sys;

namespace MatchReady.Tests;

[Collection("engine")] // AppPaths is static: run these one at a time
public class EngineTests
{
    readonly string _dir;

    public EngineTests()
    {
        _dir = Directory.CreateTempSubdirectory("cs2b-test").FullName;
        AppPaths.OverrideDataDir(_dir);
    }

    static FakeSystemOps GamingPc()
    {
        var os = new FakeSystemOps();
        var pid = 100;
        void P(string n, int session = 1) => os.Procs.Add(new ProcInfo(n, pid++, session));
        // Must never be touched
        P("cs2"); P("steam"); P("steamwebhelper"); P("FACEIT"); P("faceitclient"); P("Discord"); P("explorer");
        P("NortonUI"); P("RtkAudUService64"); P("RadeonSoftware"); P("RazerAppEngine"); P("NZXT CAM"); P("iCUE");
        P("SteelSeriesGG"); P("AMDNoiseSuppression"); P("svchost", 0); P("RTSS");
        // Closed by default
        P("ollama app"); P("ollama"); P("LM Studio"); P("wallpaper64"); P("OneDrive"); P("EpicGamesLauncher");
        P("lghub"); P("lghub_agent"); P("TeamViewer");
        // Asked first
        P("opera"); P("obs64");
        // Unknown app: never touched
        P("FL64"); P("SomeRandomTool");
        // Same name as an allow-listed app but a background service process (session 0): never touched
        P("OneDrive", 0);

        foreach (var s in new[] { "wuauserv", "UsoSvc", "WaaSMedicSvc", "DoSvc", "BITS", "WSearch", "Spooler",
                     "DiagTrack", "dmwappushservice", "SysMain", "TeamViewer", "FACEIT", "WinDefend", "Audiosrv" })
            os.Services[s] = "Running";
        os.RefuseStop.Add("WaaSMedicSvc"); // usually access denied on real Windows
        os.Registry[RegistryTargets.GameBarKey + "|" + RegistryTargets.GameModeValue] = 0;
        return os;
    }

    BoostOptions Opt(bool dry = false, bool browsers = false, bool obs = false) => new()
    {
        DryRun = dry, CloseBrowsers = browsers, CloseRecorder = obs,
        JournalPath = Path.Combine(_dir, "session.json"), GracePeriod = TimeSpan.FromMilliseconds(300),
    };

    [Fact]
    public void Boost_never_touches_protected_or_unknown_processes_or_services()
    {
        var os = GamingPc();
        var cfg = Defaults.Create();
        // Even if someone forces protected names into the in-memory config, the engine refuses them.
        cfg.Apps.Add(new AppEntry { Id = "bad", Label = "Bad", Processes = { "cs2", "steam", "FACEIT", "Discord", "RTSS" }, Enabled = true });
        cfg.Services.Add(new ServiceEntry { Name = "FACEIT", Label = "bad" });
        cfg.Services.Add(new ServiceEntry { Name = "WinDefend", Label = "bad" });

        new BoostEngine(os, cfg, sessionId: 1).Run(Opt());

        Assert.False(os.AnyProtectedTouched(), string.Join(", ", os.Calls));
        foreach (var keep in new[] { "cs2", "steam", "steamwebhelper", "FACEIT", "faceitclient", "Discord", "NortonUI",
                     "RazerAppEngine", "NZXT CAM", "iCUE", "SteelSeriesGG", "AMDNoiseSuppression", "RTSS", "FL64",
                     "SomeRandomTool", "opera", "obs64" })
            Assert.Contains(os.Procs, p => p.Name == keep);
        Assert.Contains(os.Procs, p => p.Name == "OneDrive" && p.SessionId == 0);
        Assert.Equal("Running", os.Services["FACEIT"]);
        Assert.Equal("Running", os.Services["WinDefend"]);
        Assert.Equal("Running", os.Services["Audiosrv"]);
    }

    [Fact]
    public void Boost_closes_the_allow_listed_apps_and_pauses_services()
    {
        var os = GamingPc();
        var j = new BoostEngine(os, Defaults.Create(), sessionId: 1).Run(Opt());

        foreach (var gone in new[] { "ollama app", "ollama", "LM Studio", "wallpaper64", "EpicGamesLauncher", "TeamViewer" })
            Assert.DoesNotContain(os.Procs, p => p.Name == gone && p.SessionId == 1);
        Assert.Equal("Stopped", os.Services["WSearch"]);
        Assert.Equal("Stopped", os.Services["SysMain"]); // SSD
        Assert.Equal("Running", os.Services["WaaSMedicSvc"]); // refused: skipped quietly
        Assert.Contains(j.Skipped, s => s.Contains("Windows Update Medic"));
        Assert.Equal(PowerPlans.HighPerformance, os.Active);
        Assert.Equal(1, os.Registry[RegistryTargets.GameBarKey + "|" + RegistryTargets.GameModeValue]);
        // Notifications are left alone by default (FACEIT "match found" alerts must come through).
        Assert.False(os.Registry.ContainsKey(RegistryTargets.PushKey + "|" + RegistryTargets.ToastValue));
        Assert.Equal(SessionState.Active, j.State);
        Assert.True(File.Exists(Path.Combine(_dir, "session.json")));
    }

    [Fact]
    public void Browsers_and_OBS_are_only_closed_when_he_says_yes()
    {
        var os = GamingPc();
        new BoostEngine(os, Defaults.Create(), sessionId: 1).Run(Opt(browsers: true, obs: true));
        Assert.DoesNotContain(os.Procs, p => p.Name is "opera" or "obs64");
    }

    [Fact]
    public void Apps_that_ignore_the_polite_close_are_force_closed()
    {
        var os = GamingPc();
        os.IgnoresClose.Add(os.Procs.First(p => p.Name == "OneDrive" && p.SessionId == 1).Pid);
        var j = new BoostEngine(os, Defaults.Create(), sessionId: 1).Run(Opt());
        Assert.Contains("kill:OneDrive", os.Calls);
        Assert.Equal("force-closed", j.Apps.Single(a => a.Label == "OneDrive").How);
    }

    [Fact]
    public void SysMain_is_left_alone_when_the_drive_is_not_a_confirmed_SSD()
    {
        var os = GamingPc();
        os.Ssd = null;
        new BoostEngine(os, Defaults.Create(), sessionId: 1).Run(Opt());
        Assert.Equal("Running", os.Services["SysMain"]);
    }

    [Fact]
    public void Spooler_is_left_alone_when_print_jobs_are_waiting()
    {
        var os = GamingPc();
        os.PrintEmpty = false;
        new BoostEngine(os, Defaults.Create(), sessionId: 1).Run(Opt());
        Assert.Equal("Running", os.Services["Spooler"]);
    }

    [Fact]
    public void Dry_run_changes_nothing_and_writes_no_journal()
    {
        var os = GamingPc();
        var before = os.StateKey();
        var procs = os.Procs.Count;
        var lines = new List<string>();
        new BoostEngine(os, Defaults.Create(), lines.Add, sessionId: 1).Run(Opt(dry: true));

        Assert.Empty(os.Calls);
        Assert.Equal(before, os.StateKey());
        Assert.Equal(procs, os.Procs.Count);
        Assert.False(File.Exists(Path.Combine(_dir, "session.json")));
        Assert.Contains(lines, l => l.Contains("Would close Ollama"));
        Assert.Contains(lines, l => l.Contains("Would pause service Search indexing"));
    }

    [Fact]
    public void Boost_then_restore_returns_everything_to_the_exact_original_state()
    {
        var os = GamingPc();
        // Restore only reopens apps whose exe really exists on disk.
        foreach (var name in new[] { "OneDrive", "ollama app" })
        {
            var exe = Path.Combine(_dir, name, name + ".exe");
            Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
            File.WriteAllText(exe, "");
            os.Paths[os.Procs.First(p => p.Name == name && p.SessionId == 1).Pid] = exe;
        }
        var before = os.StateKey();
        var j = new BoostEngine(os, Defaults.Create(), sessionId: 1).Run(Opt());
        Assert.NotEqual(before, os.StateKey());

        new RestoreEngine(os, sessionId: 1).Restore(j);

        Assert.Equal(before, os.StateKey());
        Assert.False(File.Exists(Path.Combine(_dir, "session.json")));
        Assert.True(File.Exists(AppPaths.LastSessionFile));
        // Default: the booster never opens anything. Closed apps stay closed.
        Assert.DoesNotContain(os.Calls, c => c.StartsWith("launch:"));
        Assert.DoesNotContain(os.Procs, p => p.Name is "OneDrive" or "ollama app" && p.SessionId == 1);
        Assert.False(os.AnyProtectedTouched());
    }

    [Fact]
    public void With_reopen_turned_on_background_apps_come_back_but_not_browsers()
    {
        var os = GamingPc();
        foreach (var name in new[] { "OneDrive", "ollama app", "opera" })
        {
            var exe = Path.Combine(_dir, name, name + ".exe");
            Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
            File.WriteAllText(exe, "");
            os.Paths[os.Procs.First(p => p.Name == name && p.SessionId == 1).Pid] = exe;
        }
        var cfg = Defaults.Create();
        cfg.ReopenAppsOnRestore = true;
        var j = new BoostEngine(os, cfg, sessionId: 1).Run(Opt(browsers: true));
        new RestoreEngine(os, sessionId: 1).Restore(j);

        Assert.Contains(os.Calls, c => c.StartsWith("launch:") && c.Contains("OneDrive"));
        Assert.Contains(os.Calls, c => c.StartsWith("launch:") && c.Contains("ollama app"));
        Assert.DoesNotContain(os.Calls, c => c.StartsWith("launch:") && c.Contains("opera"));
    }

    [Fact]
    public void Old_settings_file_with_reopen_on_is_switched_off_by_the_update()
    {
        var path = Path.Combine(_dir, "config.json");
        File.WriteAllText(path, """{ "reopenAppsOnRestore": true, "browsers": "neverClose", "pauseNotifications": false }""");
        var cfg = AppConfig.Load(path);
        Assert.False(cfg.ReopenAppsOnRestore);
        Assert.Equal(AskMode.NeverClose, cfg.Browsers);   // the other choices are kept
        Assert.False(cfg.PauseNotifications);

        // Once saved as the new version, a deliberate "on" is respected.
        cfg.ReopenAppsOnRestore = true;
        cfg.Save(path);
        Assert.True(AppConfig.Load(path).ReopenAppsOnRestore);
    }

    [Fact]
    public void Restore_puts_back_a_registry_value_that_did_not_exist_by_deleting_it()
    {
        var os = GamingPc();
        os.Registry.Remove(RegistryTargets.GameBarKey + "|" + RegistryTargets.GameModeValue);
        var j = new BoostEngine(os, Defaults.Create(), sessionId: 1).Run(Opt());
        new RestoreEngine(os, sessionId: 1).Restore(j);
        Assert.False(os.ReadUserDword(RegistryTargets.GameBarKey, RegistryTargets.GameModeValue).Exists);
    }

    [Fact]
    public void Temporary_power_plan_is_created_and_deleted_when_High_performance_is_hidden()
    {
        var os = GamingPc();
        os.Plans.RemoveAll(p => p.Id == PowerPlans.HighPerformance);
        var before = os.StateKey();
        var j = new BoostEngine(os, Defaults.Create(), sessionId: 1).Run(Opt());
        Assert.NotNull(j.Power!.CreatedTemp);
        new RestoreEngine(os, sessionId: 1).Restore(j);
        Assert.Equal(before, os.StateKey());
    }

    [Fact]
    public void Crash_mid_boost_is_recovered_from_session_json()
    {
        var os = GamingPc();
        var before = os.StateKey();
        new BoostEngine(os, Defaults.Create(), sessionId: 1).Run(Opt());

        // Simulate a Task Manager kill: the in-memory journal is gone, only the file is left,
        // and it says the boost never finished.
        var path = Path.Combine(_dir, "session.json");
        var onDisk = SessionJournal.TryLoad(path)!;
        onDisk.State = SessionState.Boosting;
        onDisk.Save();

        var recovered = SessionJournal.TryLoad(path)!;
        new RestoreEngine(os, sessionId: 1).Restore(recovered);
        Assert.Equal(before, os.StateKey());
    }

    [Fact]
    public void Shutdown_restores_settings_now_and_leaves_services_for_next_start()
    {
        var os = GamingPc();
        var before = os.StateKey();
        var j = new BoostEngine(os, Defaults.Create(), sessionId: 1).Run(Opt());
        new RestoreEngine(os, sessionId: 1).Restore(j, shuttingDown: true);

        Assert.Equal(0, os.ReadUserDword(RegistryTargets.GameBarKey, RegistryTargets.GameModeValue).Value); // original
        Assert.NotEqual(PowerPlans.HighPerformance, os.Active);
        Assert.Equal("Stopped", os.Services["WSearch"]); // left for the next start
        Assert.True(File.Exists(Path.Combine(_dir, "session.json"))); // next start finishes the job

        var next = SessionJournal.TryLoad(Path.Combine(_dir, "session.json"))!;
        os.Calls.Clear();
        new RestoreEngine(os, sessionId: 1).Restore(next);
        Assert.Equal(before, os.StateKey());
        Assert.DoesNotContain(os.Calls, c => c.StartsWith("launch:")); // no surprise app launches after a reboot
    }

    [Fact]
    public void Restoring_twice_is_harmless()
    {
        var os = GamingPc();
        var before = os.StateKey();
        var j = new BoostEngine(os, Defaults.Create(), sessionId: 1).Run(Opt());
        new RestoreEngine(os, sessionId: 1).Restore(j);
        new RestoreEngine(os, sessionId: 1).Restore(j);
        Assert.Equal(before, os.StateKey());
    }

    [Fact]
    public void Warnings_flag_RTSS_without_closing_it()
    {
        var os = GamingPc();
        Assert.Contains(BoostEngine.Warnings(os.Procs), w => w.Contains("RTSS"));
        new BoostEngine(os, Defaults.Create(), sessionId: 1).Run(Opt());
        Assert.Contains(os.Procs, p => p.Name == "RTSS");
    }
}
