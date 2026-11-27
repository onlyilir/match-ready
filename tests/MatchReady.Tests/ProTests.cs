using MatchReady.Sys;
using MatchReady.UI;

namespace MatchReady.Tests;

/// <summary>v1.3: updates with checksum, release notes, live meter, crash report privacy.</summary>
[Collection("engine")]
public class ProTests
{
    const string ReleaseJson = """
    {
      "tag_name": "v1.3.0",
      "html_url": "https://github.com/onlyilir/match-ready/releases/tag/v1.3.0",
      "body": "Intro\n\n## Changes\n\nNew\n- Native notifications\n- One-click updates\n",
      "assets": [
        { "name": "MatchReady-Setup.exe", "browser_download_url": "https://example.test/setup.exe" },
        { "name": "MatchReady.exe", "browser_download_url": "https://example.test/portable.exe" },
        { "name": "SHA256SUMS.txt", "browser_download_url": "https://example.test/sums.txt" }
      ]
    }
    """;

    [Fact]
    public void Release_data_is_read_with_its_downloads()
    {
        var r = UpdateChecker.Parse(ReleaseJson, new Version(1, 2, 0));
        Assert.True(r.Ok);
        Assert.True(r.NewerAvailable);
        Assert.Equal("1.3.0", r.Latest);
        Assert.Equal("https://example.test/setup.exe", r.SetupUrl);
        Assert.Equal("https://example.test/sums.txt", r.SumsUrl);
        Assert.False(UpdateChecker.Parse(ReleaseJson, new Version(1, 3, 0)).NewerAvailable);
    }

    [Fact]
    public void Checksum_list_is_read_strictly()
    {
        var hash = new string('a', 64);
        var sums = $"{hash}  MatchReady-Setup.exe\n{new string('b', 64)}  MatchReady.exe\n";
        Assert.Equal(hash.ToUpperInvariant(), UpdateChecker.HashFor(sums, "MatchReady-Setup.exe"));
        Assert.Null(UpdateChecker.HashFor(sums, "Other.exe"));
        Assert.Null(UpdateChecker.HashFor("short  MatchReady-Setup.exe", "MatchReady-Setup.exe")); // not a SHA256
        Assert.Equal(hash.ToUpperInvariant(), UpdateChecker.HashFor($"{hash} *MatchReady-Setup.exe", "MatchReady-Setup.exe"));
    }

    [Fact]
    public async Task A_release_without_a_checksum_list_is_never_installed()
    {
        var r = new UpdateChecker.Result(true, true, "9.9.9", "https://example.test", "", SetupUrl: "https://example.test/setup.exe", SumsUrl: null);
        var (path, message) = await UpdateChecker.DownloadAsync(r, new Version(1, 0, 0), new Progress<double>(), CancellationToken.None);
        Assert.Null(path);
        Assert.Contains("download page", message);
    }

    [Fact]
    public void Whats_new_reads_this_versions_section()
    {
        var log = "# Changelog\n\n## 1.3.0 (2026-11-26)\n\nNew\n- **Bold** thing\n- `code` thing\n\n## 1.2.0 (2026-11-10)\n- old\n";
        var lines = ReleaseNotes.Section(log, "1.3.0");
        Assert.Equal(new[] { "New", "- Bold thing", "- code thing" }, lines);
        Assert.Equal(new[] { "- old" }, ReleaseNotes.Section(log, "1.2.0"));
        Assert.Empty(ReleaseNotes.Section(log, "9.9.9"));
    }

    [Fact]
    public void The_embedded_changelog_has_a_section_for_this_version()
    {
        var text = AboutWindow.Embedded("CHANGELOG.md");
        var v = typeof(Ui).Assembly.GetName().Version!;
        Assert.NotEmpty(ReleaseNotes.Section(text, v.ToString(3)));
        Assert.Contains("MIT License", AboutWindow.Embedded("LICENSE"));
        Assert.Contains("CsWinRT", AboutWindow.Embedded("THIRD-PARTY-NOTICES.md"));
    }

    [Fact]
    public void App_Setup_and_Uninstall_share_one_version_number()
    {
        // 1.3.0 shipped a Setup that still said 1.2.0 (its own hard-coded copy). Keep one source.
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Directory.Build.props"))) root = root.Parent;
        Assert.NotNull(root);

        var props = File.ReadAllText(Path.Combine(root!.FullName, "Directory.Build.props"));
        var m = System.Text.RegularExpressions.Regex.Match(props, @"<Version>(\d+\.\d+\.\d+)</Version>");
        Assert.True(m.Success, "Directory.Build.props needs <Version>x.y.z</Version>");
        Assert.Equal(m.Groups[1].Value, typeof(Ui).Assembly.GetName().Version!.ToString(3));

        foreach (var dir in new[] { "src", "installer" })
            foreach (var proj in Directory.GetFiles(Path.Combine(root.FullName, dir), "*.csproj", SearchOption.AllDirectories))
                Assert.DoesNotContain("<Version>", File.ReadAllText(proj));

        var info = File.ReadAllText(Path.Combine(root.FullName, "installer", "Shared", "InstallInfo.cs"));
        Assert.DoesNotMatch(@"Version\s*=\s*""\d", info);
    }

    [Fact]
    public void Cpu_use_is_computed_from_system_times()
    {
        // 1000 ticks passed (kernel 600 incl. 750 idle? no: kernel includes idle). Idle 750 of 1000 -> 25% busy.
        Assert.Equal(25, SystemMeter.Compute(0, 0, 0, 750, 600, 400)!.Value, 3);
        Assert.Null(SystemMeter.Compute(5, 5, 5, 5, 5, 5));
        Assert.Equal(0, SystemMeter.Compute(0, 0, 0, 1000, 1000, 0)!.Value, 3);
    }

    [Fact]
    public void Live_meter_runs_without_opening_any_process()
    {
        var m = new SystemMeter();
        Assert.Null(m.CpuPercent()); // first sample primes
        Thread.Sleep(100);
        var cpu = m.CpuPercent();
        Assert.NotNull(cpu);
        Assert.InRange(cpu!.Value, 0, 100);
        Assert.True(SystemMeter.FreeRamBytes() > 0);
    }

    [Fact]
    public void Crash_reports_leave_out_the_user_name_PC_name_and_profile_path()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var text = $@"at {profile}\Desktop\x.cs line 5, user {Environment.UserName} on {Environment.MachineName}";
        var clean = CrashReport.Scrub(text);
        Assert.DoesNotContain(profile, clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.MachineName, clean, StringComparison.OrdinalIgnoreCase);
        if (Environment.UserName.Length > 2)
            Assert.DoesNotContain(Environment.UserName, clean, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("%USERPROFILE%", clean);
    }

    [Fact]
    public void Crash_issue_link_is_prefilled_and_private()
    {
        Exception ex;
        try { throw new InvalidOperationException($"failed in {Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}"); }
        catch (Exception e) { ex = e; }
        var url = CrashReport.IssueUrl(ex);
        Assert.StartsWith("https://github.com/onlyilir/match-ready/issues/new?template=bug_report.yml", url);
        Assert.Contains("version=", url);
        Assert.DoesNotContain(Uri.EscapeDataString(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)), url);
        Assert.True(url.Length < 4000);
    }

    [Fact]
    public void Clock_reads_like_a_timer()
    {
        Assert.Equal("0:07", Ui.Clock(TimeSpan.FromSeconds(7)));
        Assert.Equal("12:05", Ui.Clock(TimeSpan.FromSeconds(725)));
        Assert.Equal("1:02:03", Ui.Clock(new TimeSpan(1, 2, 3)));
    }
}
