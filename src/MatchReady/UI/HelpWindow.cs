using System.Text;
using System.Windows;
using System.Windows.Controls;
using MatchReady.Boost;
using MatchReady.Config;
using MatchReady.Sys;

namespace MatchReady.UI;

/// <summary>What the app does, this PC, tips, the self-check, updates and the log (with one-click copy).</summary>
public sealed class HelpWindow : Window
{
    readonly MainWindow _main;
    readonly ISystemOps _os;
    readonly AppConfig _cfg;
    readonly Func<string?> _canSelfCheck;
    readonly Action<bool> _setBusy;
    readonly TextBlock _result;

    public HelpWindow(MainWindow owner, ISystemOps os, AppConfig cfg, PcInfo pc, Func<string?> canSelfCheck, Action<bool> setBusy)
    {
        _main = owner;
        _os = os;
        _cfg = cfg;
        _canSelfCheck = canSelfCheck;
        _setBusy = setBusy;
        Width = 560;
        Height = 680;
        Ui.Prepare(this, "Help", owner);
        var (root, s, bar) = Ui.Page();

        s.Children.Add(new TextBlock { Text = "Help", Style = Ui.S("Title") });
        if (pc.Summary.Length > 0) s.Children.Add(Ui.Muted("This PC: " + pc.Summary, new Thickness(0, 4, 0, 0)));
        if (pc.Expectation.Length > 0) s.Children.Add(Ui.Muted(pc.Expectation));

        s.Children.Add(Ui.Heading("Check that everything works"));
        s.Children.Add(Ui.Muted("The self-check pauses services and changes settings like a real boost (without closing any apps), " +
                                "puts everything back, then compares. It takes about 30 seconds. CS2 must be closed."));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var selfCheck = Ui.Button("Run self-check");
        selfCheck.Click += async (_, _) => await RunSelfCheck(selfCheck);
        var update = Ui.Button("Check for updates");
        update.Margin = new Thickness(8, 0, 0, 0);
        update.Click += async (_, _) => await CheckUpdates(update);
        row.Children.Add(selfCheck);
        row.Children.Add(update);
        s.Children.Add(row);
        _result = Ui.Muted("", new Thickness(0, 8, 0, 0));
        s.Children.Add(_result);

        s.Children.Add(Ui.Heading("What it does"));
        s.Children.Add(Ui.Body(Texts.WhatIWillDo));
        s.Children.Add(Ui.Heading("Tips"));
        s.Children.Add(Ui.Body(Texts.Tips));
        s.Children.Add(Ui.Heading("Something looks wrong?"));
        s.Children.Add(Ui.Muted("Press Restore everything, or just restart your PC: nothing is permanent. Then click Copy log and paste it " +
                                "into a message or a GitHub issue, so it's clear exactly what the app did."));
        s.Children.Add(Ui.Muted("Log file: " + AppPaths.LogFile, new Thickness(0, 4, 0, 0)));

        var copy = Ui.Button("Copy log", "PrimaryButton");
        copy.Click += (_, _) => copy.Content = CopyLog(pc) ? "Copied ✓" : "No log yet";
        var open = Ui.Button("Open log folder", "SecondaryButton", (_, _) =>
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            _os.LaunchUnelevated(AppPaths.DataDir);
        });
        Ui.AddLeft(bar, copy, open);
        var close = Ui.Button("Close", "SecondaryButton", (_, _) => Close());
        close.IsCancel = true;
        Ui.AddRight(bar, close);
        Content = root;
    }

    async Task RunSelfCheck(Button b)
    {
        var blocker = _canSelfCheck();
        if (blocker != null)
        {
            _result.Text = blocker;
            return;
        }
        b.IsEnabled = false;
        _setBusy(true);
        _result.SetResourceReference(ForegroundProperty, "MutedBrush");
        _result.Text = "Running the self-check…";
        try
        {
            var r = await Task.Run(() => SelfCheck.Run(_os, _cfg));
            _result.SetResourceReference(ForegroundProperty, r.Passed ? "AccentBrush" : "WarnBrush");
            _result.Text = string.Join("\n", r.Lines.Skip(1)) + "\nReport: " + r.ReportPath;
        }
        catch (Exception ex)
        {
            _result.SetResourceReference(ForegroundProperty, "WarnBrush");
            _result.Text = "The self-check couldn't run: " + ex.Message + " Press Restore everything if a boost shows as active.";
        }
        finally
        {
            _setBusy(false);
            b.IsEnabled = true;
        }
    }

    async Task CheckUpdates(Button b)
    {
        b.IsEnabled = false;
        _result.SetResourceReference(ForegroundProperty, "MutedBrush");
        _result.Text = "Checking GitHub for a newer version…";
        var current = typeof(HelpWindow).Assembly.GetName().Version ?? new Version(0, 0, 0);
        var r = await UpdateChecker.CheckAsync(current);
        _result.Text = r.Message;
        b.IsEnabled = true;
        if (r.Ok && r.NewerAvailable)
        {
            _main.OfferUpdate(r);
            new UpdateWindow(this, r, _os).ShowDialog();
        }
    }

    /// <summary>Copies the last part of the log, with the version and this PC's basics on top.</summary>
    static bool CopyLog(PcInfo pc)
    {
        try
        {
            if (!File.Exists(AppPaths.LogFile)) return false;
            string[] lines;
            using (var fs = new FileStream(AppPaths.LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var r = new StreamReader(fs))
                lines = r.ReadToEnd().Split('\n');
            var sb = new StringBuilder();
            sb.AppendLine($"Match Ready {Ui.Version} on Windows build {WindowsInfo.BuildText()}");
            if (pc.Summary.Length > 0) sb.AppendLine("PC: " + pc.Summary);
            foreach (var l in lines.Skip(Math.Max(0, lines.Length - 300))) sb.AppendLine(CrashReport.Scrub(l.TrimEnd('\r')));
            Clipboard.SetText(sb.ToString());
            return true;
        }
        catch (Exception ex)
        {
            Log.Write("Copy log failed: " + ex.Message);
            return false;
        }
    }
}
