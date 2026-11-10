using System.Text;
using MatchReady.Boost;
using MatchReady.Config;
using MatchReady.Sys;

namespace MatchReady.UI;

/// <summary>What the app does, this PC, tips, the self-check, updates and the log (with one-click copy).</summary>
public sealed class HelpForm : Form
{
    readonly ISystemOps _os;
    readonly AppConfig _cfg;
    readonly Func<string?> _canSelfCheck;
    readonly Action<bool> _setBusy;
    readonly Action<UpdateChecker.Result> _onUpdate;
    readonly Label _result;

    public HelpForm(ISystemOps os, AppConfig cfg, PcInfo pc, Func<string?> canSelfCheck, Action<bool> setBusy,
        Action<UpdateChecker.Result> onUpdate)
    {
        _os = os;
        _cfg = cfg;
        _canSelfCheck = canSelfCheck;
        _setBusy = setBusy;
        _onUpdate = onUpdate;
        Text = "Help";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Theme.Apply(this);
        ClientSize = new Size(540, 620);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Theme.Panel };
        var copy = Theme.FlatButton("Copy log", Theme.Green, 110);
        copy.Location = new Point(20, 12);
        copy.Click += (_, _) => copy.Text = CopyLog(pc) ? "Copied ✓" : "No log yet";
        var open = Theme.FlatButton("Open log folder", Theme.Back, 140);
        open.Location = new Point(138, 12);
        open.Click += (_, _) =>
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            _os.LaunchUnelevated(AppPaths.DataDir);
        };
        var close = Theme.FlatButton("Close", Theme.Back, 100);
        close.Location = new Point(540 - 20 - 100, 12);
        close.DialogResult = DialogResult.OK;
        CancelButton = close;
        bottom.Controls.AddRange(new Control[] { copy, open, close });

        var s = new Stack { Dock = DockStyle.Fill, Padding = new Padding(22, 14, 8, 10) };
        Controls.Add(bottom);
        Controls.Add(s);
        s.BringToFront();

        s.Add(Theme.Heading("Match Ready " + Theme.Version, 13)).Margin = new Padding(0, 0, 0, 2);
        if (pc.Summary.Length > 0) s.Add(Theme.MutedText("This PC: " + pc.Summary, 9.5f));
        if (pc.Expectation.Length > 0) s.Add(Theme.MutedText(pc.Expectation, 9.5f));

        // Self-check and updates
        s.Add(Theme.Heading("Check that everything works"));
        s.Add(Theme.MutedText("The self-check pauses services and changes settings like a real boost (without closing any apps), " +
                              "puts everything back, then compares. Takes about 30 seconds. CS2 must be closed.", 9.5f));
        var row = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Back, Margin = new Padding(0, 4, 0, 2) };
        var selfCheck = Theme.FlatButton("Run self-check", Theme.Panel, 150, 32);
        selfCheck.Click += async (_, _) => await RunSelfCheck(selfCheck);
        var update = Theme.FlatButton("Check for updates", Theme.Panel, 160, 32);
        update.Margin = new Padding(8, 0, 0, 0);
        update.Click += async (_, _) => await CheckUpdates(update);
        row.Controls.AddRange(new Control[] { selfCheck, update });
        s.Add(row);
        _result = s.Add(Theme.MutedText("", 9.5f));

        s.Add(Theme.Heading("What it does"));
        s.Add(new Label { Text = Texts.WhatIWillDo, AutoSize = true, Font = Theme.UiFont(9.5f), UseMnemonic = false });
        s.Add(Theme.Heading("Tips"));
        s.Add(new Label { Text = Texts.Tips, AutoSize = true, Font = Theme.UiFont(9.5f), UseMnemonic = false });
        s.Add(Theme.Heading("Something looks wrong?"));
        s.Add(Theme.MutedText(
            "Press RESTORE EVERYTHING, or just reboot: nothing is permanent. Then click \"Copy log\" and paste it " +
            "into a message, so it's clear exactly what the app did.\r\n\r\nLog file: " + AppPaths.LogFile, 9.5f))
            .Margin = new Padding(0, 0, 0, 12);
    }

    async Task RunSelfCheck(Button b)
    {
        var blocker = _canSelfCheck();
        if (blocker != null)
        {
            _result.Text = blocker;
            return;
        }
        b.Enabled = false;
        _setBusy(true);
        _result.ForeColor = Theme.Muted;
        _result.Text = "Running the self-check…";
        try
        {
            var r = await Task.Run(() => SelfCheck.Run(_os, _cfg));
            _result.ForeColor = r.Passed ? Theme.Green : Theme.Yellow;
            _result.Text = string.Join("\r\n", r.Lines.Skip(1)) + "\r\nReport: " + r.ReportPath;
        }
        catch (Exception ex)
        {
            _result.ForeColor = Theme.Yellow;
            _result.Text = "Self-check failed to run: " + ex.Message + " Press RESTORE EVERYTHING if a boost shows as active.";
        }
        finally
        {
            _setBusy(false);
            b.Enabled = true;
        }
    }

    async Task CheckUpdates(Button b)
    {
        b.Enabled = false;
        _result.ForeColor = Theme.Muted;
        _result.Text = "Checking GitHub for a newer version…";
        var current = typeof(HelpForm).Assembly.GetName().Version ?? new Version(0, 0, 0);
        var r = await UpdateChecker.CheckAsync(current);
        _result.Text = r.Message;
        if (r.Ok && r.NewerAvailable)
        {
            _result.ForeColor = Theme.Green;
            _result.Text += " Opening the download page…";
            _os.LaunchUnelevated(r.Url);
        }
        _onUpdate(r);
        b.Enabled = true;
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
            sb.AppendLine($"Match Ready {Theme.Version} on Windows build {WindowsInfo.BuildText()}");
            if (pc.Summary.Length > 0) sb.AppendLine("PC: " + pc.Summary);
            foreach (var l in lines.Skip(Math.Max(0, lines.Length - 300))) sb.AppendLine(l.TrimEnd('\r'));
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
