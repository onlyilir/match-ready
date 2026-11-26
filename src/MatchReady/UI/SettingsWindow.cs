using System.Windows;
using System.Windows.Controls;
using MatchReady.Config;
using MatchReady.Sys;

namespace MatchReady.UI;

/// <summary>Settings, grouped under plain headings, with a search box. DialogResult true = saved.</summary>
public sealed class SettingsWindow : Window
{
    public const string CustomGroup = "Your apps";

    readonly AppConfig _cfg;
    readonly ISystemOps _os;
    readonly StackPanel _s;
    readonly TextBox _search;
    readonly List<(CheckBox Box, UIElement? Note, TextBlock Heading, AppEntry App)> _apps = new();
    readonly Dictionary<CheckBox, ServiceEntry> _services = new();
    readonly List<AppEntry> _added = new();
    readonly HashSet<AppEntry> _removed = new();
    TextBlock _mine = null!;
    ComboBox _browsers = null!, _obs = null!, _profile = null!;
    CheckBox _auto = null!, _tray = null!, _reopen = null!, _power = null!, _notify = null!, _gameMode = null!, _startup = null!, _updates = null!;

    /// <summary>"Run quick setup again" was chosen (settings are saved too).</summary>
    public bool RunQuickSetup { get; private set; }

    static readonly string[] AskOptions = { "Ask me first", "Always close", "Never close" }; // order = AskMode
    static readonly string[] ProfileOptions = { "Full (apps + services)", "Light (heavy apps only)" }; // order = BoostProfile

    public SettingsWindow(Window owner, AppConfig cfg, ISystemOps os)
    {
        _cfg = cfg;
        _os = os;
        Width = 580;
        Height = 720;
        Ui.Prepare(this, "Settings", owner);

        var (root, content, bar) = Ui.Page();
        _s = content;
        _search = new TextBox { Tag = "Search apps (e.g. discord, onedrive, razer)" };
        System.Windows.Automation.AutomationProperties.SetName(_search, "Search apps");
        _search.TextChanged += (_, _) => Filter();
        var top = new Border { Padding = new Thickness(24, 16, 24, 0), Child = _search };
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Insert(0, top);

        Ui.AddLeft(bar,
            Ui.Link("Run quick setup again", (_, _) => { Apply(); RunQuickSetup = true; DialogResult = true; }),
            Ui.Link("Reset to defaults", (_, _) => ResetToDefaults()));
        var save = Ui.Button("Save", "PrimaryButton", (_, _) => { Apply(); DialogResult = true; });
        save.IsDefault = true;
        var cancel = Ui.Button("Cancel", "SecondaryButton");
        cancel.IsCancel = true;
        Ui.AddRight(bar, cancel, save);
        Content = root;
        Build();
        Loaded += (_, _) => _search.Focus();
    }

    void Build()
    {
        var s = _s;
        s.Children.Add(Ui.Heading("Apps closed while you play", 0));
        s.Children.Add(Ui.Muted("Ticked = closed during games. Unticked = left running. Unknown apps are never touched."));
        var normal = _cfg.Apps.Where(a => a.Kind == AppKind.Normal).ToList();
        foreach (var group in normal.Where(a => a.Group is not ("Keep running" or CustomGroup)).GroupBy(a => a.Group))
        {
            var h = Ui.SubHeading(group.Key);
            s.Children.Add(h);
            foreach (var a in group) AddApp(a, h);
        }
        var devices = Ui.SubHeading("Your devices and voice");
        s.Children.Add(devices);
        s.Children.Add(Ui.Muted("Kept running unless you tick them."));
        foreach (var a in normal.Where(a => a.Group == "Keep running")) AddApp(a, devices);

        _mine = Ui.SubHeading(CustomGroup);
        s.Children.Add(_mine);
        foreach (var a in normal.Where(a => a.Group == CustomGroup)) AddApp(a, _mine);
        var add = Ui.Link("+ Add an app from what's running now…", (_, _) => AddApps());
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.Margin = new Thickness(0, 4, 0, 4);
        s.Children.Add(add);

        s.Children.Add(Ui.Heading("Browsers and OBS"));
        _browsers = Combo(AskOptions, (int)_cfg.Browsers, "Browsers");
        s.Children.Add(Ui.Row("Browsers (Chrome, Edge, Brave, Opera…)", _browsers));
        _obs = Combo(AskOptions, (int)_cfg.Obs, "OBS Studio");
        s.Children.Add(Ui.Row("OBS Studio", _obs));
        s.Children.Add(Ui.Muted("In Auto mode nothing is ever asked: \"Ask me first\" then means \"leave it open\"."));

        s.Children.Add(Ui.Heading("Windows services paused while you play"));
        s.Children.Add(Ui.Muted("Only paused, never turned off. They start again on restore or reboot. Not used by the Light profile."));
        foreach (var svc in _cfg.Services)
        {
            var cb = Ui.Check(svc.Label, svc.Enabled);
            _services[cb] = svc;
            s.Children.Add(cb);
        }

        s.Children.Add(Ui.Heading("Options"));
        _profile = Combo(ProfileOptions, (int)_cfg.Profile, "How much to do");
        s.Children.Add(Ui.Row("How much to do", _profile));
        _auto = Add(Ui.Check("Auto mode: boost when CS2 starts, restore when it closes", _cfg.AutoMode));
        _tray = Add(Ui.Check("Keep running in the tray when I close the window", _cfg.KeepInTray));
        _startup = Add(Ui.Check("Start with Windows (in the tray)", _cfg.StartWithWindows));
        s.Children.Add(Ui.Muted("Windows asks for permission (the admin prompt) each time you sign in.", new Thickness(28, 0, 0, 4)));
        _power = Add(Ui.Check("Switch to the High performance power plan", _cfg.SwitchPowerPlan));
        _gameMode = Add(Ui.Check("Make sure Windows Game Mode is on", _cfg.EnsureGameMode));
        _notify = Add(Ui.Check("Pause Windows notifications (leave off for FACEIT match alerts)", _cfg.PauseNotifications));
        _reopen = Add(Ui.Check("Reopen closed apps after restore", _cfg.ReopenAppsOnRestore));
        s.Children.Add(Ui.Muted("Off = Match Ready never opens anything; apps come back when you open them or restart.", new Thickness(28, 0, 0, 4)));
        _updates = Add(Ui.Check("Check for updates once a day (only when CS2 isn't running)", _cfg.CheckForUpdates));
        s.Children.Add(Ui.Muted("Asks GitHub for the newest version. Nothing is installed without you clicking Update.", new Thickness(28, 0, 0, 4)));
    }

    CheckBox Add(CheckBox c)
    {
        _s.Children.Add(c);
        return c;
    }

    void AddApp(AppEntry a, TextBlock heading)
    {
        var cb = Ui.Check(a.Label, a.Enabled);
        _s.Children.Add(cb);
        UIElement? note = null;
        if (a.Group == CustomGroup)
        {
            var remove = Ui.Link("remove", (_, _) => { });
            remove.HorizontalAlignment = HorizontalAlignment.Left;
            remove.Margin = new Thickness(28, 0, 0, 4);
            remove.Click += (_, _) =>
            {
                _removed.Add(a);
                cb.Visibility = Visibility.Collapsed;
                remove.Visibility = Visibility.Collapsed;
            };
            note = remove;
        }
        else if (!string.IsNullOrEmpty(a.Note))
        {
            note = Ui.Muted(a.Note!, new Thickness(28, 0, 0, 4));
        }
        if (note != null) _s.Children.Add(note);
        _apps.Add((cb, note, heading, a));
    }

    /// <summary>Shows only apps whose name matches; hides headings with nothing left under them.</summary>
    void Filter()
    {
        var q = _search.Text.Trim();
        foreach (var (box, note, _, app) in _apps)
        {
            if (_removed.Contains(app)) continue;
            var show = q.Length == 0 || app.Label.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                       app.Processes.Any(p => p.Contains(q, StringComparison.OrdinalIgnoreCase));
            box.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (note != null) note.Visibility = box.Visibility;
        }
        foreach (var heading in _apps.Select(x => x.Heading).Distinct())
            heading.Visibility = q.Length == 0 || _apps.Any(x => x.Heading == heading && x.Box.Visibility == Visibility.Visible)
                ? Visibility.Visible : Visibility.Collapsed;
    }

    void AddApps()
    {
        var w = new AddAppWindow(this, _os, _cfg);
        if (w.ShowDialog() != true) return;
        foreach (var a in w.Chosen)
        {
            _added.Add(a);
            var cb = Ui.Check(a.Label, true);
            _s.Children.Insert(_s.Children.IndexOf(_mine) + 1, cb);
            _apps.Add((cb, null, _mine, a));
        }
    }

    void Apply()
    {
        foreach (var a in _added.Where(a => !_cfg.Apps.Contains(a))) _cfg.Apps.Add(a);
        foreach (var (cb, _, _, a) in _apps) a.Enabled = cb.IsChecked == true;
        foreach (var a in _removed) _cfg.Apps.Remove(a);
        foreach (var (cb, svc) in _services) svc.Enabled = cb.IsChecked == true;
        _cfg.Browsers = (AskMode)_browsers.SelectedIndex;
        _cfg.Obs = (AskMode)_obs.SelectedIndex;
        _cfg.Profile = (BoostProfile)_profile.SelectedIndex;
        _cfg.AutoMode = _auto.IsChecked == true;
        _cfg.KeepInTray = _tray.IsChecked == true;
        _cfg.StartWithWindows = _startup.IsChecked == true;
        _cfg.ReopenAppsOnRestore = _reopen.IsChecked == true;
        _cfg.SwitchPowerPlan = _power.IsChecked == true;
        _cfg.PauseNotifications = _notify.IsChecked == true;
        _cfg.EnsureGameMode = _gameMode.IsChecked == true;
        _cfg.CheckForUpdates = _updates.IsChecked == true;
    }

    void ResetToDefaults()
    {
        if (!Ui.Ask(this, "Reset to defaults",
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
        DialogResult = true;
    }

    static ComboBox Combo(string[] options, int selected, string name)
    {
        var c = new ComboBox { MinWidth = 190 };
        System.Windows.Automation.AutomationProperties.SetName(c, name);
        foreach (var o in options) c.Items.Add(o);
        c.SelectedIndex = selected;
        return c;
    }
}

/// <summary>
/// "Add an app": pick from what's running now. Protected programs (the game, Steam, FACEIT, Discord, security,
/// drivers) and anything inside the Windows folder are never offered.
/// </summary>
public sealed class AddAppWindow : Window
{
    readonly List<(string Name, CheckBox Box)> _items = new();

    public List<AppEntry> Chosen { get; } = new();

    public AddAppWindow(Window owner, ISystemOps os, AppConfig cfg)
    {
        Width = 520;
        Height = 560;
        Ui.Prepare(this, "Add an app", owner);
        var (root, content, bar) = Ui.Page();
        content.Children.Add(Ui.Muted("Tick apps to close while you play. Only apps running right now are listed; protected ones are left out."));

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
            var cb = Ui.Check(p.Name, false);
            content.Children.Add(cb);
            content.Children.Add(Ui.Muted(Path.GetDirectoryName(path) ?? "", new Thickness(28, 0, 0, 4)));
            _items.Add((p.Name, cb));
        }
        if (_items.Count == 0) content.Children.Add(Ui.Body("Nothing new to add right now."));

        var add = Ui.Button("Add", "PrimaryButton", (_, _) => { Collect(); DialogResult = true; });
        add.IsDefault = true;
        var cancel = Ui.Button("Cancel");
        cancel.IsCancel = true;
        Ui.AddRight(bar, cancel, add);
        Content = root;
    }

    void Collect()
    {
        foreach (var (name, box) in _items.Where(i => i.Box.IsChecked == true))
        {
            Chosen.Add(new AppEntry
            {
                Id = "custom-" + name.ToLowerInvariant(), Label = name, Group = SettingsWindow.CustomGroup,
                Processes = { name }, Enabled = true, Reopen = false,
            });
        }
    }
}
