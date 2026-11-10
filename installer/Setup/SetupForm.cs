using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MatchReady.Installer
{
    /// <summary>
    /// One-screen installer. Copies Match Ready into the user's own Programs folder, adds shortcuts and an
    /// "Installed apps" entry. No admin, no services, no startup entries, nothing outside that folder.
    /// </summary>
    internal sealed class SetupForm : Form
    {
        readonly CheckBox _desktop, _startMenu, _launch;
        readonly Button _install, _cancel;
        readonly Label _status;
        bool _done;

        public SetupForm()
        {
            Ui.Apply(this, InstallInfo.AppName + " Setup", 480, 360);

            var title = Ui.Label(InstallInfo.AppName, 24, 18, 300, 34, null, 17);
            title.Font = Ui.Font(17, FontStyle.Bold);
            var ver = Ui.Label("Setup  ·  version " + InstallInfo.Version, 26, 54, 300, 22, Ui.Muted, 9.5f);

            var installed = InstallInfo.InstalledVersion();
            var intro = Ui.Label(
                (installed != null ? $"Version {installed} is already installed. This will update it and keep your settings.\r\n\r\n"
                 : InstallInfo.Legacy.IsInstalled ? "CS2 Booster (the old name) is installed. It will be replaced by Match Ready, and your settings are kept.\r\n\r\n"
                 : "") +
                "Installs Match Ready for you only. No admin needed to install.\r\n" +
                "Folder:  " + InstallInfo.InstallDir,
                24, 88, 432, 86, Ui.Text, 9.5f);

            _desktop = Check("Desktop shortcut", 182);
            _startMenu = Check("Start menu shortcut", 210);
            _launch = Check("Open Match Ready when done", 238);

            _status = Ui.Label("", 24, 270, 432, 22, Ui.Muted, 9.5f);

            _install = Ui.Button(installed != null || InstallInfo.Legacy.IsInstalled ? "Update" : "Install", Ui.Green, 326, 302);
            _install.Font = Ui.Font(11, FontStyle.Bold);
            _install.Click += async (s, e) => await OnInstall();
            _cancel = Ui.Button("Cancel", Ui.Panel, 184, 302);
            _cancel.Click += (s, e) => Close();
            AcceptButton = _install;
            CancelButton = _cancel;

            Controls.AddRange(new Control[] { title, ver, intro, _desktop, _startMenu, _launch, _status, _install, _cancel });
        }

        CheckBox Check(string text, int y) =>
            new CheckBox { Text = text, Checked = true, AutoSize = true, Location = new Point(24, y) };

        async Task OnInstall()
        {
            if (_done)
            {
                Close();
                return;
            }

            while (InstallInfo.AppRunning())
            {
                var retry = MessageBox.Show(this,
                    "Match Ready is still running (maybe in the tray, next to the clock). Right-click its icon, choose Exit, then click Retry.",
                    "Close Match Ready first", MessageBoxButtons.RetryCancel, MessageBoxIcon.Information);
                if (retry != DialogResult.Retry) return;
            }

            _install.Enabled = _cancel.Enabled = false;
            _desktop.Enabled = _startMenu.Enabled = _launch.Enabled = false;
            bool desktop = _desktop.Checked, startMenu = _startMenu.Checked;

            try
            {
                _status.Text = "Installing…";
                await Task.Run(() => Install(desktop, startMenu));
            }
            catch (Exception ex)
            {
                _status.ForeColor = Ui.Red;
                _status.Text = "Install failed: " + ex.Message;
                _install.Enabled = _cancel.Enabled = true;
                return;
            }

            _done = true;
            _status.ForeColor = Ui.Green;
            _status.Text = "Installed. Uninstall any time from Settings > Apps.";
            _install.Text = "Finish";
            _install.Enabled = true;
            _cancel.Visible = false;

            if (_launch.Checked)
            {
                // The app asks Windows for admin itself (one "Yes" on the UAC prompt).
                try
                {
                    Process.Start(new ProcessStartInfo(Path.Combine(InstallInfo.InstallDir, InstallInfo.AppExe))
                    {
                        UseShellExecute = true,
                        WorkingDirectory = InstallInfo.InstallDir,
                    })?.Dispose();
                    Close();
                }
                catch (Exception ex)
                {
                    _status.Text = "Installed, but could not open it: " + ex.Message;
                }
            }
        }

        static void Install(bool desktop, bool startMenu)
        {
            var dir = InstallInfo.InstallDir;
            Directory.CreateDirectory(dir);

            // config.json (the user's settings) is never shipped, so an update never overwrites it.
            foreach (var name in InstallInfo.Files) Extract(name, Path.Combine(dir, name));

            // Old name (CS2 Booster): keep its settings, then remove the old install so only one app remains.
            InstallInfo.Legacy.MigrateTo(dir);

            var exe = Path.Combine(dir, InstallInfo.AppExe);
            if (desktop) ShellLink.Create(InstallInfo.DesktopShortcut, exe, dir, "Get your PC match ready (FACEIT-safe)");
            else if (File.Exists(InstallInfo.DesktopShortcut)) File.Delete(InstallInfo.DesktopShortcut);
            if (startMenu) ShellLink.Create(InstallInfo.StartMenuShortcut, exe, dir, "Get your PC match ready (FACEIT-safe)");
            else if (File.Exists(InstallInfo.StartMenuShortcut)) File.Delete(InstallInfo.StartMenuShortcut);

            using (var k = Registry.CurrentUser.CreateSubKey(InstallInfo.UninstallKey))
            {
                var uninstall = Path.Combine(dir, InstallInfo.UninstallExe);
                k.SetValue("DisplayName", InstallInfo.AppName);
                k.SetValue("DisplayVersion", InstallInfo.Version);
                k.SetValue("Publisher", InstallInfo.AppName);
                k.SetValue("DisplayIcon", exe + ",0");
                k.SetValue("InstallLocation", dir);
                k.SetValue("UninstallString", "\"" + uninstall + "\"");
                k.SetValue("QuietUninstallString", "\"" + uninstall + "\"");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)(DirSize(dir) / 1024), RegistryValueKind.DWord);
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            }
        }

        /// <summary>Writes one embedded file. Goes through a temp file so a failed copy never leaves a broken exe.</summary>
        static void Extract(string name, string target)
        {
            using (var src = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload/" + name))
            {
                if (src == null) throw new InvalidOperationException("Setup is damaged: " + name + " is missing. Download it again.");
                var tmp = target + ".new";
                using (var dst = File.Create(tmp)) src.CopyTo(dst);
                if (File.Exists(target)) File.Delete(target);
                File.Move(tmp, target);
            }
        }

        static long DirSize(string dir)
        {
            long total = 0;
            foreach (var f in Directory.GetFiles(dir)) total += new FileInfo(f).Length;
            return total;
        }
    }

    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm());
        }
    }
}
