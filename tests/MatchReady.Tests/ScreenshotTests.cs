using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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
        if (File.Exists(AppPaths.ConfigFile)) File.Delete(AppPaths.ConfigFile);

        Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                ThemeManager.Apply(app);

                var main = new MainWindow();
                Offscreen(main);
                main.Show();
                Pump();
                Save(main, outDir, "01-welcome.png");

                var overlay = (FirstRunView)Field<System.Windows.Controls.ContentControl>(main, "Overlay").Content;
                Invoke(overlay, "ShowQuestions");
                Pump();
                Save(main, outDir, "02-quick-setup.png");

                // Close the quick setup without saving anything.
                Field<System.Windows.Controls.ContentControl>(main, "Overlay").Visibility = Visibility.Collapsed;
                Field<FrameworkElement>(main, "Shell").Visibility = Visibility.Visible;
                SetField(main, "_firstRun", null);
                Pump();
                Save(main, outDir, "03-ready.png");

                var j = SampleJournal();
                SetField(main, "_active", j);
                SetField(main, "_ramAtBoost", SystemMeter.FreeRamBytes() - 6_800_000_000);
                Invoke(main, "UpdateUi");
                Invoke(main, "SetActiveStatus");
                Invoke(main, "StartDashboard");
                var cpu = Field<List<double>>(main, "_cpuHistory");
                var rnd = new Random(3);
                for (var i = 0; i < 59; i++) cpu.Add(18 + 14 * Math.Sin(i / 6.0) + rnd.Next(0, 8));
                Invoke(main, "DrawCpu");
                Pump();
                Save(main, outDir, "04-boost-active.png");

                Field<System.Windows.Controls.RadioButton>(main, "TabPro").IsChecked = true;
                Pump();
                Save(main, outDir, "05-pro.png");
                Field<System.Windows.Controls.RadioButton>(main, "TabBoost").IsChecked = true;

                var cfg = Defaults.Create();
                var os = new WindowsSystemOps();
                Render(new HistoryWindow(main, j, new List<SessionJournal> { SampleJournal(finished: true) }, cfg, () => { }), outDir, "06-what-was-stopped.png");
                Render(new SettingsWindow(main, cfg, os), outDir, "07-settings.png");
                Render(new HelpWindow(main, os, cfg, PcInfo.Detect(os), () => null, _ => { }), outDir, "08-help.png");
                Render(new AboutWindow(main, os), outDir, "09-about.png");
                Render(new WhatsNewWindow(main), outDir, "10-whats-new.png");
                Exception sample;
                try { throw new InvalidOperationException("Example error for the screenshot"); }
                catch (Exception e) { sample = e; }
                Render(new CrashWindow(sample, "report", @"C:\Users\Example\AppData\Local\MatchReady\crash.txt", true, os), outDir, "11-crash.png");
                var update = new UpdateChecker.Result(true, true, "1.3.0", "https://example.test", "", "https://example.test/s.exe", "https://example.test/sums",
                    "## Changes\nNew\n- Native Windows notifications\n- One-click updates checked with SHA256\n- New look\n");
                Render(new UpdateWindow(main, update, os), outDir, "12-update.png");
                Render(new AskSample(main), outDir, "13-question.png");

                // High contrast: the same main window drawn with Windows' system colors.
                ThemeManager.ForceHighContrast = true;
                ThemeManager.ApplyContrast(app);
                Pump();
                Save(main, outDir, "14-high-contrast.png");
                ThemeManager.ForceHighContrast = false;
                ThemeManager.ApplyContrast(app);

                SetField(main, "_active", null);
                SetField(main, "_exiting", true);
                main.Close();
            }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw(); // keep the real stack
    }

    /// <summary>Same look as Ui.Ask, without blocking on ShowDialog.</summary>
    sealed class AskSample : Window
    {
        public AskSample(Window owner)
        {
            Width = 440;
            SizeToContent = SizeToContent.Height;
            Ui.Prepare(this, "Close your browsers?", owner);
            var (root, content, bar) = Ui.Page();
            content.Children.Add(new System.Windows.Controls.TextBlock { Text = "Close your browsers? Unsaved work may be lost.", Style = Ui.S("Body"), FontSize = 15 });
            Ui.AddRight(bar, Ui.Button("Close browsers"), Ui.Button("Skip browsers", "PrimaryButton"));
            Content = root;
        }
    }

    static SessionJournal SampleJournal(bool finished = false)
    {
        var j = new SessionJournal
        {
            State = finished ? SessionState.Restored : SessionState.Active,
            StartedUtc = DateTime.UtcNow.AddMinutes(finished ? -200 : -14).AddSeconds(-23),
            EndedUtc = finished ? DateTime.UtcNow.AddMinutes(-128) : null,
            RamFreedBytes = 6_800_000_000,
        };
        j.Apps.Add(new AppChange { Id = "ollama", Label = "Ollama (AI models)", Closed = true, How = "closed" });
        j.Apps.Add(new AppChange { Id = "wallpaperengine", Label = "Wallpaper Engine", Closed = true, How = "closed" });
        j.Apps.Add(new AppChange { Id = "onedrive", Label = "OneDrive", Closed = true, How = "force-closed" });
        j.Apps.Add(new AppChange { Id = "epic", Label = "Epic Games Launcher", Closed = true, How = "closed" });
        j.Apps.Add(new AppChange { Id = "ghub", Label = "Logitech G HUB", Closed = true, How = "closed" });
        foreach (var (n, l) in new[] { ("wuauserv", "Windows Update"), ("WSearch", "Search indexing"), ("DiagTrack", "Telemetry") })
            j.Services.Add(new ServiceChange { Name = n, Label = l, WasRunning = true, Stopped = true });
        j.Power = new PowerChange { OriginalName = "Balanced", AppliedName = "High performance", Done = true };
        j.Registry.Add(new RegistryChange { Label = "Windows Game Mode on", Applied = true });
        j.Skipped.Add("Windows Update Medic: Access is denied.");
        return j;
    }

    static void Offscreen(Window w)
    {
        w.WindowStartupLocation = WindowStartupLocation.Manual;
        w.Left = -10000;
        w.Top = 0;
        w.ShowActivated = false;
    }

    static void Render(Window w, string dir, string name)
    {
        Offscreen(w);
        w.Show();
        Pump();
        Save(w, dir, name);
        w.Close();
    }

    static void Pump()
    {
        for (var i = 0; i < 4; i++)
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
    }

    /// <summary>Renders the window's content (the title bar is drawn by Windows, so it isn't included).</summary>
    static void Save(Window w, string dir, string name)
    {
        var content = (FrameworkElement)w.Content;
        var width = (int)Math.Ceiling(content.ActualWidth);
        var height = (int)Math.Ceiling(content.ActualHeight);
        var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var bg = new DrawingVisual();
        using (var dc = bg.RenderOpen()) dc.DrawRectangle(w.Background, null, new Rect(0, 0, width, height));
        bmp.Render(bg);
        bmp.Render(content);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(Path.Combine(dir, name));
        png.Save(fs);
    }

    static T Field<T>(object o, string name) =>
        (T)o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(o)!;

    static void SetField(object o, string name, object? value) =>
        o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(o, value);

    static void Invoke(object o, string name, params object[] args) =>
        o.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(o, args);
}
