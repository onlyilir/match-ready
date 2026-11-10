using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace MatchReady.Installer
{
    /// <summary>Where Match Ready is installed, shared by Setup and Uninstall. Per-user: no admin needed.</summary>
    internal static class InstallInfo
    {
        public const string AppName = "Match Ready";
        public const string Version = "1.2.0";
        public const string AppExe = "MatchReady.exe";
        public const string UninstallExe = "Uninstall.exe";
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MatchReady";

        /// <summary>Every file Setup puts in the install folder. Uninstall removes only these (plus config files).</summary>
        public static readonly string[] Files = { AppExe, UninstallExe, "README.md", "FACEIT-NOTES.md", "LICENSE.txt" };

        static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        public static string InstallDir => Path.Combine(LocalAppData, "Programs", AppName);
        public static string DataDir => Path.Combine(LocalAppData, "MatchReady");

        public static string DesktopShortcut =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk");

        public static string StartMenuShortcut =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk");

        /// <summary>The optional "Start with Windows" shortcut the app itself creates.</summary>
        public static string StartupShortcut =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), AppName + ".lnk");

        public static bool IsInstalled => File.Exists(Path.Combine(InstallDir, AppExe));

        /// <summary>A boost that was never restored leaves this file behind (in the new or the old data folder).</summary>
        public static bool BoostActive =>
            File.Exists(Path.Combine(DataDir, "session.json")) || File.Exists(Path.Combine(Legacy.DataDir, "session.json"));

        /// <summary>True if the app is open (new or old name). Only our own app's names are looked up.</summary>
        public static bool AppRunning()
        {
            foreach (var name in new[] { "MatchReady", "CS2Booster" })
            {
                var procs = Process.GetProcessesByName(name);
                var running = procs.Length > 0;
                foreach (var p in procs) p.Dispose();
                if (running) return true;
            }
            return false;
        }

        public static string InstalledVersion()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(UninstallKey))
                return k?.GetValue("DisplayVersion") as string;
        }

        /// <summary>Safety check before deleting anything: the folder must be exactly the expected folder.</summary>
        public static bool IsFolder(string dir, string expected)
        {
            if (string.IsNullOrWhiteSpace(dir)) return false;
            var a = Path.GetFullPath(dir).TrimEnd('\\');
            var b = Path.GetFullPath(expected).TrimEnd('\\');
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsOurFolder(string dir) => IsFolder(dir, InstallDir);

        public static bool IsKnownFile(string name) =>
            Files.Concat(new[] { "config.json", "config.json.tmp", "config.json.bak" })
                .Any(f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>The app's old name (v1.0-1.1). Setup moves these installs over to Match Ready.</summary>
        public static class Legacy
        {
            public const string AppName = "CS2 Booster";
            public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\CS2Booster";
            public static readonly string[] Files =
                { "CS2Booster.exe", "Uninstall.exe", "README.md", "FACEIT-NOTES.md", "config.json", "config.json.tmp" };

            public static string InstallDir => Path.Combine(LocalAppData, "Programs", AppName);
            public static string DataDir => Path.Combine(LocalAppData, "CS2Booster");
            public static string DesktopShortcut =>
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk");
            public static string StartMenuShortcut =>
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk");

            public static bool IsInstalled => File.Exists(Path.Combine(InstallDir, "CS2Booster.exe"));

            /// <summary>
            /// Keeps the settings, then removes the old install: only its own known files, its shortcuts and its
            /// "Installed apps" entry. The old data folder (log, unfinished boost) is moved by the app on first start.
            /// </summary>
            public static string MigrateTo(string newInstallDir)
            {
                if (!IsInstalled) return null;
                var oldConfig = Path.Combine(InstallDir, "config.json");
                var newConfig = Path.Combine(newInstallDir, "config.json");
                if (File.Exists(oldConfig) && !File.Exists(newConfig)) File.Copy(oldConfig, newConfig);

                foreach (var name in Files)
                {
                    var f = Path.Combine(InstallDir, name);
                    if (File.Exists(f)) File.Delete(f);
                }
                if (Directory.Exists(InstallDir) && Directory.GetFileSystemEntries(InstallDir).Length == 0)
                    Directory.Delete(InstallDir);
                if (File.Exists(DesktopShortcut)) File.Delete(DesktopShortcut);
                if (File.Exists(StartMenuShortcut)) File.Delete(StartMenuShortcut);
                Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
                return "Moved CS2 Booster over to Match Ready (settings kept).";
            }
        }
    }
}
