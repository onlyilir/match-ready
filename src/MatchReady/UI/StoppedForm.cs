using MatchReady.Config;
using MatchReady.Restore;

namespace MatchReady.UI;

/// <summary>
/// "What was stopped": the current boost and the last 10, one row per app/service.
/// Each row has a "Keep running next time" button, so a wrong choice is fixed in one click.
/// </summary>
public sealed class StoppedForm : Form
{
    readonly AppConfig _cfg;
    readonly Action _save;
    readonly List<SessionJournal> _boosts = new();
    readonly ComboBox _pick;
    readonly Stack _list;

    public StoppedForm(SessionJournal? active, List<SessionJournal> history, AppConfig cfg, Action save)
    {
        _cfg = cfg;
        _save = save;
        if (active != null) _boosts.Add(active);
        _boosts.AddRange(history);

        Text = "What was stopped";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Theme.Apply(this);
        ClientSize = new Size(500, 580);

        var top = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = Theme.Back };
        var lbl = new Label { Text = "Boost:", AutoSize = true, Location = new Point(22, 17) };
        _pick = new ComboBox
        {
            Location = new Point(80, 13), Width = 398, DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Theme.Panel, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat, AccessibleName = "Which boost",
        };
        foreach (var j in _boosts) _pick.Items.Add(Describe(j, active));
        top.Controls.AddRange(new Control[] { lbl, _pick });

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Theme.Panel };
        var close = Theme.FlatButton("Close", Theme.Back, 110);
        close.Location = new Point(500 - 22 - 110, 10);
        close.DialogResult = DialogResult.OK;
        bottom.Controls.Add(close);
        AcceptButton = close;
        CancelButton = close;

        _list = new Stack { Dock = DockStyle.Fill, Padding = new Padding(22, 4, 8, 0) };
        Controls.Add(bottom);
        Controls.Add(top);
        Controls.Add(_list);
        _list.BringToFront();
        top.Visible = _boosts.Count > 1;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // Built after the window has scaled itself: rows size themselves from the font.
        _pick.SelectedIndexChanged += (_, _) => Fill(_boosts[_pick.SelectedIndex]);
        if (_boosts.Count > 0) _pick.SelectedIndex = 0;
        else Fill(null);
    }

    static string Describe(SessionJournal j, SessionJournal? active)
    {
        var when = j.StartedUtc.ToLocalTime().ToString("ddd d MMM, HH:mm");
        if (j == active) return $"Now: active since {when}";
        var took = j.EndedUtc is DateTime end ? $"  ·  {MainForm.Duration(end - j.StartedUtc)}" : "";
        return $"{when}{took}  ·  {j.AppsClosed} apps, {j.ServicesPaused} services";
    }

    void Fill(SessionJournal? j)
    {
        var s = _list;
        s.SuspendLayout();
        foreach (Control c in s.Controls.Cast<Control>().ToList()) c.Dispose();
        s.Controls.Clear();

        if (j == null)
        {
            s.Add(Theme.Heading("Nothing has been boosted yet.", 13)).Margin = new Padding(0, 12, 0, 4);
            s.Add(Theme.MutedText("Press BOOST MY GAME, or just start CS2 with Auto mode on."));
            s.ResumeLayout();
            return;
        }

        var when = j.StartedUtc.ToLocalTime().ToString("ddd d MMM, HH:mm");
        var active = j.State != SessionState.Restored;
        s.Add(Theme.Heading(active ? "Boost active" : "Finished boost", 13)).Margin = new Padding(0, 8, 0, 2);
        var freed = j.RamFreedBytes > 0 ? $" Freed about {MainForm.FormatBytes(j.RamFreedBytes)} of RAM." : "";
        s.Add(Theme.MutedText((active
            ? $"Since {when}. Pressing Restore (or closing CS2) puts services and settings back."
            : $"{when}. Services and settings were put back.") + freed));

        // Apps
        var apps = j.Apps.Where(a => a.Closed).ToList();
        s.Add(Theme.Heading($"Apps closed ({apps.Count})"));
        if (apps.Count == 0) s.Add(Theme.MutedText("None."));
        foreach (var a in apps)
        {
            var entry = _cfg.Apps.FirstOrDefault(e => (a.Id.Length > 0 && e.Id == a.Id) || e.Label == a.Label);
            var note = a.How == "force-closed" ? "Force-closed (it ignored the polite close)" : null;
            s.Add(new Row(a.Label, KeepButton(entry != null && !entry.Enabled, entry == null ? null : () => entry.Enabled = false), note));
        }

        // Services
        var svcs = j.Services.Where(x => x.Stopped).ToList();
        s.Add(Theme.Heading($"Windows services paused ({svcs.Count})"));
        if (svcs.Count == 0) s.Add(Theme.MutedText("None."));
        foreach (var x in svcs)
        {
            var entry = _cfg.Services.FirstOrDefault(e => string.Equals(e.Name, x.Name, StringComparison.OrdinalIgnoreCase));
            s.Add(new Row(x.Label, KeepButton(entry != null && !entry.Enabled, entry == null ? null : () => entry.Enabled = false,
                "Don't pause next time")));
        }

        // Settings
        var settings = new List<string>();
        if (j.Power is { Done: true } p) settings.Add($"Power plan: {p.OriginalName} → {p.AppliedName}");
        settings.AddRange(j.Registry.Where(r => r.Applied).Select(r => r.Label));
        s.Add(Theme.Heading("Settings changed"));
        if (settings.Count == 0) s.Add(Theme.MutedText("None."));
        foreach (var t in settings) s.Add(new Row(t));

        if (j.Skipped.Count > 0)
        {
            s.Add(Theme.Heading("Left alone"));
            foreach (var t in j.Skipped) s.Add(Theme.MutedText("•  " + t));
        }
        s.Add(Theme.MutedText("Changed your mind? Everything here can be switched back in Settings.")).Margin = new Padding(0, 14, 0, 12);
        s.ResumeLayout();
    }

    Control KeepButton(bool alreadyKept, Action? keep, string text = "Keep running next time")
    {
        const string done = "Kept next time ✓";
        var b = Row.SmallButton(alreadyKept ? done : text);
        b.AccessibleName = text;
        // Not disabled: Windows would grey out the text. "Done" buttons just ignore clicks.
        void MarkDone()
        {
            b.Text = done;
            b.ForeColor = Theme.Green;
            b.Cursor = Cursors.Default;
            b.FlatAppearance.MouseOverBackColor = Theme.Back;
            b.Tag = "done";
        }
        if (alreadyKept) MarkDone();
        if (keep == null && !alreadyKept) b.Visible = false;
        b.Click += (_, _) =>
        {
            if (b.Tag as string == "done") return;
            keep!();
            _save();
            MarkDone();
        };
        return b;
    }
}
