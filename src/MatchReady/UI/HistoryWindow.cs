using System.Windows;
using System.Windows.Controls;
using MatchReady.Config;
using MatchReady.Restore;

namespace MatchReady.UI;

/// <summary>
/// "What was stopped": the current boost and the last 10, one row per app/service.
/// Each row has a "Keep running next time" button, so a wrong choice is fixed in one click.
/// </summary>
public sealed class HistoryWindow : Window
{
    readonly AppConfig _cfg;
    readonly Action _save;
    readonly List<SessionJournal> _boosts = new();
    readonly StackPanel _list;

    public HistoryWindow(Window owner, SessionJournal? active, List<SessionJournal> history, AppConfig cfg, Action save)
    {
        _cfg = cfg;
        _save = save;
        if (active != null) _boosts.Add(active);
        _boosts.AddRange(history);

        Width = 540;
        Height = 640;
        Ui.Prepare(this, "What was stopped", owner);
        var (root, content, bar) = Ui.Page();
        _list = content;

        if (_boosts.Count > 1)
        {
            var pick = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            System.Windows.Automation.AutomationProperties.SetName(pick, "Which boost");
            foreach (var j in _boosts) pick.Items.Add(Describe(j, active));
            pick.SelectionChanged += (_, _) => Fill(_boosts[pick.SelectedIndex]);
            var top = new DockPanel { Margin = new Thickness(24, 16, 24, 0) };
            var label = new TextBlock { Text = "Boost", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            DockPanel.SetDock(label, Dock.Left);
            top.Children.Add(label);
            top.Children.Add(pick);
            DockPanel.SetDock(top, Dock.Top);
            root.Children.Insert(0, top);
            Loaded += (_, _) => pick.SelectedIndex = 0;
        }
        else
        {
            Fill(_boosts.FirstOrDefault());
        }

        var close = Ui.Button("Close", "SecondaryButton", (_, _) => Close());
        close.IsCancel = true;
        close.IsDefault = true;
        Ui.AddRight(bar, close);
        Content = root;
    }

    static string Describe(SessionJournal j, SessionJournal? active)
    {
        var when = j.StartedUtc.ToLocalTime().ToString("ddd d MMM, HH:mm");
        if (j == active) return $"Now: active since {when}";
        var took = j.EndedUtc is DateTime end ? $"  ·  {Ui.Duration(end - j.StartedUtc)}" : "";
        return $"{when}{took}  ·  {j.AppsClosed} apps, {j.ServicesPaused} services";
    }

    void Fill(SessionJournal? j)
    {
        var s = _list;
        s.Children.Clear();
        if (j == null)
        {
            s.Children.Add(Ui.Heading("Nothing has been boosted yet.", 4));
            s.Children.Add(Ui.Muted("Press Boost my game, or just start CS2 with Auto mode on."));
            return;
        }

        var when = j.StartedUtc.ToLocalTime().ToString("ddd d MMM, HH:mm");
        var active = j.State != SessionState.Restored;
        s.Children.Add(Ui.Heading(active ? "Boost active" : "Finished boost", 4));
        var freed = j.RamFreedBytes > 0 ? $" Freed about {Ui.FormatBytes(j.RamFreedBytes)} of RAM." : "";
        s.Children.Add(Ui.Muted((active
            ? $"Since {when}. Pressing Restore (or closing CS2) puts services and settings back."
            : $"{when}. Services and settings were put back.") + freed));

        var apps = j.Apps.Where(a => a.Closed).ToList();
        s.Children.Add(Ui.Heading($"Apps closed ({apps.Count})"));
        if (apps.Count == 0) s.Children.Add(Ui.Muted("None."));
        foreach (var a in apps)
        {
            var entry = _cfg.Apps.FirstOrDefault(e => (a.Id.Length > 0 && e.Id == a.Id) || e.Label == a.Label);
            var note = a.How == "force-closed" ? "Force-closed (it ignored the polite close)" : null;
            s.Children.Add(Ui.Row(a.Label, KeepButton(entry != null && !entry.Enabled, entry == null ? null : () => entry.Enabled = false), note));
        }

        var svcs = j.Services.Where(x => x.Stopped).ToList();
        s.Children.Add(Ui.Heading($"Windows services paused ({svcs.Count})"));
        if (svcs.Count == 0) s.Children.Add(Ui.Muted("None."));
        foreach (var x in svcs)
        {
            var entry = _cfg.Services.FirstOrDefault(e => string.Equals(e.Name, x.Name, StringComparison.OrdinalIgnoreCase));
            s.Children.Add(Ui.Row(x.Label, KeepButton(entry != null && !entry.Enabled, entry == null ? null : () => entry.Enabled = false,
                "Don't pause next time")));
        }

        var settings = new List<string>();
        if (j.Power is { Done: true } p) settings.Add($"Power plan: {p.OriginalName} → {p.AppliedName}");
        settings.AddRange(j.Registry.Where(r => r.Applied).Select(r => r.Label));
        s.Children.Add(Ui.Heading("Settings changed"));
        if (settings.Count == 0) s.Children.Add(Ui.Muted("None."));
        foreach (var t in settings) s.Children.Add(Ui.Row(t));

        if (j.Skipped.Count > 0)
        {
            s.Children.Add(Ui.Heading("Left alone"));
            foreach (var t in j.Skipped) s.Children.Add(Ui.Muted("•  " + t));
        }
        s.Children.Add(Ui.Muted("Changed your mind? Everything here can be switched back in Settings.", new Thickness(0, 14, 0, 0)));
    }

    UIElement KeepButton(bool alreadyKept, Action? keep, string text = "Keep running next time")
    {
        const string done = "Kept next time ✓";
        if (keep == null && !alreadyKept) return new TextBlock();
        var b = Ui.Button(alreadyKept ? done : text, "QuietButton");
        var isDone = false;
        if (alreadyKept) MarkDone();
        b.Click += (_, _) =>
        {
            if (isDone) return;
            keep!();
            _save();
            MarkDone();
        };
        return b;

        void MarkDone()
        {
            isDone = true;
            b.Content = done;
            b.SetResourceReference(ForegroundProperty, "AccentBrush");
            b.Cursor = System.Windows.Input.Cursors.Arrow;
        }
    }
}
