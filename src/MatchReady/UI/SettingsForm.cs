using MatchReady.Config;
using MatchReady.Sys;

namespace MatchReady.UI;

/// <summary>
/// Settings, grouped under plain headings, with a search box. OK = saved; Retry = saved and run the quick setup again.
/// </summary>
public sealed class SettingsForm : Form
{
    public const string CustomGroup = "Your apps";

    readonly AppConfig _cfg;
    readonly ISystemOps _os;
    readonly Stack _s;
    readonly TextBox _search;
    // Each app row: its checkbox, its note (if any) and its group heading, for the search filter.
    readonly List<(CheckBox Box, Label? Note, Label Heading, AppEntry App)> _apps = new();
    readonly Dictionary<CheckBox, ServiceEntry> _services = new();
    readonly List<AppEntry> _added = new();
    readonly HashSet<AppEntry> _removed = new();
    Label _mine = null!;
    ComboBox _browsers = null!, _obs = null!, _profile = null!;
    CheckBox _auto = null!, _tray = null!, _reopen = null!, _power = null!, _notify = null!, _gameMode = null!,
        _startup = null!, _updates = null!;

    static readonly string[] AskOptions = { "Ask me first", "Always close", "Never close" }; // order = AskMode
    static readonly string[] ProfileOptions = { "Full (apps + services)", "Light (heavy apps only)" }; // order = BoostProfile

    public SettingsForm(AppConfig cfg, ISystemOps os)
    {
        _cfg = cfg;
        _os = os;
        Text = "Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Theme.Apply(this);
        ClientSize = new Size(560, 680);

        var top = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Theme.Back };
        _search = new TextBox
        {
            Location = new Point(22, 12), Width = 516, BackColor = Theme.Panel, ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle, PlaceholderText = "Search apps (e.g. discord, onedrive, razer)",
            AccessibleName = "Search apps",
        };
        _search.TextChanged += (_, _) => Filter();
        top.Controls.Add(_search);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Theme.Panel };
        var rerun = Theme.LinkText("Run quick setup again");
        rerun.Location = new Point(20, 10);
        rerun.LinkClicked += (_, _) => { Apply(); DialogResult = DialogResult.Retry; };
        var reset = Theme.LinkText("Reset to defaults");
        reset.Location = new Point(20, 32);
        reset.LinkClicked += (_, _) => ResetToDefaults();
        var save = Theme.FlatButton("Save", Theme.Green, 100);
        save.Location = new Point(560 - 20 - 100, 12);
        save.Click += (_, _) => { Apply(); DialogResult = DialogResult.OK; };
        var cancel = Theme.FlatButton("Cancel", Theme.Back, 100);
        cancel.Location = new Point(560 - 20 - 100 - 108, 12);
        cancel.DialogResult = DialogResult.Cancel;
        CancelButton = cancel;
        bottom.Controls.AddRange(new Control[] { rerun, reset, save, cancel });

        _s = new Stack { Dock = DockStyle.Fill, Padding = new Padding(22, 0, 8, 10) };
        Controls.Add(bottom);
        Controls.Add(top);
        Controls.Add(_s);
        _s.BringToFront();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // Built after the window has scaled itself: rows size themselves from the font.
        Build();
        _search.Focus();
    }

    void Build()
    {
        var s = _s;
        s.SuspendLayout();

        // ----- Apps -----
        s.Add(Theme.Heading("Apps closed while you play", 13)).Margin = new Padding(0, 4, 0, 2);
        s.Add(Theme.MutedText("Ticked = closed during games. Unticked = left running. Unknown apps are never touched."));
        var normal = _cfg.Apps.Where(a => a.Kind == AppKind.Normal).ToList();
        foreach (var group in normal.Where(a => a.Group is not ("Keep running" or CustomGroup)).GroupBy(a => a.Group))
        {
            var h = s.Add(SubHeading(group.Key));
            foreach (var a in group) AddApp(a, h);
        }
        var devices = s.Add(SubHeading("Your devices and voice"));
        s.Add(Theme.MutedText("Kept running unless you tick them.")).Margin = new Padding(0, 0, 0, 2);
        foreach (var a in normal.Where(a => a.Group == "Keep running")) AddApp(a, devices);

        _mine = s.Add(SubHeading(CustomGroup));
        foreach (var a in normal.Where(a => a.Group == CustomGroup)) AddApp(a, _mine);
        var add = s.Add(Theme.LinkText("+ Add an app from what's running now…"));
        add.Margin = new Padding(2, 4, 0, 4);
        add.LinkClicked += (_, _) => AddApps();

        // ----- Browsers and OBS -----
        s.Add(Theme.Heading("Browsers and OBS", 13));
        _browsers = Combo(AskOptions, (int)_cfg.Browsers);
        s.Add(new Row("Browsers (Chrome, Edge, Brave, Opera…)", _browsers));
        _obs = Combo(AskOptions, (int)_cfg.Obs);
        s.Add(new Row("OBS Studio", _obs));
        s.Add(Theme.MutedText("In Auto mode nothing is ever asked: \"Ask me first\" then means \"leave it open\"."));

        // ----- Services -----
        s.Add(Theme.Heading("Windows services paused while you play", 13));
        s.Add(Theme.MutedText("Only paused, never turned off. They start again on restore or reboot. Not used by the Light profile."));
        foreach (var svc in _cfg.Services)
        {
            var cb = s.Add(Check(svc.Label, svc.Enabled));
            _services[cb] = svc;
        }

        // ----- Options -----
        s.Add(Theme.Heading("Options", 13));
        _profile = Combo(ProfileOptions, (int)_cfg.Profile);
        s.Add(new Row("How much to do", _profile));
        _auto = s.Add(Check("Auto mode: boost when CS2 starts, restore when it closes", _cfg.AutoMode));
        _tray = s.Add(Check("Keep running in the tray when I close the window", _cfg.KeepInTray));
        _startup = s.Add(Check("Start with Windows (in the tray)", _cfg.StartWithWindows));
        s.Add(Theme.MutedText("Windows asks for permission (the admin prompt) each time you sign in.")).Margin = new Padding(20, 0, 0, 2);
        _power = s.Add(Check("Switch to the High performance power plan", _cfg.SwitchPowerPlan));
        _gameMode = s.Add(Check("Make sure Windows Game Mode is on", _cfg.EnsureGameMode));
        _notify = s.Add(Check("Pause Windows notifications (leave off for FACEIT match alerts)", _cfg.PauseNotifications));
        _reopen = s.Add(Check("Reopen closed apps after restore", _cfg.ReopenAppsOnRestore));
        s.Add(Theme.MutedText("Off = Match Ready never opens anything; apps come back when you open them or restart.")).Margin = new Padding(20, 0, 0, 2);
        _updates = s.Add(Check("Check for updates once a day (only when CS2 isn't running)", _cfg.CheckForUpdates));
        s.Add(Theme.MutedText("Only asks GitHub for the newest version number. Nothing is downloaded or installed.")).Margin = new Padding(20, 0, 0, 16);

        s.ResumeLayout();
    }

    void AddApp(AppEntry a, Label heading)
    {
        var cb = _s.Add(Check(a.Label, a.Enabled));
        Label? note = null;
        if (a.Group == CustomGroup)
        {
            note = _s.Add(Theme.LinkText("remove"));
            note.Margin = new Padding(20, 0, 0, 2);
            ((LinkLabel)note).LinkClicked += (_, _) =>
            {
                _removed.Add(a);
                cb.Visible = false;
                note.Visible = false;
            };
        }
        else if (!string.IsNullOrEmpty(a.Note))
        {
            note = _s.Add(Theme.MutedText(a.Note!, 8.5f));
            note.Margin = new Padding(20, 0, 0, 2);
        }
        _apps.Add((cb, note, heading, a));
    }

    /// <summary>Shows only apps whose name matches; hides headings with nothing left under them.</summary>
    void Filter()
    {
        var q = _search.Text.Trim();
        _s.SuspendLayout();
        foreach (var (box, note, _, app) in _apps)
        {
            if (_removed.Contains(app)) continue;
            var show = q.Length == 0 || app.Label.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                       app.Processes.Any(p => p.Contains(q, StringComparison.OrdinalIgnoreCase));
            box.Visible = show;
            if (note != null) note.Visible = show;
        }
        foreach (var heading in _apps.Select(x => x.Heading).Distinct())
            heading.Visible = q.Length == 0 || _apps.Any(x => x.Heading == heading && x.Box.Visible);
        _s.ResumeLayout();
    }

    void AddApps()
    {
        using var f = new AddAppForm(_os, _cfg);
        if (f.ShowDialog(this) != DialogResult.OK) return;
        foreach (var a in f.Chosen)
        {
            _added.Add(a);
            // Shown right away under "Your apps".
            var cb = Check(a.Label, true);
            _s.Controls.Add(cb);
            _s.Controls.SetChildIndex(cb, _s.Controls.IndexOf(_mine) + 1);
            _apps.Add((cb, null, _mine, a));
        }
    }

    void Apply()
    {
        foreach (var a in _added.Where(a => !_cfg.Apps.Contains(a))) _cfg.Apps.Add(a);
        foreach (var (cb, _, _, a) in _apps) a.Enabled = cb.Checked;
        foreach (var a in _removed) _cfg.Apps.Remove(a);
        foreach (var (cb, svc) in _services) svc.Enabled = cb.Checked;
        _cfg.Browsers = (AskMode)_browsers.SelectedIndex;
        _cfg.Obs = (AskMode)_obs.SelectedIndex;
        _cfg.Profile = (BoostProfile)_profile.SelectedIndex;
        _cfg.AutoMode = _auto.Checked;
        _cfg.KeepInTray = _tray.Checked;
        _cfg.StartWithWindows = _startup.Checked;
        _cfg.ReopenAppsOnRestore = _reopen.Checked;
        _cfg.SwitchPowerPlan = _power.Checked;
        _cfg.PauseNotifications = _notify.Checked;
        _cfg.EnsureGameMode = _gameMode.Checked;
        _cfg.CheckForUpdates = _updates.Checked;
    }

    void ResetToDefaults()
    {
        if (!Theme.Ask(this, "Reset to defaults",
                "Put every setting back to how it was after installing? Your Pro tab ticks and added apps are removed too.",
                "Reset", "Cancel")) return;
        var d = Defaults.Create();
        _cfg.Apps = d.Apps;
        _cfg.Services = d.Services;
        _cfg.AutoMode = d.AutoMode;
        _cfg.KeepInTray = d.KeepInTray;
        _cfg.Browsers = d.Browsers;
        _cfg.Obs = d.Obs;
        _cfg.Profile = d.Profile;
        _cfg.ReopenAppsOnRestore = d.ReopenAppsOnRestore;
        _cfg.SwitchPowerPlan = d.SwitchPowerPlan;
        _cfg.PauseNotifications = d.PauseNotifications;
        _cfg.EnsureGameMode = d.EnsureGameMode;
        _cfg.CheckForUpdates = d.CheckForUpdates;
        _cfg.ProDone.Clear();
        DialogResult = DialogResult.OK;
    }

    static Label SubHeading(string text) => new()
    {
        Text = text, AutoSize = true, Font = Theme.UiFont(10, FontStyle.Bold), ForeColor = Theme.Muted, UseMnemonic = false,
        Margin = new Padding(0, 10, 0, 2),
    };

    static CheckBox Check(string text, bool value) => new()
    {
        Text = text, Checked = value, AutoSize = true, Margin = new Padding(2, 1, 0, 1), UseMnemonic = false,
    };

    static ComboBox Combo(string[] options, int selected)
    {
        var font = Theme.UiFont(9.5f);
        var c = new ComboBox
        {
            Width = options.Max(o => TextRenderer.MeasureText(o, font).Width) + SystemInformation.VerticalScrollBarWidth + 16,
            DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Theme.Panel, ForeColor = Theme.Text,
            FlatStyle = FlatStyle.Flat, Font = font,
        };
        c.Items.AddRange(options);
        c.SelectedIndex = selected;
        return c;
    }
}

/// <summary>
/// "Add an app": pick from what's running now. Protected programs (the game, Steam, FACEIT, Discord, security,
/// drivers) and anything inside the Windows folder are never offered.
/// </summary>
public sealed class AddAppForm : Form
{
    readonly CheckedListBox _list;
    readonly List<(string Name, string Path)> _items = new();

    public List<AppEntry> Chosen { get; } = new();

    public AddAppForm(ISystemOps os, AppConfig cfg)
    {
        Text = "Add an app";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Theme.Apply(this);
        ClientSize = new Size(480, 480);

        var intro = new Label
        {
            Text = "Tick apps to close while you play. Only apps running right now are listed; protected ones are left out.",
            Location = new Point(18, 14), Size = new Size(444, 40), ForeColor = Theme.Muted, Font = Theme.UiFont(9.5f),
        };
        _list = new CheckedListBox
        {
            Location = new Point(18, 60), Size = new Size(444, 350), CheckOnClick = true, BackColor = Theme.Panel,
            ForeColor = Theme.Text, BorderStyle = BorderStyle.None, IntegralHeight = false, AccessibleName = "Running apps",
        };
        var add = Theme.FlatButton("Add", Theme.Green, 100);
        add.Location = new Point(480 - 18 - 100, 426);
        add.Click += (_, _) => { Collect(); DialogResult = DialogResult.OK; };
        var cancel = Theme.FlatButton("Cancel", Theme.Panel, 100);
        cancel.Location = new Point(480 - 18 - 208, 426);
        cancel.DialogResult = DialogResult.Cancel;
        CancelButton = cancel;
        Controls.AddRange(new Control[] { intro, _list, add, cancel });

        var known = new HashSet<string>(cfg.Apps.SelectMany(a => a.Processes), StringComparer.OrdinalIgnoreCase);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows) + "\\";
        var mySession = System.Diagnostics.Process.GetCurrentProcess().SessionId;
        foreach (var p in os.ListProcesses()
                     .Where(p => p.SessionId == mySession && !known.Contains(p.Name) && !Safety.ProtectedList.IsProtectedProcess(p.Name))
                     .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                     .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            string? path;
            try { path = os.GetProcessPath(p); }
            catch (Safety.ProtectedTargetException) { continue; }
            if (string.IsNullOrEmpty(path) || path.StartsWith(windows, StringComparison.OrdinalIgnoreCase)) continue;
            _items.Add((p.Name, path));
            _list.Items.Add($"{p.Name}    ({Path.GetDirectoryName(path)})");
        }
        if (_items.Count == 0) _list.Items.Add("(nothing new to add right now)");
    }

    void Collect()
    {
        foreach (int i in _list.CheckedIndices)
        {
            if (i >= _items.Count) continue;
            var (name, _) = _items[i];
            Chosen.Add(new AppEntry
            {
                Id = "custom-" + name.ToLowerInvariant(), Label = name, Group = SettingsForm.CustomGroup,
                Processes = { name }, Enabled = true, Reopen = false,
            });
        }
    }
}
