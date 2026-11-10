using System.ComponentModel;
using System.Diagnostics;
using MatchReady.Sys;
using MatchReady.UI;

namespace MatchReady;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        var dataDir = Cli.Arg(args, "--data-dir");
        if (dataDir != null) AppPaths.OverrideDataDir(Path.GetFullPath(dataDir));

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

        ApplicationConfiguration.Initialize();
        // --tray: started by the optional "Start with Windows" shortcut, so open quietly in the tray.
        Application.Run(new MainForm(startInTray: args.Contains("--tray")));
        return 0;
    }

    const string MutexName = @"Local\MatchReady.SingleInstance";

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

    static void ShowRunningInstance() =>
        NativeMethods.PostMessage(NativeMethods.HWND_BROADCAST, MainForm.ShowMessage, IntPtr.Zero, IntPtr.Zero);

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
            ApplicationConfiguration.Initialize();
            MessageBox.Show(
                "Match Ready needs admin rights to pause Windows services. Nothing was changed.",
                "Match Ready", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 1;
        }
    }
}
