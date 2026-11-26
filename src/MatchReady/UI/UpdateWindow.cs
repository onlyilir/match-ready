using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using MatchReady.Restore;
using MatchReady.Sys;

namespace MatchReady.UI;

/// <summary>"Version X is available": Update now downloads, checks the SHA256, then lets Setup replace the app and reopen it.</summary>
public sealed class UpdateWindow : Window
{
    readonly UpdateChecker.Result _r;
    readonly ISystemOps _os;
    readonly TextBlock _status;
    readonly ProgressBar _bar;
    readonly Button _go;
    CancellationTokenSource? _cts;

    public UpdateWindow(Window owner, UpdateChecker.Result r, ISystemOps os)
    {
        _r = r;
        _os = os;
        Width = 500;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        Ui.Prepare(this, "Update available", owner);
        var (root, s, bottom) = Ui.Page();

        s.Children.Add(new TextBlock { Text = $"Match Ready {r.Latest} is available", Style = Ui.S("Title") });
        s.Children.Add(Ui.Muted($"You have {Ui.Version.TrimStart('v')}. Your settings and history are kept.", new Thickness(0, 4, 0, 0)));
        var notes = ReleaseNotes.Section("## " + r.Latest + "\n" + ExtractChanges(r.Notes), r.Latest)
            .Where(l => l.StartsWith("- ")).Take(8).ToList();
        foreach (var n in notes)
            s.Children.Add(new TextBlock { Text = "•  " + n[2..], Style = Ui.S("Body"), Margin = new Thickness(4, 4, 0, 0) });

        _bar = new ProgressBar { Height = 6, Minimum = 0, Maximum = 1, Margin = new Thickness(0, 16, 0, 0), Visibility = Visibility.Collapsed };
        _bar.SetResourceReference(ForegroundProperty, "AccentBrush");
        _bar.SetResourceReference(BackgroundProperty, "SurfaceHighBrush");
        _bar.BorderThickness = new Thickness(0);
        s.Children.Add(_bar);
        _status = Ui.Muted("", new Thickness(0, 8, 0, 0));
        s.Children.Add(_status);

        var installed = UpdateChecker.IsInstalledCopy();
        _go = Ui.Button(installed ? "Update now" : "Open download page", "PrimaryButton");
        _go.IsDefault = true;
        _go.Click += async (_, _) =>
        {
            if (installed) await Update();
            else { _os.LaunchUnelevated(_r.Url); Close(); }
        };
        var later = Ui.Button("Later", "SecondaryButton", (_, _) => { _cts?.Cancel(); Close(); });
        later.IsCancel = true;
        Ui.AddLeft(bottom, Ui.Link("Release notes on GitHub", (_, _) => _os.LaunchUnelevated(_r.Url)));
        Ui.AddRight(bottom, later, _go);
        if (!installed) _status.Text = "This is the portable copy: download the new MatchReady.exe from the release page.";
        Content = root;
    }

    /// <summary>The release notes' "## Changes" part (what the release workflow writes).</summary>
    static string ExtractChanges(string notes)
    {
        var i = notes.IndexOf("## Changes", StringComparison.OrdinalIgnoreCase);
        return i >= 0 ? notes[(i + "## Changes".Length)..] : notes;
    }

    async Task Update()
    {
        if (SessionJournal.TryLoad(AppPaths.SessionFile) != null)
        {
            _status.Text = "A boost is on. Restore it first, then update.";
            return;
        }
        if (Boost.BoostEngine.IsGameRunning(_os.ListProcesses()))
        {
            _status.Text = "CS2 is running. Update after you play.";
            return;
        }
        _go.IsEnabled = false;
        _bar.Visibility = Visibility.Visible;
        _status.Text = "Downloading…";
        _cts = new CancellationTokenSource();
        try
        {
            var current = typeof(UpdateWindow).Assembly.GetName().Version ?? new Version(0, 0, 0);
            var (path, message) = await UpdateChecker.DownloadAsync(_r, current, new Progress<double>(p => _bar.Value = p), _cts.Token);
            Log.Write("Update: " + message);
            _status.Text = message;
            if (path == null)
            {
                _go.IsEnabled = true;
                if (_r.SetupUrl == null) _os.LaunchUnelevated(_r.Url);
                return;
            }
            _status.Text = "Installing… Match Ready will reopen by itself.";
            // Setup waits for this copy to exit, installs quietly, then starts the new version.
            Process.Start(new ProcessStartInfo(path, $"--update {Environment.ProcessId}") { UseShellExecute = false })?.Dispose();
            if (Owner is MainWindow main) main.ExitApp();
            else if (Application.Current.MainWindow is MainWindow m) m.ExitApp();
            Application.Current.Shutdown();
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Cancelled.";
            _go.IsEnabled = true;
        }
        catch (Exception ex)
        {
            Log.Write("Update failed: " + ex.Message);
            _status.Text = "The update couldn't be downloaded: " + ex.Message;
            _go.IsEnabled = true;
        }
    }
}
