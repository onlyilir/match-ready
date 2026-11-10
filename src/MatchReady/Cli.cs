using System.Text.Json;
using MatchReady.Boost;
using MatchReady.Config;
using MatchReady.Restore;
using MatchReady.Sys;

namespace MatchReady;

/// <summary>
/// Command-line modes:
///   --dry-run          print what a boost would do, change nothing (no admin needed)
///   --state            print the current state (services, power plan, settings) as JSON (no admin needed)
///   --boost            boost without the window (admin, for testing)
///   --restore          restore from session.json without the window (admin, for testing)
///   --self-check       boost services and settings (no apps), restore, and prove everything came back (admin)
///   --out FILE         also write the output to FILE
///   --data-dir DIR     use DIR instead of %LOCALAPPDATA%\MatchReady (for testing)
/// </summary>
public static class Cli
{
    public static bool IsCliMode(string[] args) =>
        args.Any(a => a is "--dry-run" or "--state" or "--boost" or "--restore" or "--self-check");

    public static bool NeedsAdmin(string[] args) =>
        !args.Any(a => a is "--dry-run" or "--state");

    public static int Run(string[] args)
    {
        NativeMethods.AttachConsole(-1); // ATTACH_PARENT_PROCESS: print into the terminal that started us
        var outFile = Arg(args, "--out");
        var lines = new List<string>();
        void Print(string s)
        {
            Console.WriteLine(s);
            lines.Add(s);
        }

        try
        {
            var os = new WindowsSystemOps();
            var cfg = AppConfig.Load(AppPaths.ConfigFile);

            if (args.Contains("--state"))
            {
                Print(JsonSerializer.Serialize(StateSnapshot.Capture(os, cfg), new JsonSerializerOptions { WriteIndented = true }));
            }
            else if (args.Contains("--dry-run"))
            {
                Print($"Match Ready dry run ({DateTime.Now:yyyy-MM-dd HH:mm}). Windows build {WindowsInfo.BuildText()}" +
                      (WindowsInfo.IsInsiderBuild() ? " (Insider preview)." : "."));
                Print("Browsers and OBS are listed as if you said yes; the real boost asks first.");
                Log.Echo = Print;
                new BoostEngine(os, cfg).Run(new BoostOptions
                {
                    DryRun = true,
                    CloseBrowsers = cfg.Browsers != AskMode.NeverClose,
                    CloseRecorder = cfg.Obs != AskMode.NeverClose,
                });
            }
            else if (args.Contains("--boost"))
            {
                if (SessionJournal.TryLoad(AppPaths.SessionFile) != null)
                {
                    Print("A boost is already active (session.json exists). Run --restore first.");
                    return 2;
                }
                Log.Echo = Print;
                new BoostEngine(os, cfg).Run(new BoostOptions
                {
                    CloseBrowsers = cfg.Browsers == AskMode.AlwaysClose,
                    CloseRecorder = cfg.Obs == AskMode.AlwaysClose,
                });
            }
            else if (args.Contains("--self-check"))
            {
                var r = SelfCheck.Run(os, cfg, Print);
                Print("Report saved: " + r.ReportPath);
                return r.Passed ? 0 : 3;
            }
            else if (args.Contains("--restore"))
            {
                var j = SessionJournal.TryLoad(AppPaths.SessionFile);
                if (j == null)
                {
                    Print("Nothing to restore (no session.json).");
                    return 0;
                }
                Log.Echo = Print;
                new RestoreEngine(os).Restore(j, reopenApps: cfg.ReopenAppsOnRestore);
            }
            return 0;
        }
        catch (Exception ex)
        {
            Print("Error: " + ex);
            return 1;
        }
        finally
        {
            Log.Echo = null;
            if (outFile != null) File.WriteAllLines(outFile, lines);
        }
    }

    public static string? Arg(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
