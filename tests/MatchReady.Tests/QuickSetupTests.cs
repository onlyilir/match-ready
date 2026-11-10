using MatchReady.Boost;
using MatchReady.Config;
using MatchReady.Sys;

namespace MatchReady.Tests;

[Collection("engine")]
public class QuickSetupTests
{
    [Fact]
    public void Defaults_keep_all_device_software_running()
    {
        var a = QuickSetup.From(Defaults.Create());
        Assert.False(a.QueueInBrowser);
        Assert.False(a.UsesObs);
        Assert.False(a.PauseNotifications);
        Assert.Equal(new[] { "amdnoise", "ghub", "gigabyte", "icue", "nzxtcam", "razer", "steelseries" }, a.KeepRunning.OrderBy(x => x));
    }

    [Fact]
    public void Browser_queue_means_browsers_are_never_closed()
    {
        var c = Defaults.Create();
        new QuickSetup { QueueInBrowser = true }.ApplyTo(c);
        Assert.Equal(AskMode.NeverClose, c.Browsers);
        new QuickSetup { QueueInBrowser = false }.ApplyTo(c);
        Assert.Equal(AskMode.Ask, c.Browsers);
    }

    [Fact]
    public void OBS_users_never_get_OBS_closed()
    {
        var c = Defaults.Create();
        new QuickSetup { UsesObs = true }.ApplyTo(c);
        Assert.Equal(AskMode.NeverClose, c.Obs);
    }

    [Fact]
    public void Device_answers_decide_what_is_closed()
    {
        var c = Defaults.Create();
        new QuickSetup { KeepRunning = { "ghub", "razer" } }.ApplyTo(c); // everything else unticked: closed
        Assert.False(c.Apps.Single(a => a.Id == "ghub").Enabled);       // kept
        Assert.False(c.Apps.Single(a => a.Id == "razer").Enabled);      // kept
        Assert.True(c.Apps.Single(a => a.Id == "steelseries").Enabled); // not ticked: closed
        Assert.True(c.Apps.Single(a => a.Id == "onedrive").Enabled);    // not a device: untouched by the questions
        Assert.True(c.QuestionsDone);
        Assert.True(c.FirstRunDone);
    }

    [Fact]
    public void Answers_round_trip_through_From()
    {
        var c = Defaults.Create();
        var answers = new QuickSetup { QueueInBrowser = true, UsesObs = true, PauseNotifications = true, KeepRunning = { "nzxtcam" } };
        answers.ApplyTo(c);
        var back = QuickSetup.From(c);
        Assert.True(back.QueueInBrowser);
        Assert.True(back.UsesObs);
        Assert.True(back.PauseNotifications);
        Assert.Equal(new[] { "nzxtcam" }, back.KeepRunning);
    }

    [Fact]
    public void People_updating_from_v1_see_the_questions_once()
    {
        var dir = Directory.CreateTempSubdirectory("cs2b-qs").FullName;
        AppPaths.OverrideDataDir(dir);
        var path = Path.Combine(dir, "config.json");
        File.WriteAllText(path, """{ "firstRunDone": true, "browsers": "neverClose" }""");
        var c = AppConfig.Load(path);
        Assert.False(c.QuestionsDone);
        Assert.True(c.FirstRunDone);   // so the welcome page is skipped
        Assert.True(c.KeepInTray);     // new option defaults on
    }

    [Fact]
    public void Boost_record_remembers_which_entry_each_app_came_from()
    {
        var dir = Directory.CreateTempSubdirectory("cs2b-id").FullName;
        AppPaths.OverrideDataDir(dir);
        var os = new FakeSystemOps();
        os.Procs.Add(new ProcInfo("OneDrive", 10, 1));
        var j = new BoostEngine(os, Defaults.Create(), sessionId: 1).Run(new BoostOptions
        {
            JournalPath = Path.Combine(dir, "session.json"), GracePeriod = TimeSpan.FromMilliseconds(100),
        });
        Assert.Equal("onedrive", j.Apps.Single().Id);
    }
}
