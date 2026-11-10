using MatchReady.Boost;
using MatchReady.Config;
using MatchReady.Restore;
using MatchReady.Sys;

namespace MatchReady.UI;

public sealed class MainForm : Form
{
    /// <summary>A second launch (e.g. the Desktop shortcut while we're in the tray) posts this to bring the window back.</summary>
    public static readonly uint ShowMessage = NativeMethods.RegisterWindowMessage("MatchReady.ShowWindow.7f3c2a");

    /// <summary>After CS2 closes, wait this long before restoring, in case it's only restarting.</summary>
    public const int RestoreGraceSeconds = 30;

    readonly ISystemOps _os = new WindowsSystemOps();
    readonly AppConfig _cfg;
    readonly string _configPath = AppPaths.ConfigFile;
    PcInfo? _pc;

    SessionJournal? _active;
    bool _busy;
    bool _closeAfterWork;
    bool _exiting;             // "Exit" from the tray menu: really close (after restoring)
    bool _autoSuppressed;      // "no" while CS2 was running, or restored by hand: wait until CS2 closes
    bool _gameSeenWhileBoosted;
    DateTime? _gameGoneSince;
    bool _startHidden;         // started by "Start with Windows": stay in the tray until opened
    bool _startupDone;
    bool _checkingUpdate;
    string? _updateUrl;

    readonly System.Windows.Forms.Timer _poll = new() { Interval = 3000 };

    // Header
    readonly Button _tabBoost, _tabPro;
    readonly Panel _insider;
    // Boost page
    readonly Panel _boostPage;
    readonly Button _main;
    readonly Panel _card, _cardAccent;
    readonly Label _statusTitle, _statusDetail;
    readonly Panel _warnCard;
    readonly Label _warn;
    readonly CheckBox _auto;
    // Pro page
    readonly Panel _proPage;
    readonly ProPanel _pro;
    // First run
    FirstRunPanel? _firstRun;
    // Tray
    readonly NotifyIcon _tray;
    readonly ToolStripMenuItem _miLast, _miUpdate, _miBoost, _miAuto;

    PcInfo Pc => _pc ??= PcInfo.Detect(_os);

    public MainForm(bool startInTray = false)
    {
        _cfg = AppConfig.Load(_configPath);
        _cfg.StartWithWindows = StartupShortcut.IsEnabled; // the shortcut is the source of truth
        _startHidden = startInTray && _cfg.QuestionsDone;

        Text = "Match Ready";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        Theme.Apply(this);
        ClientSize = new Size(460, 548);
        StartPosition = FormStartPosition.CenterScreen;

        // ---------- Header ----------
        var title = new Label
        {
            Text = "Match Ready", Font = Theme.UiFont(16, FontStyle.Bold), AutoSize = true, Location = new Point(22, 16),
        };
        var version = new Label
        {
            Text = Theme.Version, ForeColor = Theme.Muted, Font = Theme.UiFont(9), AutoSize = true,
            Location = new Point(24 + title.PreferredWidth, 27),
        };
        var subtitle = new Label
        {
            Text = "FACEIT-safe: never touches the game or the anti-cheat.",
            ForeColor = Theme.Muted, AutoSize = true, Location = new Point(24, 50),
        };
        _tabBoost = Theme.FlatButton("Boost", Theme.Panel, 66, 30);
        _tabBoost.Location = new Point(302, 18);
        _tabBoost.AccessibleName = "Boost tab";
        _tabBoost.Click += (_, _) => ShowTab(pro: false);
        _tabPro = Theme.FlatButton("Pro", Theme.Back, 66, 30);
        _tabPro.Location = new Point(370, 18);
        _tabPro.AccessibleName = "Pro tips tab";
        _tabPro.Click += (_, _) => ShowTab(pro: true);

        _insider = new Panel { Location = new Point(24, 82), Size = new Size(412, 96), BackColor = Theme.YellowBack, Visible = false };
        var insiderText = new Label
        {
            Text = Texts.Insider, ForeColor = Theme.Yellow, Location = new Point(10, 8), Size = new Size(392, 64),
            Font = Theme.UiFont(9),
        };
        var insiderOk = Theme.LinkText("Got it");
        insiderOk.Location = new Point(360, 74);
        insiderOk.LinkColor = Theme.Yellow;
        insiderOk.LinkClicked += (_, _) =>
        {
            _cfg.InsiderNoteSeen = true;
            _cfg.Save(_configPath);
            _insider.Visible = false;
            Relayout();
        };
        _insider.Controls.AddRange(new Control[] { insiderText, insiderOk });

        // ---------- Boost page ----------
        _boostPage = new Panel { Location = new Point(24, 84), Size = new Size(412, 500), BackColor = Theme.Back };

        _main = Theme.FlatButton("BOOST MY GAME", Theme.Green, 412, 112);
        _main.Location = new Point(0, 0);
        _main.Font = Theme.UiFont(20, FontStyle.Bold);
        _main.Click += (_, _) => OnMainClick();

        _card = new Panel { Location = new Point(0, 126), Size = new Size(412, 78), BackColor = Theme.Panel };
        _cardAccent = new Panel { Location = new Point(0, 0), Size = new Size(4, 78), BackColor = Theme.Muted };
        _statusTitle = new Label
        {
            Text = "Ready", Location = new Point(16, 10), Size = new Size(386, 24), Font = Theme.UiFont(12, FontStyle.Bold),
        };
        _statusDetail = new Label
        {
            Text = "", Location = new Point(16, 36), Size = new Size(386, 38), ForeColor = Theme.Muted, Font = Theme.UiFont(9.5f),
            UseMnemonic = false,
        };
        _card.Controls.AddRange(new Control[] { _cardAccent, _statusTitle, _statusDetail });
        _card.AccessibleRole = AccessibleRole.StatusBar;

        // Warnings (RTSS, Ryzen Master…) in their own yellow card, only when there is something to say.
        _warnCard = new Panel { Location = new Point(0, 214), Size = new Size(412, 74), BackColor = Theme.YellowBack, Visible = false };
        _warn = new Label
        {
            Text = "", ForeColor = Theme.Yellow, Location = new Point(10, 8), Size = new Size(392, 60), Font = Theme.UiFont(9),
            UseMnemonic = false,
        };
        _warnCard.Controls.Add(_warn);

        _auto = new CheckBox
        {
            Text = "Auto mode", Checked = _cfg.AutoMode, AutoSize = true, Font = Theme.UiFont(10.5f, FontStyle.Bold),
            Location = new Point(0, 500 - 96), Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
            AccessibleDescription = "Boost when CS2 starts and restore when it closes",
        };
        _auto.CheckedChanged += (_, _) => SetAutoMode(_auto.Checked);
        var autoNote = new Label
        {
            Text = "Boosts when CS2 starts and restores when it closes, even with this window closed (it stays in the tray).",
            ForeColor = Theme.Muted, Font = Theme.UiFont(9), Location = new Point(20, 500 - 70), Size = new Size(392, 34),
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
        };

        var footer = new Panel { Location = new Point(0, 500 - 26), Size = new Size(412, 24), Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
        var stopped = Theme.LinkText("What was stopped");
        stopped.Location = new Point(0, 2);
        stopped.LinkClicked += (_, _) => ShowStopped();
        var settings = Theme.LinkText("Settings");
        settings.Location = new Point(176, 2);
        settings.LinkClicked += (_, _) => OpenSettings();
        var help = Theme.LinkText("Help");
        help.Location = new Point(412 - help.PreferredWidth, 2);
        help.LinkClicked += (_, _) => OpenHelp();
        footer.Controls.AddRange(new Control[] { stopped, settings, help });

        _boostPage.Controls.AddRange(new Control[] { _main, _card, _warnCard, _auto, autoNote, footer });

        // ---------- Pro page ----------
        _proPage = new Panel { Location = new Point(24, 84), Size = new Size(412, 500), BackColor = Theme.Back, Visible = false };
        _pro = new ProPanel(_cfg, _os, () => _cfg.Save(_configPath)) { Dock = DockStyle.Fill };
        _proPage.Controls.Add(_pro);

        Controls.AddRange(new Control[] { title, version, subtitle, _tabBoost, _tabPro, _insider, _boostPage, _proPage });

        // ---------- First run / quick setup (also shown once after updating from an older version) ----------
        if (!_cfg.QuestionsDone) ShowFirstRun(showWelcome: !_cfg.FirstRunDone);

        // ---------- Tray ----------
        var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), ShowImageMargin = true, Font = Theme.UiFont(9.5f) };
        var miOpen = new ToolStripMenuItem("Open Match Ready") { Font = Theme.UiFont(9.5f, FontStyle.Bold) };
        miOpen.Click += (_, _) => ShowFromTray();
        _miLast = new ToolStripMenuItem("") { Enabled = false, Visible = false };
        _miUpdate = new ToolStripMenuItem("") { Visible = false };
        _miUpdate.Click += (_, _) => { if (_updateUrl != null) _os.LaunchUnelevated(_updateUrl); };
        _miBoost = new ToolStripMenuItem("Boost now");
        _miBoost.Click += (_, _) => OnMainClick();
        _miAuto = new ToolStripMenuItem("Auto mode") { Checked = _cfg.AutoMode, CheckOnClick = true };
        _miAuto.CheckedChanged += (_, _) => SetAutoMode(_miAuto.Checked);
        var miExit = new ToolStripMenuItem("Exit (restores everything)");
        miExit.Click += (_, _) => ExitApp();
        menu.Items.AddRange(new ToolStripItem[] { miOpen, _miLast, _miUpdate, new ToolStripSeparator(), _miBoost, _miAuto, new ToolStripSeparator(), miExit });
        _tray = new NotifyIcon { Icon = TrayIcons.Gray, Text = "Match Ready: Ready", ContextMenuStrip = menu, Visible = true };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowFromTray(); };
        _tray.BalloonTipClicked += (_, _) =>
        {
            if (_updateUrl != null && _miUpdate.Visible) _os.LaunchUnelevated(_updateUrl);
            else ShowFromTray();
        };

        _poll.Tick += (_, _) => Poll();
        UpdateUi();
        UpdateLastResult();

        if (_startHidden)
        {
            // No window: create the handle (for the tray and messages) and start working right away.
            CreateHandle();
            BeginInvoke(Startup);
        }
    }

    // ================= Lifecycle =================

    protected override void SetVisibleCore(bool value)
    {
        if (_startHidden) value = false;
        base.SetVisibleCore(value);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // We run as admin; the second launch doesn't. Allow its "show yourself" message through.
        NativeMethods.ChangeWindowMessageFilterEx(Handle, ShowMessage, NativeMethods.MSGFLT_ALLOW, IntPtr.Zero);
    }

    protected override void WndProc(ref Message m)
    {
        if (ShowMessage != 0 && m.Msg == (int)ShowMessage)
        {
            ShowFromTray();
            return;
        }
        base.WndProc(ref m);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Startup();
    }

    /// <summary>Runs once, whether the window opened or the app started quietly in the tray.</summary>
    void Startup()
    {
        if (_startupDone) return;
        _startupDone = true;

        var insider = WindowsInfo.IsInsiderBuild();
        Log.Write($"Opened {Theme.Version}{(_startHidden ? " in the tray" : "")}. Windows build {WindowsInfo.BuildText()}{(insider ? " (Insider)" : "")}.");
        _insider.Visible = insider && !_cfg.InsiderNoteSeen;
        Relayout();
        UpdateWarnings();

        // Crash recovery: a session.json left behind means the last boost was never undone.
        var leftover = SessionJournal.TryLoad(AppPaths.SessionFile);
        if (leftover != null)
        {
            _active = leftover;
            SetStatus("Putting things back…", "Found an unfinished boost from last time.");
            RunRestore(recovered: true);
        }
        else
        {
            SetIdleStatus();
            if (_cfg.LoadProblem != null) SetStatus("Settings were reset", _cfg.LoadProblem);
            if (!_startHidden) StartupGameCheck();
        }
        _poll.Start();
    }

    /// <summary>Moves the pages below the yellow note when it's shown. Works in real pixels at any display scale.</summary>
    void Relayout()
    {
        int S(int px) => (int)Math.Round(px * DeviceDpi / 96f);
        var top = _insider.Visible ? _insider.Bottom + S(12) : _insider.Top + S(2);
        foreach (var page in new[] { _boostPage, _proPage })
        {
            page.Location = new Point(_insider.Left, top);
            page.Size = new Size(_insider.Width, ClientSize.Height - top - S(14));
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.WindowsShutDown)
        {
            // Windows is shutting down or logging off: put settings back right now.
            if (_active != null) new RestoreEngine(_os).Restore(_active, shuttingDown: true);
            _tray.Visible = false;
            base.OnFormClosing(e);
            return;
        }
        if (e.CloseReason == CloseReason.UserClosing && _cfg.KeepInTray && !_exiting)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        if (_busy || _active != null)
        {
            e.Cancel = true;
            _closeAfterWork = true;
            if (!_busy) RunRestore(recovered: false);
            return;
        }
        _tray.Visible = false;
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _poll.Dispose();
        }
        base.Dispose(disposing);
    }

    void ExitApp()
    {
        _exiting = true;
        if (_active != null || _busy) ShowFromTray(); // show "Restoring…"
        Close();
        if (_startHidden && !_busy && _active == null) Application.Exit(); // never shown: Close() alone isn't enough
    }

    void HideToTray()
    {
        Hide();
        if (!_cfg.TrayHintShown)
        {
            _cfg.TrayHintShown = true;
            _cfg.Save(_configPath);
            _tray.ShowBalloonTip(5000, "Match Ready is still running",
                "Auto mode keeps working from here. Right-click this icon to exit.", ToolTipIcon.None);
        }
    }

    void ShowFromTray()
    {
        _startHidden = false;
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
        if (_proPage.Visible) _pro.Rebuild();
    }

    // ================= First run =================

    void ShowFirstRun(bool showWelcome)
    {
        var running = _os.ListProcesses().Select(p => p.Name);
        _firstRun = new FirstRunPanel(_cfg, running, Pc, showWelcome, (answers, restorePoint) =>
        {
            answers.ApplyTo(_cfg);
            _cfg.RestorePointAsked = true;
            _cfg.Save(_configPath);
            Log.Write($"Quick setup: browser queue={answers.QueueInBrowser}, OBS={answers.UsesObs}, profile={answers.Profile}, " +
                      $"keep running=[{string.Join(", ", answers.KeepRunning)}], pause notifications={answers.PauseNotifications}.");
            Controls.Remove(_firstRun);
            _firstRun!.Dispose();
            _firstRun = null;
            if (restorePoint) MakeRestorePoint();
            else SetIdleStatus();
        });
        Controls.Add(_firstRun);
        _firstRun.BringToFront();
    }

    // ================= Boost / restore =================

    void StartupGameCheck()
    {
        if (!_cfg.AutoMode || !BoostEngine.IsGameRunning(_os.ListProcesses())) return;
        var go = Theme.Ask(this, "CS2 is running",
            "CS2 is already running. If you're in a match, boosting may close apps (never the game, Steam, FACEIT or Discord). Continue?",
            "Boost now", "Not now");
        if (go) StartBoost(auto: true);
        else _autoSuppressed = true;
    }

    void OnMainClick()
    {
        if (_busy) return;
        if (_active == null) StartBoost(auto: false);
        else
        {
            if (BoostEngine.IsGameRunning(_os.ListProcesses())) _autoSuppressed = true;
            RunRestore(recovered: false);
        }
    }

    void StartBoost(bool auto)
    {
        if (_busy || _active != null) return;
        var procs = _os.ListProcesses();
        var engine = new BoostEngine(_os, _cfg, msg => BeginInvoke(() => SetStatus("Boosting…", msg)));

        // Questions only when the button was pressed. In auto mode CS2 is starting: never steal focus.
        var probe = new List<string>();
        bool browsersOpen = engine.FindApps(procs, new BoostOptions { CloseBrowsers = true }, probe)
            .Any(a => a.entry.Kind == AppKind.Browser);
        bool obsOpen = engine.FindApps(procs, new BoostOptions { CloseRecorder = true }, probe)
            .Any(a => a.entry.Kind == AppKind.Recorder);

        var closeBrowsers = _cfg.Browsers == AskMode.AlwaysClose;
        if (browsersOpen && _cfg.Browsers == AskMode.Ask && !auto && Visible)
            closeBrowsers = Theme.Ask(this, "Close your browsers?",
                "Close your browsers? Unsaved work may be lost.", "Close browsers", "Skip browsers");
        var closeObs = _cfg.Obs == AskMode.AlwaysClose;
        if (obsOpen && _cfg.Obs == AskMode.Ask && !auto && Visible)
            closeObs = Theme.Ask(this, "Close OBS?",
                "OBS Studio is open. If you're recording or streaming, keep it.", "Close OBS", "Keep OBS");

        _gameSeenWhileBoosted = BoostEngine.IsGameRunning(procs);
        _gameGoneSince = null;
        var opt = new BoostOptions { CloseBrowsers = closeBrowsers, CloseRecorder = closeObs, Auto = auto };
        RunWork(() => engine.Run(opt), j =>
        {
            _active = (SessionJournal)j!;
            SetActiveStatus();
        });
    }

    void RunRestore(bool recovered, bool gameClosed = false)
    {
        var j = _active;
        if (j == null) return;
        var engine = new RestoreEngine(_os, msg => BeginInvoke(() => SetStatus("Restoring…", msg)));
        RunWork(() => { engine.Restore(j, reopenApps: _cfg.ReopenAppsOnRestore); return null; }, _ =>
        {
            _active = null;
            _gameSeenWhileBoosted = false;
            _gameGoneSince = null;
            var took = j.EndedUtc is DateTime end ? Duration(end - j.StartedUtc) : "";
            if (recovered)
                SetStatus("Recovered", "The last boost wasn't finished (the app was closed unexpectedly). Everything is back.");
            else
                SetStatus("Restored", (_cfg.ReopenAppsOnRestore
                    ? "Services and settings are back, and closed apps were reopened."
                    : "Services and settings are back. Closed apps stay closed until you open them.") +
                    (took.Length > 0 ? $"  Boost was on for {took}." : ""));
            // One short note after the game has closed: never during play.
            if (gameClosed && !recovered)
                _tray.ShowBalloonTip(4000, "Match Ready: restored",
                    $"Boost was on for {took}. Services and settings are back.", ToolTipIcon.None);
            UpdateLastResult();
            if (recovered && !_startHidden) StartupGameCheck();
        });
    }

    void RunWork(Func<object?> work, Action<object?> done)
    {
        _busy = true;
        UpdateUi();
        Task.Run(() =>
        {
            object? result = null;
            Exception? error = null;
            try { result = work(); }
            catch (Exception ex) { error = ex; }
            BeginInvoke(() =>
            {
                _busy = false;
                if (error != null)
                {
                    Log.Write("Unexpected error: " + error);
                    SetStatus("Something went wrong", BoostEngine.Short(error) + " Press the button again, or reboot.");
                    _active = SessionJournal.TryLoad(AppPaths.SessionFile);
                }
                else
                {
                    done(result);
                }
                UpdateUi();
                UpdateWarnings();
                if (_closeAfterWork)
                {
                    if (_active != null) RunRestore(recovered: false);
                    else
                    {
                        Close();
                        if (_startHidden) Application.Exit();
                    }
                }
            });
        });
    }

    void Poll()
    {
        if (_busy || _firstRun != null) return;
        var procs = _os.ListProcesses();
        var game = BoostEngine.IsGameRunning(procs);
        if (!game) _autoSuppressed = false;

        if (_active != null)
        {
            if (game)
            {
                if (!_gameSeenWhileBoosted || _gameGoneSince != null)
                {
                    _gameSeenWhileBoosted = true;
                    _gameGoneSince = null;
                    SetActiveStatus();
                }
            }
            else if (_gameSeenWhileBoosted && _cfg.AutoMode)
            {
                _gameGoneSince ??= DateTime.UtcNow;
                var left = RestoreGraceSeconds - (int)(DateTime.UtcNow - _gameGoneSince.Value).TotalSeconds;
                if (left <= 0)
                {
                    Log.Write("CS2 closed: auto restore.");
                    RunRestore(recovered: false, gameClosed: true);
                }
                else
                {
                    SetStatus("CS2 closed", $"Restoring in {left} s, in case it's only restarting.");
                }
            }
        }
        else if (game && _cfg.AutoMode && !_autoSuppressed)
        {
            Log.Write("CS2 started: auto boost.");
            StartBoost(auto: true);
        }
        else if (!game)
        {
            MaybeCheckForUpdates();
        }
    }

    void SetAutoMode(bool on)
    {
        if (_cfg.AutoMode == on) return;
        _cfg.AutoMode = on;
        _cfg.Save(_configPath);
        _auto.Checked = on;
        _miAuto.Checked = on;
        if (_active == null && !_busy) SetIdleStatus();
    }

    // ================= Updates (opt-in) =================

    /// <summary>At most once a day, only when idle and CS2 isn't running, and only if it is turned on.</summary>
    void MaybeCheckForUpdates()
    {
        if (!_cfg.CheckForUpdates || _checkingUpdate || _active != null) return;
        if (_cfg.LastUpdateCheckUtc is DateTime last && DateTime.UtcNow - last < TimeSpan.FromHours(24)) return;
        _checkingUpdate = true;
        _cfg.LastUpdateCheckUtc = DateTime.UtcNow;
        _cfg.Save(_configPath);
        var current = typeof(MainForm).Assembly.GetName().Version ?? new Version(0, 0, 0);
        Task.Run(async () =>
        {
            var r = await UpdateChecker.CheckAsync(current);
            BeginInvoke(() =>
            {
                _checkingUpdate = false;
                Log.Write("Update check: " + r.Message);
                if (r.Ok && r.NewerAvailable) ShowUpdate(r);
            });
        });
    }

    void ShowUpdate(UpdateChecker.Result r)
    {
        _updateUrl = r.Url;
        _miUpdate.Text = $"Update available: v{r.Latest}…";
        _miUpdate.Visible = true;
        _tray.ShowBalloonTip(5000, "Match Ready update", $"Version {r.Latest} is available. Click to open the download page.", ToolTipIcon.None);
    }

    // ================= Status =================

    void UpdateUi()
    {
        var boosted = _active != null;
        if (_busy)
        {
            _main.Text = "Working…";
            _main.Enabled = false;
            _cardAccent.BackColor = Theme.Yellow;
        }
        else
        {
            _main.Enabled = true;
            _main.Text = boosted ? "RESTORE EVERYTHING" : "BOOST MY GAME";
            _main.BackColor = boosted ? Theme.Red : Theme.Green;
            _main.FlatAppearance.MouseOverBackColor = boosted ? Theme.RedHover : Theme.GreenHover;
            _cardAccent.BackColor = boosted ? Theme.Green : Theme.Muted;
        }
        _main.AccessibleName = _main.Text;
        _miBoost.Text = boosted ? "Restore everything" : "Boost now";
        _miBoost.Enabled = !_busy;
        _tray.Icon = boosted ? TrayIcons.Green : TrayIcons.Gray;
        var tip = _busy ? "Working…" : boosted ? $"Boost active ({_active!.AppsClosed} apps, {_active.ServicesPaused} services)" : "Ready";
        _tray.Text = Clip("Match Ready: " + tip);
    }

    /// <summary>Tray menu line: what the last finished boost did.</summary>
    void UpdateLastResult()
    {
        var last = SessionJournal.LoadHistory().FirstOrDefault();
        _miLast.Visible = last != null;
        if (last == null) return;
        var took = last.EndedUtc is DateTime end ? "  ·  " + Duration(end - last.StartedUtc) : "";
        _miLast.Text = $"Last boost: {last.AppsClosed} apps, {last.ServicesPaused} services{took}";
    }

    void SetIdleStatus() =>
        SetStatus("Ready", _cfg.AutoMode
            ? "Auto mode is on: I'll boost as soon as CS2 starts."
            : "Press the button before you play.");

    void SetActiveStatus()
    {
        if (_active == null) return;
        var game = BoostEngine.IsGameRunning(_os.ListProcesses());
        var freed = _active.RamFreedBytes > 0 ? $"  ·  freed {FormatBytes(_active.RamFreedBytes)}" : "";
        SetStatus("Boost active",
            $"{_active.AppsClosed} apps closed  ·  {_active.ServicesPaused} services paused{freed}" +
            (game ? "  ·  CS2 running" : "\r\nLaunch CS2 now."));
    }

    void SetStatus(string title, string detail)
    {
        _statusTitle.Text = title;
        _statusDetail.Text = detail;
        _card.AccessibleName = title + ". " + detail;
    }

    void UpdateWarnings()
    {
        var w = BoostEngine.Warnings(_os.ListProcesses());
        _warn.Text = string.Join("\r\n", w);
        _warnCard.Visible = w.Count > 0;
    }

    static string Clip(string s) => s.Length <= 63 ? s : s[..62] + "…";

    public static string FormatBytes(long b) =>
        b >= 1024L * 1024 * 1024 ? $"{b / (1024.0 * 1024 * 1024):0.0} GB" : $"{b / (1024 * 1024)} MB";

    public static string Duration(TimeSpan t) =>
        t.TotalMinutes < 1 ? "under a minute"
        : t.TotalHours < 1 ? $"{(int)t.TotalMinutes} min"
        : $"{(int)t.TotalHours} h {t.Minutes} min";

    // ================= Pages and dialogs =================

    void ShowTab(bool pro)
    {
        _tabBoost.BackColor = pro ? Theme.Back : Theme.Panel;
        _tabPro.BackColor = pro ? Theme.Panel : Theme.Back;
        _boostPage.Visible = !pro;
        _proPage.Visible = pro;
        if (pro) _pro.Rebuild();
    }

    void ShowStopped()
    {
        using var f = new StoppedForm(_active, SessionJournal.LoadHistory(), _cfg, () => _cfg.Save(_configPath));
        f.ShowDialog(this);
    }

    void OpenSettings()
    {
        var wasStartup = _cfg.StartWithWindows;
        using var f = new SettingsForm(_cfg, _os);
        var r = f.ShowDialog(this);
        if (r == DialogResult.Cancel) return;
        if (_cfg.StartWithWindows != wasStartup)
        {
            try { StartupShortcut.Set(_cfg.StartWithWindows); }
            catch (Exception ex)
            {
                _cfg.StartWithWindows = StartupShortcut.IsEnabled;
                Log.Write("Start with Windows failed: " + ex.Message);
            }
        }
        _cfg.Save(_configPath);
        _auto.Checked = _cfg.AutoMode;
        _miAuto.Checked = _cfg.AutoMode;
        if (r == DialogResult.Retry) ShowFirstRun(showWelcome: false);
        else if (_active == null && !_busy) SetIdleStatus();
    }

    void OpenHelp()
    {
        using var f = new HelpForm(_os, _cfg, Pc,
            canSelfCheck: () => _busy ? "Wait for the current step to finish." : _active != null ? "Restore the boost first." : null,
            setBusy: b => { _busy = b; UpdateUi(); },
            onUpdate: r => { if (r.Ok && r.NewerAvailable) ShowUpdate(r); });
        f.ShowDialog(this);
        UpdateLastResult();
    }

    void MakeRestorePoint()
    {
        RunWork(() =>
        {
            BeginInvoke(() => SetStatus("Making a restore point…", "This takes about a minute."));
            var (ok, msg) = WindowsInfo.CreateRestorePoint();
            Log.Write(msg);
            return msg;
        }, r => SetStatus("Ready", (string)r!));
    }
}
