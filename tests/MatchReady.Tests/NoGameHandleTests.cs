using System.Diagnostics;
using System.Runtime.InteropServices;
using MatchReady.Boost;
using MatchReady.Config;
using MatchReady.Sys;

namespace MatchReady.Tests;

/// <summary>
/// Runtime proof for the FACEIT rule: detecting CS2 and planning a boost must not leave this process
/// holding any handle to the game. A copy of a harmless Windows tool, renamed cs2.exe, stands in for the game.
/// </summary>
[Collection("engine")]
public class NoGameHandleTests
{
    [DllImport("kernel32.dll")]
    static extern int GetProcessId(IntPtr process);

    [DllImport("kernel32.dll")]
    static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr handle);

    [Fact]
    public void Detection_and_dry_run_hold_no_handle_to_cs2()
    {
        var dir = Directory.CreateTempSubdirectory("cs2b-fakegame").FullName;
        AppPaths.OverrideDataDir(Path.Combine(dir, "data"));
        var fake = Path.Combine(dir, "cs2.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "PING.EXE"), fake);

        // Started through cmd so this test itself never holds a handle to the stand-in game.
        using (Process.Start(new ProcessStartInfo("cmd.exe", $"/c start \"\" /min \"{fake}\" -n 60 127.0.0.1")
               { CreateNoWindow = true, UseShellExecute = false })!) { }
        Thread.Sleep(1500);
        var gamePid = new WindowsSystemOps().ListProcesses().Single(p => p.Name == "cs2").Pid;
        try
        {
            var os = new WindowsSystemOps();

            // What auto mode does every 3 seconds, plus a full dry run and a state snapshot.
            for (var i = 0; i < 3; i++) Assert.True(BoostEngine.IsGameRunning(os.ListProcesses()));
            new BoostEngine(os, Defaults.Create()).Run(new BoostOptions { DryRun = true, CloseBrowsers = true, CloseRecorder = true });
            StateSnapshot.Capture(os, Defaults.Create());
            Assert.Throws<MatchReady.Safety.ProtectedTargetException>(() => os.GetProcessPath(new ProcInfo("cs2", gamePid, 1)));
            GC.Collect();
            GC.WaitForPendingFinalizers();

            var holders = OwnHandlesTo(gamePid);
            Assert.True(holders.Count == 0, $"Holding {holders.Count} handle(s) to cs2: {string.Join(",", holders)}");

            // Positive control: the scanner does find a handle when one is really open.
            var control = OpenProcess(0x1000, false, gamePid); // PROCESS_QUERY_LIMITED_INFORMATION
            try { Assert.NotEmpty(OwnHandlesTo(gamePid)); }
            finally { CloseHandle(control); }
        }
        finally
        {
            foreach (var p in Process.GetProcessesByName("cs2"))
            {
                using (p) { if (p.MainModule?.FileName == fake) p.Kill(); }
            }
        }
    }

    /// <summary>Every handle value this process could own; GetProcessId returns 0 for anything that isn't a process handle.</summary>
    static List<long> OwnHandlesTo(int pid)
    {
        var found = new List<long>();
        for (long h = 4; h < (1 << 20); h += 4)
        {
            if (GetProcessId((IntPtr)h) == pid) found.Add(h);
        }
        return found;
    }
}
