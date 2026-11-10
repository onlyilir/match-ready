using MatchReady.Config;
using MatchReady.Sys;

namespace MatchReady.UI;

/// <summary>
/// First launch: "What I will do", then a few quick questions so the app fits this PC without digging
/// through Settings. It shows what it detected (hardware, installed device software) to pre-fill answers.
/// </summary>
public sealed class FirstRunPanel : Panel
{
    readonly AppConfig _cfg;
    readonly HashSet<string> _running;
    readonly PcInfo _pc;
    readonly bool _offerRestorePoint;
    readonly Action<QuickSetup, bool> _done;
    readonly Stack _content;
    readonly Button _next;

    RadioButton? _browser, _obsYes, _light;
    readonly Dictionary<string, CheckBox> _devices = new();
    CheckBox? _notify, _restorePoint;

    public FirstRunPanel(AppConfig cfg, IEnumerable<string> runningProcessNames, PcInfo pc, bool showWelcome,
        Action<QuickSetup, bool> done)
    {
        _cfg = cfg;
        _running = new HashSet<string>(runningProcessNames, StringComparer.OrdinalIgnoreCase);
        _pc = pc;
        _offerRestorePoint = !cfg.RestorePointAsked;
        _done = done;

        Dock = DockStyle.Fill;
        BackColor = Theme.Back;
        Padding = new Padding(24, 18, 24, 18);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = Theme.Back };
        _next = Theme.FlatButton("", Theme.Green, 200, 44);
        _next.Dock = DockStyle.Fill;
        _next.Font = Theme.UiFont(12, FontStyle.Bold);
        _next.Click += (_, _) => (_next.Tag as Action)?.Invoke();
        bottom.Controls.Add(_next);

        _content = new Stack { Dock = DockStyle.Fill };
        Controls.Add(bottom);
        Controls.Add(_content);
        _content.BringToFront();

        if (showWelcome) ShowWelcome();
        else ShowQuestions();
    }

    void Reset(string buttonText, Action onClick)
    {
        _content.SuspendLayout();
        foreach (Control c in _content.Controls.Cast<Control>().ToList()) c.Dispose();
        _content.Controls.Clear();
        _next.Text = buttonText;
        _next.AccessibleName = buttonText;
        _next.Tag = onClick;
    }

    void ShowWelcome()
    {
        Reset("Next", ShowQuestions);
        _content.Add(Theme.Heading("Welcome to Match Ready", 15)).Margin = new Padding(0, 0, 0, 10);
        _content.Add(new Label { Text = Texts.WhatIWillDo, AutoSize = true, Font = Theme.UiFont(10), UseMnemonic = false });
        _content.ResumeLayout();
        _next.Focus();
    }

    void ShowQuestions()
    {
        var a = QuickSetup.From(_cfg);
        Reset("Save and start", Finish);

        _content.Add(Theme.Heading("Quick setup", 15)).Margin = new Padding(0, 0, 0, 2);
        _content.Add(Theme.MutedText("So Match Ready fits your PC. You can change all of this later in Settings."));
        if (_pc.Summary.Length > 0)
        {
            _content.Add(Theme.MutedText("Your PC: " + _pc.Summary, 9.5f)).Margin = new Padding(0, 4, 0, 0);
            if (_pc.Expectation.Length > 0) _content.Add(Theme.MutedText(_pc.Expectation));
        }

        _content.Add(Theme.Heading("Where do you queue for FACEIT?"));
        var q1 = Radios("The FACEIT app (or I don't use FACEIT)", "A web browser (Chrome, Edge…)");
        _browser = (RadioButton)q1.Controls[1];
        ((RadioButton)q1.Controls[a.QueueInBrowser ? 1 : 0]).Checked = true;
        _content.Add(q1);

        _content.Add(Theme.Heading("Do you record or stream with OBS?"));
        var q2 = Radios("No", "Yes, sometimes (never close OBS)");
        _obsYes = (RadioButton)q2.Controls[1];
        ((RadioButton)q2.Controls[a.UsesObs ? 1 : 0]).Checked = true;
        _content.Add(q2);

        _content.Add(Theme.Heading("How much should it do?"));
        var q3 = Radios("Full: apps + Windows services (recommended)", "Light: only heavy apps (AI models, wallpapers)");
        _light = (RadioButton)q3.Controls[1];
        ((RadioButton)q3.Controls[a.Profile == BoostProfile.Light ? 1 : 0]).Checked = true;
        _content.Add(q3);

        AddDevices(a);

        _content.Add(Theme.Heading("Notifications"));
        _notify = _content.Add(new CheckBox
        {
            Text = "Pause Windows notifications while CS2 runs", Checked = a.PauseNotifications, AutoSize = true,
            Margin = new Padding(2, 2, 0, 0),
        });
        _content.Add(Theme.MutedText("Leave this off if you want FACEIT \"match found\" alerts to pop up.")).Margin = new Padding(22, 0, 0, 4);

        if (_offerRestorePoint)
        {
            _content.Add(Theme.Heading("Safety net"));
            _restorePoint = _content.Add(new CheckBox
            {
                Text = "Make a Windows restore point now (recommended)", Checked = true, AutoSize = true,
                Margin = new Padding(2, 2, 0, 8),
            });
        }
        _content.ResumeLayout();
        _next.Focus();
    }

    /// <summary>Only the device software this PC has. "Show all" reveals the rest.</summary>
    void AddDevices(QuickSetup a)
    {
        var found = QuickSetup.FoundOn(_pc, _running, _cfg).Select(f => f.Id).ToHashSet();
        _content.Add(Theme.Heading("Keep these running while you play"));
        _content.Add(Theme.MutedText(found.Count > 0
            ? "Found on this PC. Tick the ones that run your mouse, keyboard, headset, mic or fans. Unticked ones are closed during games."
            : "No mouse, headset or lighting software was found on this PC."));

        var hidden = new List<CheckBox>();
        foreach (var (id, label, _) in QuickSetup.Devices)
        {
            var app = _cfg.Apps.FirstOrDefault(x => x.Id == id);
            var running = app != null && app.Processes.Any(_running.Contains);
            var cb = new CheckBox
            {
                Text = label + (running ? "   · running now" : ""),
                // Default "keep" for everything (also software installed later); the saved choice wins after the first setup.
                Checked = !_cfg.QuestionsDone || a.KeepRunning.Contains(id),
                AutoSize = true,
                Margin = new Padding(2, 2, 0, 2),
                Visible = found.Contains(id),
                UseMnemonic = false,
            };
            _devices[id] = cb;
            _content.Add(cb);
            if (!cb.Visible) hidden.Add(cb);
        }
        if (hidden.Count > 0)
        {
            var more = _content.Add(Theme.LinkText($"Show {hidden.Count} more"));
            more.Margin = new Padding(2, 2, 0, 4);
            more.LinkClicked += (_, _) =>
            {
                foreach (var cb in hidden) cb.Visible = true;
                more.Visible = false;
            };
        }
    }

    static FlowLayoutPanel Radios(params string[] options)
    {
        // Each question gets its own container, so its radio buttons form their own group.
        var p = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0),
            BackColor = Theme.Back,
        };
        foreach (var o in options)
        {
            // Flat style: a clear filled dot on the dark background.
            var r = new RadioButton
            {
                Text = o, AutoSize = true, Margin = new Padding(2, 2, 0, 2), FlatStyle = FlatStyle.Flat, UseMnemonic = false,
            };
            r.FlatAppearance.CheckedBackColor = Theme.Green;
            p.Controls.Add(r);
        }
        return p;
    }

    void Finish()
    {
        var answers = new QuickSetup
        {
            QueueInBrowser = _browser?.Checked == true,
            UsesObs = _obsYes?.Checked == true,
            Profile = _light?.Checked == true ? BoostProfile.Light : BoostProfile.Full,
            KeepRunning = new HashSet<string>(_devices.Where(d => d.Value.Checked).Select(d => d.Key), StringComparer.OrdinalIgnoreCase),
            PauseNotifications = _notify?.Checked == true,
        };
        _done(answers, _restorePoint?.Checked == true);
    }
}
