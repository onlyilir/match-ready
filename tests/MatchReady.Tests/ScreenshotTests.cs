using System.Reflection;
using MatchReady.Config;
using MatchReady.Restore;
using MatchReady.Sys;
using MatchReady.UI;

namespace MatchReady.Tests;

/// <summary>
/// Renders every window to PNG files for a visual check, without admin rights and without changing anything.
/// Only runs when MR_SCREENSHOTS is set to an output folder.
/// </summary>
[Collection("engine")]
public class ScreenshotTests
{
    [Fact]
    public void Render_windows()
    {
        var outDir = Environment.GetEnvironmentVariable("MR_SCREENSHOTS");
        if (string.IsNullOrEmpty(outDir)) return;
        Directory.CreateDirectory(outDir);
        AppPaths.OverrideDataDir(Path.Combine(outDir, "data"));
        var cfgFile = AppPaths.ConfigFile;
        if (File.Exists(cfgFile)) File.Delete(cfgFile);

        Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                Application.EnableVisualStyles();
                using var f = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(-3000, 0) };
                f.Show();
                Pump();
                Save(Field<Control>(f, "_firstRun"), outDir, "01-welcome.png"); // the panel itself: DrawToBitmap mixes up layer order

                Click(Field<Control>(f, "_firstRun"), "Next");
                Pump();
                Save(Field<Control>(f, "_firstRun"), outDir, "02-quick-setup.png");

                // Close the first-run panel without saving anything.
                var fr = Field<Control>(f, "_firstRun");
                f.Controls.Remove(fr);
                fr.Dispose();
                typeof(MainForm).GetField("_firstRun", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(f, null);
                Pump();
                Save(f, outDir, "03-ready.png");

                var j = SampleJournal();
                typeof(MainForm).GetField("_active", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(f, j);
                Invoke(f, "UpdateUi");
                Invoke(f, "SetActiveStatus");
                Pump();
                Save(f, outDir, "04-boost-active.png");

                Invoke(f, "ShowTab", true);
                Pump();
                Save(f, outDir, "05-pro.png");

                var cfg = Defaults.Create();
                var history = new List<SessionJournal> { SampleJournal(finished: true) };
                Render(new StoppedForm(j, history, cfg, () => { }), outDir, "06-what-was-stopped.png");
                Render(new SettingsForm(cfg, new WindowsSystemOps()), outDir, "07-settings.png");
                var os = new WindowsSystemOps();
                Render(new HelpForm(os, cfg, PcInfo.Detect(os), () => null, _ => { }, _ => { }), outDir, "08-help.png");

                typeof(MainForm).GetField("_active", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(f, null);
                typeof(MainForm).GetField("_exiting", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(f, true);
                f.Close();
            }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) throw error;
    }

    static SessionJournal SampleJournal(bool finished = false)
    {
        var j = new SessionJournal
        {
            State = finished ? SessionState.Restored : SessionState.Active,
            StartedUtc = DateTime.UtcNow.AddMinutes(finished ? -200 : -3),
            EndedUtc = finished ? DateTime.UtcNow.AddMinutes(-128) : null,
            RamFreedBytes = 6_800_000_000,
        };
        j.Apps.Add(new AppChange { Id = "ollama", Label = "Ollama (AI models)", Closed = true, How = "closed" });
        j.Apps.Add(new AppChange { Id = "wallpaperengine", Label = "Wallpaper Engine", Closed = true, How = "closed" });
        j.Apps.Add(new AppChange { Id = "onedrive", Label = "OneDrive", Closed = true, How = "force-closed" });
        j.Apps.Add(new AppChange { Id = "ghub", Label = "Logitech G HUB", Closed = true, How = "closed" });
        j.Apps.Add(new AppChange { Id = "gigabyte", Label = "Gigabyte Control Center / RGB", Closed = true, How = "closed" });
        foreach (var (n, l) in new[] { ("wuauserv", "Windows Update"), ("WSearch", "Search indexing"), ("DiagTrack", "Telemetry (Connected User Experiences)") })
            j.Services.Add(new ServiceChange { Name = n, Label = l, WasRunning = true, Stopped = true });
        j.Power = new PowerChange { OriginalName = "Balanced", AppliedName = "High performance", Done = true };
        j.Registry.Add(new RegistryChange { Label = "Windows Game Mode on", Applied = true });
        j.Skipped.Add("Windows Update Medic: Access is denied.");
        return j;
    }

    static void Render(Form form, string dir, string name)
    {
        using (form)
        {
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-3000, 0);
            form.Show();
            Pump();
            Save(form, dir, name);
            form.Close();
        }
    }

    static void Pump()
    {
        for (var i = 0; i < 5; i++) Application.DoEvents();
    }

    static void Save(Control f, string dir, string name)
    {
        using var bmp = new Bitmap(f.Width, f.Height);
        f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
        bmp.Save(Path.Combine(dir, name));
    }

    static void Click(Control root, string buttonText)
    {
        foreach (Control c in root.Controls)
        {
            if (c is Button b && b.Text == buttonText) { b.PerformClick(); return; }
            Click(c, buttonText);
        }
    }

    static T Field<T>(object o, string name) =>
        (T)o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(o)!;

    static void Invoke(object o, string name, params object[] args) =>
        o.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(o, args);
}
