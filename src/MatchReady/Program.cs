using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using MatchReady.Restore;
using MatchReady.Sys;
using MatchReady.UI;

namespace MatchReady;

static class Program
{
    const string MutexName = @"Local\MatchReady.SingleInstance";

    [STAThread]
    static int Main(string[] args)
    {
        var dataDir = Cli.Arg(args, "--data-dir");
        if (dataDir != null) AppPaths.OverrideDataDir(Path.GetFullPath(dataDir));

        // After "Restart Match Ready" on the crash window or an update: wait for the old copy to finish closing.
        if (int.TryParse(Cli.Arg(args, "--wait-pid"), out var waitPid)) WaitForExit(waitPid);

        // Already running (maybe hidden in the tray)? Bring that window back instead of asking for admin again.
        if (!Cli.IsCliMode(args) && AlreadyRunning())
        {
            ShowRunningInstance();
            return 0;
        }

        // Pausing Windows services needs admin. Ask once through the normal Windows (UAC) prompt.
        if (Cli.NeedsAdmin(args) && !WindowsInfo.IsAdmin())
            return RelaunchAsAdmin(args);

        // First start after the rename from CS2 Booster: bring over its data (and any unfinished boost).
        if (dataDir == null) AppPaths.MigrateLegacyData();

        if (Cli.IsCliMode(args)) return Cli.Run(args);

        using var single = new Mutex(true, MutexName, out var first);
        if (!first)
        {
            ShowRunningInstance();
            return 0;
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        ThemeManager.Apply(app);
        CrashGuard.Install(app);

        // --tray: started by the optional "Start with Windows" shortcut, so open quietly in the tray.
        var window = new MainWindow(startInTray: args.Contains("--tray"));
        if (!args.Contains("--tray") || !window.Config.QuestionsDone) window.Show();
        app.Run();
        return 0;
    }

    /// <summary>
    /// The running copy is admin, this one may not be. If the mutex exists but we can't open it,
    /// that still means it's running.
    /// </summary>
    static bool AlreadyRunning()
    {
        try
        {
            if (!Mutex.TryOpenExisting(MutexName, out var m)) return false;
            m.Dispose();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    static void WaitForExit(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            p.WaitForExit(15000);
        }
        catch (ArgumentException) { /* already gone */ }
        catch (InvalidOperationException) { }
    }

    static void ShowRunningInstance() =>
        NativeMethods.PostMessage(NativeMethods.HWND_BROADCAST, MainWindow.ShowMessage, IntPtr.Zero, IntPtr.Zero);

    static int RelaunchAsAdmin(string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)),
            };
            Process.Start(psi)?.Dispose();
            return 0;
        }
        catch (Win32Exception)
        {
            if (args.Contains("--tray")) return 1; // "No" at sign-in: stay quiet
            var app = new Application();
            ThemeManager.Apply(app);
            Ui.Tell(null, "Match Ready", "Match Ready needs admin rights to pause Windows services. Nothing was changed.");
            return 1;
        }
    }
}

/// <summary>
/// Last line of defence: any unexpected error puts a boost back first, saves a report (without personal details),
/// and shows a calm "Something went wrong" window instead of a silent crash.
/// </summary>
static class CrashGuard
{
    static int _handling;

    public static void Install(Application app)
    {
        app.DispatcherUnhandledException += (_, e) =>
        {
            e.Handled = true;
            Handle(e.Exception, showWindow: true);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Handle(ex, showWindow: false); // the process is ending: no window
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Write("Background task error (ignored): " + e.Exception.GetBaseException().Message);
            e.SetObserved();
        };
    }

    static void Handle(Exception ex, bool showWindow)
    {
        if (Interlocked.Exchange(ref _handling, 1) == 1) return;
        var restored = false;
        try
        {
            Log.Write("Unexpected error: " + ex);
            var j = SessionJournal.TryLoad(AppPaths.SessionFile);
            if (j != null)
            {
                new RestoreEngine(new WindowsSystemOps()).Restore(j, reopenApps: false);
                restored = true;
            }
        }
        catch (Exception restoreError)
        {
            Log.Write("Restore after the error failed (it runs again on next start): " + restoreError.Message);
        }

        string report, path;
        try
        {
            report = CrashReport.Build(ex, restored ? "A boost was on; it was put back." : "No boost was on.");
            path = CrashReport.Save(report);
        }
        catch
        {
            report = ex.ToString();
            path = "(could not be saved)";
        }

        if (!showWindow) return;
        try
        {
            foreach (Window w in Application.Current.Windows) w.Hide();
            var crash = new CrashWindow(ex, report, path, restored, new WindowsSystemOps());
            crash.ShowDialog();
            if (crash.Restart)
                Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"--wait-pid {Environment.ProcessId}") { UseShellExecute = false })?.Dispose();
        }
        finally
        {
            Application.Current.Shutdown();
        }
    }
}
