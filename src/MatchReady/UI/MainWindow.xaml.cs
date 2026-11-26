using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MatchReady.Boost;
using MatchReady.Config;
using MatchReady.Restore;
using MatchReady.Sys;

namespace MatchReady.UI;

public partial class MainWindow : Window
{
    /// <summary>A second launch (e.g. the Desktop shortcut while we're in the tray) posts this to bring the window back.</summary>
    public static readonly uint ShowMessage = NativeMethods.RegisterWindowMessage("MatchReady.ShowWindow.7f3c2a");

    /// <summary>After CS2 closes, wait this long before restoring, in case it's only restarting.</summary>
    public const int RestoreGraceSeconds = 30;

    readonly ISystemOps _os = new WindowsSystemOps();
    readonly AppConfig _cfg;
    readonly string _configPath = AppPaths.ConfigFile;
    readonly Tray _tray;
    readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(3) };
    readonly DispatcherTimer _dash = new() { Interval = TimeSpan.FromSeconds(2) };
    readonly SystemMeter _meter = new();
    readonly List<double> _cpuHistory = new();
    PcInfo? _pc;

    SessionJournal? _active;
    bool _busy, _closeAfterWork, _exiting, _autoSuppressed, _gameSeenWhileBoosted, _startupDone, _checkingUpdate;
    DateTime? _gameGoneSince;
    long _ramAtBoost;
    UpdateChecker.Result? _update;
    bool _startHidden;

    PcInfo Pc => _pc ??= PcInfo.Detect(_os);
    public AppConfig Config => _cfg;
    public bool IsBusy => _busy;
    public SessionJournal? ActiveBoost => _active;

    public MainWindow(bool startInTray = false)
    {
        _cfg = AppConfig.Load(_configPath);
        _cfg.StartWithWindows = StartupShortcut.IsEnabled; // the shortcut is the source of truth
        _startHidden = startInTray && _cfg.QuestionsDone;

        InitializeComponent();
        Icon = Ui.AppIcon;
        VersionText.Text = Ui.Version;
        InsiderText.Text = Texts.Insider;
        AutoSwitch.IsChecked = _cfg.AutoMode;
        TabBoost.Checked += (_, _) => ShowTab(pro: false);
        TabPro.Checked += (_, _) => ShowTab(pro: true);

        _tray = new Tray(_cfg.AutoMode);
        _tray.OpenClicked += ShowFromTray;
        _tray.BoostClicked += OnMainButton;
        _tray.AutoChanged += SetAutoMode;
        _tray.ExitClicked += ExitApp;
        _tray.UpdateClicked += () => OpenUpdate();
        Notifier.Init(_tray, ShowFromTray);

        _poll.Tick += (_, _) => Poll();
        _dash.Tick += (_, _) => UpdateDashboard();
        CpuChart.SizeChanged += (_, _) => DrawCpu();

        if (!_cfg.QuestionsDone) ShowFirstRun(showWelcome: !_cfg.FirstRunDone);

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            Ui.DarkTitleBar(hwnd);
            // We run as admin; a second launch doesn't. Let its "show yourself" message through.
            NativeMethods.ChangeWindowMessageFilterEx(hwnd, ShowMessage, NativeMethods.MSGFLT_ALLOW, IntPtr.Zero);
            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        };
        Loaded += (_, _) => Startup();
        Application.Current.SessionEnding += (_, _) => OnWindowsShutdown();

        UpdateUi();
        UpdateLastResult();
        if (_startHidden)
        {
            // Started by "Start with Windows": no window, but a handle for the tray and messages, and work starts now.
            new WindowInteropHelper(this).EnsureHandle();
            Dispatcher.BeginInvoke(Startup);
        }
    }

    // ================= Lifecycle =================

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (ShowMessage != 0 && msg == (int)ShowMessage)
        {
            ShowFromTray();
            handled = true;
        }
        return IntPtr.Zero;
    }

    /// <summary>Runs once, whether the window opened or the app started quietly in the tray.</summary>
    void Startup()
    {
        if (_startupDone) return;
        _startupDone = true;

        var insider = WindowsInfo.IsInsiderBuild();
        Log.Write($"Opened {Ui.Version}{(_startHidden ? " in the tray" : "")}. Windows build {WindowsInfo.BuildText()}{(insider ? " (Insider)" : "")}.");
        InsiderCard.Visibility = insider && !_cfg.InsiderNoteSeen ? Visibility.Visible : Visibility.Collapsed;
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
            if (!_startHidden)
            {
                StartupGameCheck();
                ShowWhatsNewIfUpdated();
            }
        }
        _poll.Start();
    }

    void ShowWhatsNewIfUpdated()
    {
        var current = Ui.Version;
        var last = _cfg.LastSeenVersion;
        _cfg.LastSeenVersion = current;
        _cfg.Save(_configPath);
        if (last != null && last != current && _firstRun == null)
            Dispatcher.BeginInvoke(() => new WhatsNewWindow(this).ShowDialog(), DispatcherPriority.Background);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting && _cfg.KeepInTray)
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
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _tray.Dispose();
        _poll.Stop();
        _dash.Stop();
        base.OnClosed(e);
        Application.Current.Shutdown();
    }

    /// <summary>Windows is shutting down or signing out: put settings back right now.</summary>
    void OnWindowsShutdown()
    {
        _exiting = true; // closing now must really close, not hide in the tray
        if (_active != null) new RestoreEngine(_os).Restore(_active, shuttingDown: true);
        _tray.Dispose();
    }

    public void ExitApp()
    {
        _exiting = true;
        if (_active != null || _busy) ShowFromTray(); // show "Restoring…"
        Close();
    }

    void HideToTray()
    {
        Hide();
        if (!_cfg.TrayHintShown)
        {
            _cfg.TrayHintShown = true;
            _cfg.Save(_configPath);
            Notifier.Show("Match Ready is still running", "Auto mode keeps working from the tray. Right-click the icon to exit.");
        }
    }

    public void ShowFromTray()
    {
        _startHidden = false;
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;  // bring to front reliably, then let go
        Topmost = false;
        if (ProPage.Visibility == Visibility.Visible) ProTips.Fill(ProList, _cfg, _os, SaveConfig);
    }

    public void SaveConfig() => _cfg.Save(_configPath);

    // ================= First run =================

    FirstRunView? _firstRun;

    public void ShowFirstRun(bool showWelcome)
    {
        var running = _os.ListProcesses().Select(p => p.Name);
        _firstRun = new FirstRunView(_cfg, running, Pc, showWelcome, (answers, restorePoint) =>
        {
            answers.ApplyTo(_cfg);
            _cfg.RestorePointAsked = true;
            _cfg.LastSeenVersion = Ui.Version;
            SaveConfig();
            Log.Write($"Quick setup: browser queue={answers.QueueInBrowser}, OBS={answers.UsesObs}, profile={answers.Profile}, " +
                      $"keep running=[{string.Join(", ", answers.KeepRunning)}], pause notifications={answers.PauseNotifications}.");
            Overlay.Content = null;
            Overlay.Visibility = Visibility.Collapsed;
            Shell.Visibility = Visibility.Visible;
            _firstRun = null;
            if (restorePoint) MakeRestorePoint();
            else SetIdleStatus();
        });
        Overlay.Content = _firstRun;
        Overlay.Visibility = Visibility.Visible;
        Shell.Visibility = Visibility.Collapsed;
    }

    // ================= Boost / restore =================

    void StartupGameCheck()
    {
        if (!_cfg.AutoMode || !BoostEngine.IsGameRunning(_os.ListProcesses())) return;
        var go = Ui.Ask(this, "CS2 is running",
            "CS2 is already running. If you're in a match, boosting may close apps (never the game, Steam, FACEIT or Discord). Continue?",
            "Boost now", "Not now");
        if (go) StartBoost(auto: true);
        else _autoSuppressed = true;
    }

    void MainButton_Click(object sender, RoutedEventArgs e) => OnMainButton();

    void OnMainButton()
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
        var engine = new BoostEngine(_os, _cfg, msg => Dispatcher.BeginInvoke(() => SetStatus("Boosting…", msg)));

        // Questions only when the button was pressed with the window open. In auto mode CS2 is starting: never steal focus.
        var probe = new List<string>();
        bool browsersOpen = engine.FindApps(procs, new BoostOptions { CloseBrowsers = true }, probe).Any(a => a.entry.Kind == AppKind.Browser);
        bool obsOpen = engine.FindApps(procs, new BoostOptions { CloseRecorder = true }, probe).Any(a => a.entry.Kind == AppKind.Recorder);

        var closeBrowsers = _cfg.Browsers == AskMode.AlwaysClose;
        if (browsersOpen && _cfg.Browsers == AskMode.Ask && !auto && IsVisible)
            closeBrowsers = Ui.Ask(this, "Close your browsers?", "Close your browsers? Unsaved work may be lost.", "Close browsers", "Skip browsers");
        var closeObs = _cfg.Obs == AskMode.AlwaysClose;
        if (obsOpen && _cfg.Obs == AskMode.Ask && !auto && IsVisible)
            closeObs = Ui.Ask(this, "Close OBS?", "OBS Studio is open. If you're recording or streaming, keep it.", "Close OBS", "Keep OBS");

        _gameSeenWhileBoosted = BoostEngine.IsGameRunning(procs);
        _gameGoneSince = null;
        _ramAtBoost = SystemMeter.FreeRamBytes();
        var opt = new BoostOptions { CloseBrowsers = closeBrowsers, CloseRecorder = closeObs, Auto = auto };
        RunWork(() => engine.Run(opt), j =>
        {
            _active = (SessionJournal)j!;
            SetActiveStatus();
            StartDashboard();
        });
    }

    void RunRestore(bool recovered, bool gameClosed = false)
    {
        var j = _active;
        if (j == null) return;
        var engine = new RestoreEngine(_os, msg => Dispatcher.BeginInvoke(() => SetStatus("Restoring…", msg)));
        RunWork(() => { engine.Restore(j, reopenApps: _cfg.ReopenAppsOnRestore); return null; }, _ =>
        {
            _active = null;
            _gameSeenWhileBoosted = false;
            _gameGoneSince = null;
            StopDashboard();
            var took = j.EndedUtc is DateTime end ? Ui.Duration(end - j.StartedUtc) : "";
            if (recovered)
                SetStatus("Recovered", "The last boost wasn't finished (the app was closed unexpectedly). Everything is back.");
            else
                SetStatus("Restored", (_cfg.ReopenAppsOnRestore
                    ? "Services and settings are back, and closed apps were reopened."
                    : "Services and settings are back. Closed apps stay closed until you open them.") +
                    (took.Length > 0 ? $" Boost was on for {took}." : ""));
            // One short note after the game has closed: never during play.
            if (gameClosed && !recovered)
                Notifier.Show("Restored", $"Boost was on for {took}. Services and settings are back.");
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
            Dispatcher.BeginInvoke(() =>
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
                    else Close();
                }
            });
        });
    }

    void Poll()
    {
        if (_busy || _firstRun != null) return;
        var game = BoostEngine.IsGameRunning(_os.ListProcesses());
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

    void AutoSwitch_Changed(object sender, RoutedEventArgs e)
    {
        if (_tray == null) return; // fired while the window is still being built
        SetAutoMode(AutoSwitch.IsChecked == true);
    }

    void SetAutoMode(bool on)
    {
        if (AutoSwitch.IsChecked != on) AutoSwitch.IsChecked = on;
        _tray.SetAuto(on);
        if (_cfg.AutoMode == on) return;
        _cfg.AutoMode = on;
        SaveConfig();
        if (_active == null && !_busy) SetIdleStatus();
    }

    // ================= Live dashboard =================

    void StartDashboard()
    {
        _cpuHistory.Clear();
        _meter.CpuPercent(); // prime
        Dashboard.Visibility = Visibility.Visible;
        UpdateDashboard();
        _dash.Start();
    }

    void StopDashboard()
    {
        _dash.Stop();
        Dashboard.Visibility = Visibility.Collapsed;
    }

    void UpdateDashboard()
    {
        if (_active == null) return;
        DashTime.Text = Ui.Clock(DateTime.UtcNow - _active.StartedUtc);
        var free = SystemMeter.FreeRamBytes();
        DashRam.Text = free > 0 ? Ui.FormatBytes(free) : "–";
        var gained = _active.RamFreedBytes > 0 ? _active.RamFreedBytes : free - _ramAtBoost;
        DashRamDelta.Text = gained > 100L * 1024 * 1024 ? $"+{Ui.FormatBytes(gained)} from the boost" : "";
        if (_meter.CpuPercent() is double cpu)
        {
            DashCpu.Text = $"{cpu:0}%";
            _cpuHistory.Add(cpu);
            if (_cpuHistory.Count > 60) _cpuHistory.RemoveAt(0);
            DrawCpu();
        }
    }

    void DrawCpu()
    {
        var w = CpuChart.ActualWidth;
        var h = CpuChart.ActualHeight;
        if (w <= 0 || h <= 0) return;
        var pts = new PointCollection();
        var step = w / 59.0;
        var start = 60 - _cpuHistory.Count;
        for (var i = 0; i < _cpuHistory.Count; i++)
            pts.Add(new Point((start + i) * step, h - 2 - (h - 4) * _cpuHistory[i] / 100.0));
        CpuLine.Points = pts;
    }

    // ================= Updates (opt-in) =================

    /// <summary>At most once a day, only when idle and CS2 isn't running, and only if it is turned on.</summary>
    void MaybeCheckForUpdates()
    {
        if (!_cfg.CheckForUpdates || _checkingUpdate || _active != null) return;
        if (_cfg.LastUpdateCheckUtc is DateTime last && DateTime.UtcNow - last < TimeSpan.FromHours(24)) return;
        _checkingUpdate = true;
        _cfg.LastUpdateCheckUtc = DateTime.UtcNow;
        SaveConfig();
        var current = typeof(MainWindow).Assembly.GetName().Version ?? new Version(0, 0, 0);
        Task.Run(async () =>
        {
            var r = await UpdateChecker.CheckAsync(current);
            await Dispatcher.BeginInvoke(() =>
            {
                _checkingUpdate = false;
                Log.Write("Update check: " + r.Message);
                if (r.Ok && r.NewerAvailable) OfferUpdate(r);
            });
        });
    }

    public void OfferUpdate(UpdateChecker.Result r)
    {
        _update = r;
        _tray.SetUpdate($"Update to v{r.Latest}…");
        Notifier.Show("Update available", $"Match Ready {r.Latest} is ready to install. Open the app to update.");
    }

    public void OpenUpdate(UpdateChecker.Result? r = null)
    {
        r ??= _update;
        if (r == null) return;
        ShowFromTray();
        new UpdateWindow(this, r, _os).ShowDialog();
    }

    // ================= Status =================

    void UpdateUi()
    {
        var boosted = _active != null;
        if (_busy)
        {
            MainButton.Content = "Working…";
            MainButton.IsEnabled = false;
            StatusCard.SetResourceReference(BorderBrushProperty, "WarnBrush");
        }
        else
        {
            MainButton.IsEnabled = true;
            MainButton.Content = boosted ? "RESTORE EVERYTHING" : "BOOST MY GAME";
            MainButton.SetResourceReference(BackgroundProperty, boosted ? "DangerBrush" : "AccentBrush");
            StatusCard.SetResourceReference(BorderBrushProperty, boosted ? "AccentBrush" : "MutedBrush");
        }
        System.Windows.Automation.AutomationProperties.SetName(MainButton, MainButton.Content?.ToString() ?? "");
        var tip = _busy ? "Working…" : boosted ? $"Boost active ({_active!.AppsClosed} apps, {_active.ServicesPaused} services)" : "Ready";
        _tray.SetState(boosted, _busy, "Match Ready: " + tip);
    }

    void UpdateLastResult()
    {
        var last = SessionJournal.LoadHistory().FirstOrDefault();
        if (last == null)
        {
            _tray.SetLast(null);
            return;
        }
        var took = last.EndedUtc is DateTime end ? "  ·  " + Ui.Duration(end - last.StartedUtc) : "";
        _tray.SetLast($"Last boost: {last.AppsClosed} apps, {last.ServicesPaused} services{took}");
    }

    void SetIdleStatus() =>
        SetStatus("Ready", _cfg.AutoMode ? "Auto mode is on: the boost starts as soon as CS2 does." : "Press Boost my game before you play.");

    void SetActiveStatus()
    {
        if (_active == null) return;
        var game = BoostEngine.IsGameRunning(_os.ListProcesses());
        var freed = _active.RamFreedBytes > 0 ? $"  ·  freed {Ui.FormatBytes(_active.RamFreedBytes)}" : "";
        SetStatus("Boost active",
            $"{_active.AppsClosed} apps closed  ·  {_active.ServicesPaused} services paused{freed}" +
            (game ? "  ·  CS2 running" : "\nLaunch CS2 now."));
    }

    void SetStatus(string title, string detail)
    {
        StatusTitle.Text = title;
        StatusDetail.Text = detail;
        System.Windows.Automation.AutomationProperties.SetName(StatusCard, title + ". " + detail);
    }

    void UpdateWarnings()
    {
        var w = BoostEngine.Warnings(_os.ListProcesses());
        WarnText.Text = string.Join("\n\n", w);
        WarnCard.Visibility = w.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ================= Pages and dialogs =================

    void ShowTab(bool pro)
    {
        BoostPage.Visibility = pro ? Visibility.Collapsed : Visibility.Visible;
        ProPage.Visibility = pro ? Visibility.Visible : Visibility.Collapsed;
        if (pro) ProTips.Fill(ProList, _cfg, _os, SaveConfig);
    }

    void InsiderOk_Click(object sender, RoutedEventArgs e)
    {
        _cfg.InsiderNoteSeen = true;
        SaveConfig();
        InsiderCard.Visibility = Visibility.Collapsed;
    }

    void Stopped_Click(object sender, RoutedEventArgs e) =>
        new HistoryWindow(this, _active, SessionJournal.LoadHistory(), _cfg, SaveConfig).ShowDialog();

    void Settings_Click(object sender, RoutedEventArgs e)
    {
        var wasStartup = _cfg.StartWithWindows;
        var w = new SettingsWindow(this, _cfg, _os);
        if (w.ShowDialog() != true) return;
        if (_cfg.StartWithWindows != wasStartup)
        {
            try { StartupShortcut.Set(_cfg.StartWithWindows); }
            catch (Exception ex)
            {
                _cfg.StartWithWindows = StartupShortcut.IsEnabled;
                Log.Write("Start with Windows failed: " + ex.Message);
            }
        }
        SaveConfig();
        SetAutoMode(_cfg.AutoMode);
        if (w.RunQuickSetup) ShowFirstRun(showWelcome: false);
        else if (_active == null && !_busy) SetIdleStatus();
    }

    void Help_Click(object sender, RoutedEventArgs e)
    {
        new HelpWindow(this, _os, _cfg, Pc,
            canSelfCheck: () => _busy ? "Wait for the current step to finish." : _active != null ? "Restore the boost first." : null,
            setBusy: b => { _busy = b; UpdateUi(); }).ShowDialog();
        UpdateLastResult();
    }

    void About_Click(object sender, RoutedEventArgs e) => new AboutWindow(this, _os).ShowDialog();

    void MakeRestorePoint()
    {
        RunWork(() =>
        {
            Dispatcher.BeginInvoke(() => SetStatus("Making a restore point…", "This takes about a minute."));
            var (_, msg) = WindowsInfo.CreateRestorePoint();
            Log.Write(msg);
            return msg;
        }, r => SetStatus("Ready", (string)r!));
    }
}
