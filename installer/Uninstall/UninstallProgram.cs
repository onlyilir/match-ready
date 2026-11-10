using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MatchReady.Installer
{
    /// <summary>
    /// Removes Match Ready. If a boost was never restored, it puts everything back first.
    /// A running exe can't delete its own folder, so it re-runs a copy of itself from %TEMP% to finish.
    /// </summary>
    internal static class UninstallProgram
    {
        const string Title = "Uninstall " + InstallInfo.AppName;

        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            try
            {
                if (args.Length >= 2 && args[0] == "--from-temp") return Finish(args[1]);
                return Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Uninstall failed: " + ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        static int Start()
        {
            if (MessageBox.Show("Remove Match Ready from this PC?", Title, MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes) return 0;

            while (InstallInfo.AppRunning())
            {
                if (MessageBox.Show(
                        "Match Ready is still running (maybe in the tray, next to the clock). Right-click its icon, choose Exit, then click Retry.",
                        Title, MessageBoxButtons.RetryCancel, MessageBoxIcon.Information) != DialogResult.Retry) return 0;
            }

            if (InstallInfo.BoostActive && !RestoreFirst()) return 0;

            // Hand over to a copy in %TEMP% so this folder can be deleted.
            var temp = Path.Combine(Path.GetTempPath(), "MatchReady-Uninstall-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".exe");
            File.Copy(Application.ExecutablePath, temp);
            Process.Start(new ProcessStartInfo(temp, "--from-temp " + Process.GetCurrentProcess().Id)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetTempPath(),
            })?.Dispose();
            return 0;
        }

        /// <summary>A boost is still recorded in session.json: let the app put everything back before removing it.</summary>
        static bool RestoreFirst()
        {
            var exe = Path.Combine(InstallInfo.InstallDir, InstallInfo.AppExe);
            var ok = MessageBox.Show(
                "A boost was never restored (services or settings may still be paused).\r\n\r\n" +
                "Put everything back now? Windows will ask for permission.",
                Title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
            if (!ok || !File.Exists(exe))
            {
                MessageBox.Show("Uninstall stopped. Open Match Ready (it puts everything back on start), then try again. " +
                                "A reboot also brings paused services back.", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            try
            {
                using (var p = Process.Start(new ProcessStartInfo(exe, "--restore")
                       { UseShellExecute = true, Verb = "runas", WorkingDirectory = InstallInfo.InstallDir }))
                {
                    p?.WaitForExit(120000);
                }
            }
            catch (Win32Exception)
            {
                MessageBox.Show("Uninstall stopped: permission was not given, so nothing was put back yet.",
                    Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            if (InstallInfo.BoostActive)
            {
                MessageBox.Show("Could not finish putting everything back. Open Match Ready, press RESTORE EVERYTHING, then try again.",
                    Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            return true;
        }

        static int Finish(string parentPid)
        {
            // Wait for the first copy to exit so its exe is no longer locked.
            if (int.TryParse(parentPid, out var pid))
            {
                try
                {
                    using (var parent = Process.GetProcessById(pid)) parent.WaitForExit(15000);
                }
                catch (ArgumentException) { /* already gone */ }
            }

            var dir = InstallInfo.InstallDir;
            if (!InstallInfo.IsOurFolder(dir)) throw new InvalidOperationException("Unexpected install folder: " + dir);

            DeleteFile(InstallInfo.DesktopShortcut);
            DeleteFile(InstallInfo.StartMenuShortcut);
            DeleteFile(InstallInfo.StartupShortcut); // "Start with Windows", if it was turned on

            if (Directory.Exists(dir))
            {
                // Only our own files. Anything else put in the folder is left alone.
                foreach (var f in Directory.GetFiles(dir))
                {
                    if (InstallInfo.IsKnownFile(Path.GetFileName(f))) DeleteFile(f);
                }
                if (Directory.GetFileSystemEntries(dir).Length == 0) Directory.Delete(dir);
            }

            Registry.CurrentUser.DeleteSubKeyTree(InstallInfo.UninstallKey, false);

            if (Directory.Exists(InstallInfo.DataDir) && !InstallInfo.BoostActive &&
                MessageBox.Show("Also delete Match Ready's log and boost history?", Title, MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
            {
                Directory.Delete(InstallInfo.DataDir, true);
            }

            MessageBox.Show("Match Ready was removed.", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        static void DeleteFile(string path)
        {
            for (var i = 0; i < 10 && File.Exists(path); i++)
            {
                try { File.Delete(path); }
                catch (IOException) { Thread.Sleep(500); }
                catch (UnauthorizedAccessException) { Thread.Sleep(500); }
            }
        }
    }
}
