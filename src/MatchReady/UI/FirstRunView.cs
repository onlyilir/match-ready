using System.Windows;
using System.Windows.Controls;
using MatchReady.Config;
using MatchReady.Sys;

namespace MatchReady.UI;

/// <summary>
/// First launch: "What I will do", then a few quick questions so the app fits this PC without digging
/// through Settings. It shows what it detected (hardware, installed device software) to pre-fill answers.
/// </summary>
public sealed class FirstRunView : UserControl
{
    readonly AppConfig _cfg;
    readonly HashSet<string> _running;
    readonly PcInfo _pc;
    readonly bool _offerRestorePoint;
    readonly Action<QuickSetup, bool> _done;
    readonly StackPanel _content;
    readonly ScrollViewer _scroll;
    readonly Button _next;
    Action? _onNext;

    RadioButton? _browser, _obsYes, _light;
    readonly Dictionary<string, CheckBox> _devices = new();
    CheckBox? _notify, _restorePoint;

    public FirstRunView(AppConfig cfg, IEnumerable<string> runningProcessNames, PcInfo pc, bool showWelcome, Action<QuickSetup, bool> done)
    {
        _cfg = cfg;
        _running = new HashSet<string>(runningProcessNames, StringComparer.OrdinalIgnoreCase);
        _pc = pc;
        _offerRestorePoint = !cfg.RestorePointAsked;
        _done = done;

        var root = new DockPanel { Margin = new Thickness(24, 18, 24, 18) };
        _next = Ui.Button("Next", "PrimaryButton", (_, _) => _onNext?.Invoke());
        _next.MinHeight = 46;
        _next.FontSize = 16;
        _next.IsDefault = true;
        DockPanel.SetDock(_next, Dock.Bottom);
        root.Children.Add(_next);
        _content = new StackPanel { Margin = new Thickness(0, 0, 8, 12) };
        _scroll = new ScrollViewer { Content = _content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
        root.Children.Add(_scroll);
        Content = root;

        if (showWelcome) ShowWelcome();
        else ShowQuestions();
    }

    void Reset(string button, Action onNext)
    {
        _content.Children.Clear();
        _scroll.ScrollToTop();
        _next.Content = button;
        System.Windows.Automation.AutomationProperties.SetName(_next, button);
        _onNext = onNext;
    }

    void ShowWelcome()
    {
        Reset("Next", ShowQuestions);
        _content.Children.Add(new TextBlock { Text = "Welcome to Match Ready", Style = Ui.S("Title"), Margin = new Thickness(0, 0, 0, 12) });
        _content.Children.Add(Ui.Body(Texts.WhatIWillDo));
        Dispatcher.BeginInvoke(() => _next.Focus());
    }

    void ShowQuestions()
    {
        var a = QuickSetup.From(_cfg);
        Reset("Save and start", Finish);

        _content.Children.Add(new TextBlock { Text = "Quick setup", Style = Ui.S("Title") });
        _content.Children.Add(Ui.Muted("So Match Ready fits your PC. You can change all of this later in Settings.", new Thickness(0, 2, 0, 0)));
        if (_pc.Summary.Length > 0)
        {
            var card = new StackPanel();
            card.Children.Add(new TextBlock { Text = "Your PC", Style = Ui.S("H3"), Margin = new Thickness(0) });
            card.Children.Add(Ui.Body(_pc.Summary));
            if (_pc.Expectation.Length > 0) card.Children.Add(Ui.Muted(_pc.Expectation, new Thickness(0, 4, 0, 0)));
            _content.Children.Add(new Border { Style = Ui.S("Card"), Margin = new Thickness(0, 12, 0, 0), Child = card });
        }

        _content.Children.Add(Ui.Heading("Where do you queue for FACEIT?"));
        (_, _browser) = Radios("q1", a.QueueInBrowser, "The FACEIT app (or I don't use FACEIT)", "A web browser (Chrome, Edge…)");

        _content.Children.Add(Ui.Heading("Do you record or stream with OBS?"));
        (_, _obsYes) = Radios("q2", a.UsesObs, "No", "Yes, sometimes (never close OBS)");

        _content.Children.Add(Ui.Heading("How much should it do?"));
        (_, _light) = Radios("q3", a.Profile == BoostProfile.Light,
            "Full: background apps and Windows services (recommended)", "Light: only heavy apps (AI models, wallpapers)");

        AddDevices(a);

        _content.Children.Add(Ui.Heading("Notifications"));
        _notify = Ui.Check("Pause Windows notifications while CS2 runs", a.PauseNotifications);
        _content.Children.Add(_notify);
        _content.Children.Add(Ui.Muted("Leave this off if you want FACEIT \"match found\" alerts to pop up.", new Thickness(28, 0, 0, 4)));

        if (_offerRestorePoint)
        {
            _content.Children.Add(Ui.Heading("Safety net"));
            _restorePoint = Ui.Check("Make a Windows restore point now (recommended)", true);
            _content.Children.Add(_restorePoint);
        }
        Dispatcher.BeginInvoke(() => _next.Focus());
    }

    /// <summary>Two radio buttons as one group. Returns (first, second); the second is checked if <paramref name="second"/>.</summary>
    (RadioButton, RadioButton) Radios(string group, bool second, string a, string b)
    {
        var r1 = new RadioButton { Content = new TextBlock { Text = a, TextWrapping = TextWrapping.Wrap }, GroupName = group, IsChecked = !second };
        var r2 = new RadioButton { Content = new TextBlock { Text = b, TextWrapping = TextWrapping.Wrap }, GroupName = group, IsChecked = second };
        System.Windows.Automation.AutomationProperties.SetName(r1, a);
        System.Windows.Automation.AutomationProperties.SetName(r2, b);
        _content.Children.Add(r1);
        _content.Children.Add(r2);
        return (r1, r2);
    }

    /// <summary>Only the device software this PC has. "Show N more" reveals the rest.</summary>
    void AddDevices(QuickSetup a)
    {
        var found = QuickSetup.FoundOn(_pc, _running, _cfg).Select(f => f.Id).ToHashSet();
        _content.Children.Add(Ui.Heading("Keep these running while you play"));
        _content.Children.Add(Ui.Muted(found.Count > 0
            ? "Found on this PC. Tick the ones that run your mouse, keyboard, headset, mic or fans. Unticked ones are closed during games."
            : "No mouse, headset or lighting software was found on this PC."));

        var hidden = new List<CheckBox>();
        foreach (var (id, label, _) in QuickSetup.Devices)
        {
            var app = _cfg.Apps.FirstOrDefault(x => x.Id == id);
            var running = app != null && app.Processes.Any(_running.Contains);
            // Default "keep" for everything (also software installed later); the saved choice wins after the first setup.
            var cb = Ui.Check(label + (running ? "   · running now" : ""), !_cfg.QuestionsDone || a.KeepRunning.Contains(id));
            cb.Visibility = found.Contains(id) ? Visibility.Visible : Visibility.Collapsed;
            _devices[id] = cb;
            _content.Children.Add(cb);
            if (cb.Visibility != Visibility.Visible) hidden.Add(cb);
        }
        if (hidden.Count > 0)
        {
            Button? more = null;
            more = Ui.Link($"Show {hidden.Count} more", (_, _) =>
            {
                foreach (var cb in hidden) cb.Visibility = Visibility.Visible;
                more!.Visibility = Visibility.Collapsed;
            });
            more.Margin = new Thickness(0, 4, 0, 0);
            more.HorizontalAlignment = HorizontalAlignment.Left;
            _content.Children.Add(more);
        }
    }

    void Finish()
    {
        var answers = new QuickSetup
        {
            QueueInBrowser = _browser?.IsChecked == true,
            UsesObs = _obsYes?.IsChecked == true,
            Profile = _light?.IsChecked == true ? BoostProfile.Light : BoostProfile.Full,
            KeepRunning = new HashSet<string>(_devices.Where(d => d.Value.IsChecked == true).Select(d => d.Key), StringComparer.OrdinalIgnoreCase),
            PauseNotifications = _notify?.IsChecked == true,
        };
        _done(answers, _restorePoint?.IsChecked == true);
    }
}
