using MatchReady.Boost;
using MatchReady.Config;
using MatchReady.Restore;
using MatchReady.Sys;
using MatchReady.UI;

namespace MatchReady.Tests;

/// <summary>v1.2: self-check, profiles, freed RAM, history, log limit, broken config, updates, PC detection, rename.</summary>
[Collection("engine")]
public class NewFeatureTests
{
    readonly string _dir;

    public NewFeatureTests()
    {
        _dir = Directory.CreateTempSubdirectory("mr-new").FullName;
        AppPaths.OverrideDataDir(_dir);
        AppPaths.OverrideLegacyDataDir(Path.Combine(_dir, "legacy-CS2Booster"));
    }

    static FakeSystemOps Pc()
    {
        var os = new FakeSystemOps();
        os.Procs.Add(new ProcInfo("ollama app", 10, 1));
        os.Procs.Add(new ProcInfo("OneDrive", 11, 1));
        os.Procs.Add(new ProcInfo("EpicGamesLauncher", 12, 1));
        foreach (var s in new[] { "wuauserv", "WSearch", "DiagTrack", "Spooler" }) os.Services[s] = "Running";
        return os;
    }

    BoostOptions Opt(bool skipApps = false) => new()
    {
        JournalPath = Path.Combine(_dir, "session.json"), GracePeriod = TimeSpan.FromMilliseconds(100), SkipApps = skipApps,
    };

    // ---------- Self-check ----------

    [Fact]
    public void Self_check_passes_and_closes_no_apps()
    {
        var os = Pc();
        var r = SelfCheck.Run(os, Defaults.Create());
        Assert.True(r.Passed, string.Join("\n", r.Lines));
        Assert.DoesNotContain(os.Calls, c => c.StartsWith("close:") || c.StartsWith("kill:"));
        Assert.Contains(os.Calls, c => c.StartsWith("stop:"));   // it really paused services
        Assert.True(File.Exists(r.ReportPath));
        Assert.Empty(SessionJournal.LoadHistory());               // self-checks don't clutter the history
    }

    [Fact]
    public void Self_check_reports_a_service_that_did_not_come_back()
    {
        var os = new RefusesStartOps();
        os.Services["WSearch"] = "Running";
        var r = SelfCheck.Run(os, Defaults.Create());
        Assert.False(r.Passed);
        Assert.Contains(r.Lines, l => l.Contains("NOT BACK") && l.Contains("WSearch"));
    }

    [Fact]
    public void Self_check_refuses_while_CS2_runs()
    {
        var os = Pc();
        os.Procs.Add(new ProcInfo("cs2", 99, 1));
        var r = SelfCheck.Run(os, Defaults.Create());
        Assert.False(r.Passed);
        Assert.Empty(os.Calls);
    }

    sealed class RefusesStartOps : ISystemOps
    {
        readonly FakeSystemOps _f = new();
        public Dictionary<string, string> Services => _f.Services;
        public IReadOnlyList<ProcInfo> ListProcesses() => _f.ListProcesses();
        public string? GetProcessPath(ProcInfo p) => _f.GetProcessPath(p);
        public bool RequestClose(ProcInfo p) => _f.RequestClose(p);
        public bool Kill(ProcInfo p) => _f.Kill(p);
        public void LaunchUnelevated(string exePath) => _f.LaunchUnelevated(exePath);
        public bool ExeNeedsAdmin(string exePath) => false;
        public string? GetServiceStatus(string name) => _f.GetServiceStatus(name);
        public void StopService(string name, TimeSpan timeout) => _f.StopService(name, timeout);
        public void StartService(string name, TimeSpan timeout) => throw new InvalidOperationException("won't start");
        public IReadOnlyList<PowerPlan> ListPowerPlans() => _f.ListPowerPlans();
        public Guid? GetActivePowerPlan() => _f.GetActivePowerPlan();
        public void SetActivePowerPlan(Guid id) => _f.SetActivePowerPlan(id);
        public Guid? DuplicatePowerPlan(Guid source) => _f.DuplicatePowerPlan(source);
        public void DeletePowerPlan(Guid id) => _f.DeletePowerPlan(id);
        public RegValue ReadUserDword(string key, string name) => _f.ReadUserDword(key, name);
        public void WriteUserDword(string key, string name, int value) => _f.WriteUserDword(key, name, value);
        public void DeleteUserValue(string key, string name) => _f.DeleteUserValue(key, name);
        public long AvailableMemoryBytes() => 0;
        public bool? SystemDriveIsSsd() => true;
        public bool PrintQueueIsEmpty() => true;
    }

    // ---------- Profiles and freed RAM ----------

    [Fact]
    public void Light_profile_closes_only_heavy_apps_and_pauses_no_services()
    {
        var os = Pc();
        var cfg = Defaults.Create();
        cfg.Profile = BoostProfile.Light;
        var j = new BoostEngine(os, cfg, sessionId: 1).Run(Opt());
        Assert.Contains(os.Calls, c => c == "close:ollama app");
        Assert.DoesNotContain(os.Calls, c => c.Contains("OneDrive") || c.Contains("Epic"));
        Assert.DoesNotContain(os.Calls, c => c.StartsWith("stop:"));
        Assert.Equal(0, j.ServicesPaused);
    }

    [Fact]
    public void Freed_RAM_is_measured_when_apps_close()
    {
        var j = new BoostEngine(Pc(), Defaults.Create(), sessionId: 1).Run(Opt());
        Assert.True(j.RamFreedBytes > 0);
        Assert.Equal("1.5 GB", MainForm.FormatBytes(1_610_612_736));
        Assert.Equal("500 MB", MainForm.FormatBytes(524_288_000));
    }

    // ---------- History ----------

    [Fact]
    public void History_keeps_the_last_ten_finished_boosts_newest_first()
    {
        for (var i = 0; i < 12; i++)
        {
            var os = Pc();
            var j = new BoostEngine(os, Defaults.Create(), sessionId: 1).Run(Opt());
            new RestoreEngine(os, sessionId: 1).Restore(j);
            Thread.Sleep(5);
        }
        var h = SessionJournal.LoadHistory();
        Assert.Equal(SessionJournal.HistoryLimit, h.Count);
        Assert.True(h[0].StartedUtc >= h[^1].StartedUtc);
        Assert.All(h, x => Assert.NotNull(x.EndedUtc));
    }

    [Fact]
    public void Durations_read_naturally()
    {
        Assert.Equal("under a minute", MainForm.Duration(TimeSpan.FromSeconds(20)));
        Assert.Equal("12 min", MainForm.Duration(TimeSpan.FromMinutes(12)));
        Assert.Equal("1 h 12 min", MainForm.Duration(TimeSpan.FromMinutes(72)));
    }

    // ---------- Log limit and broken settings ----------

    [Fact]
    public void Log_is_kept_under_its_size_limit()
    {
        File.WriteAllText(AppPaths.LogFile, new string('x', (int)Log.MaxBytes + 10));
        Log.Write("after the limit");
        Assert.True(new FileInfo(AppPaths.LogFile).Length < 1000);
        Assert.True(File.Exists(AppPaths.OldLogFile));
    }

    [Fact]
    public void A_broken_settings_file_is_kept_as_bak_and_defaults_are_used()
    {
        var path = Path.Combine(_dir, "config.json");
        File.WriteAllText(path, "{ this is not json");
        var cfg = AppConfig.Load(path);
        Assert.NotNull(cfg.LoadProblem);
        Assert.True(File.Exists(path + ".bak"));
        Assert.Equal("{ this is not json", File.ReadAllText(path + ".bak"));
        Assert.True(cfg.Apps.Count > 10); // defaults
    }

    [Fact]
    public void New_installs_do_not_pause_notifications_by_default()
    {
        Assert.False(Defaults.Create().PauseNotifications); // FACEIT "match found" alerts must come through
        Assert.False(Defaults.Create().CheckForUpdates);    // no network unless turned on
        Assert.False(Defaults.Create().StartWithWindows);
    }

    // ---------- Updates ----------

    [Theory]
    [InlineData("v1.2.0", "1.2.0")]
    [InlineData("1.3", "1.3.0")]
    [InlineData("V2.0.1", "2.0.1")]
    public void Release_tags_are_read(string tag, string expected)
    {
        Assert.True(UpdateChecker.TryParse(tag, out var v));
        Assert.Equal(expected, v.ToString(3));
    }

    [Fact]
    public void Garbage_tags_are_rejected() => Assert.False(UpdateChecker.TryParse("latest", out _));

    // ---------- PC detection ----------

    [Fact]
    public void Expectations_follow_the_amount_of_RAM()
    {
        Assert.Contains("real difference", new PcInfo { RamBytes = 8L << 30 }.Expectation);
        Assert.Contains("16 GB", new PcInfo { RamBytes = 16L << 30 }.Expectation);
        Assert.Contains("not big FPS jumps", new PcInfo { RamBytes = 32L << 30 }.Expectation);
    }

    [Fact]
    public void Quick_setup_only_lists_device_software_this_PC_has()
    {
        var pc = new PcInfo { InstalledApps = new[] { "Razer Synapse", "NZXT CAM 4.76.6", "FL Studio 2026" } };
        var found = QuickSetup.FoundOn(pc, new[] { "SteelSeriesGG" }, Defaults.Create()).Select(f => f.Id).ToList();
        Assert.Equal(new[] { "steelseries", "razer", "nzxtcam" }, found); // installed, or running right now
    }

    [Fact]
    public void Detection_runs_on_this_PC_without_errors()
    {
        var pc = PcInfo.Detect(new WindowsSystemOps());
        Assert.True(pc.RamBytes > 0);
        Assert.NotEmpty(pc.InstalledApps);
        Assert.False(string.IsNullOrWhiteSpace(pc.Summary));
    }

    // ---------- Rename from CS2 Booster ----------

    [Fact]
    public void An_unfinished_boost_from_the_old_name_is_moved_over_for_recovery()
    {
        Directory.CreateDirectory(AppPaths.LegacyDataDir);
        File.WriteAllText(Path.Combine(AppPaths.LegacyDataDir, "session.json"), "{}");
        File.WriteAllText(Path.Combine(AppPaths.LegacyDataDir, "log.txt"), "old log");
        AppPaths.MigrateLegacyData();
        Assert.True(File.Exists(AppPaths.SessionFile));
        Assert.False(File.Exists(Path.Combine(AppPaths.LegacyDataDir, "session.json")));
        Assert.StartsWith("old log", File.ReadAllText(AppPaths.LogFile));
    }

    [Fact]
    public void The_old_and_new_app_names_are_protected() =>
        Assert.True(Safety.ProtectedList.IsProtectedProcess("MatchReady") && Safety.ProtectedList.IsProtectedProcess("CS2Booster"));
}
