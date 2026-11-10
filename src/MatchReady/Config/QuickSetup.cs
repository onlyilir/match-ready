namespace MatchReady.Config;

/// <summary>Answers from the first-run questions, turned into settings.</summary>
public sealed class QuickSetup
{
    /// <summary>
    /// Software that may run a mouse, keyboard, headset, mic or fans. Ticked in the questions = keep running.
    /// InstalledAs = words in its "Installed apps" name, used to show only what this PC actually has.
    /// </summary>
    public static readonly (string Id, string Label, string[] InstalledAs)[] Devices =
    {
        ("steelseries", "SteelSeries GG", new[] { "SteelSeries GG" }),
        ("razer", "Razer Synapse", new[] { "Razer Synapse" }),
        ("icue", "Corsair iCUE", new[] { "iCUE" }),
        ("nzxtcam", "NZXT CAM", new[] { "NZXT CAM" }),
        ("ghub", "Logitech G HUB", new[] { "Logitech G HUB" }),
        ("gigabyte", "Gigabyte Control Center", new[] { "GIGABYTE Control Center", "RGB Fusion" }),
        ("amdnoise", "AMD Noise Suppression (mic)", new[] { "AMD Software" }),
    };

    /// <summary>Device software found on this PC (installed, or running right now).</summary>
    public static List<(string Id, string Label)> FoundOn(Sys.PcInfo pc, IEnumerable<string> runningNames, AppConfig c)
    {
        var running = new HashSet<string>(runningNames, StringComparer.OrdinalIgnoreCase);
        return Devices
            .Where(d => pc.HasInstalled(d.InstalledAs) ||
                        (c.Apps.FirstOrDefault(a => a.Id == d.Id)?.Processes.Any(running.Contains) ?? false))
            .Select(d => (d.Id, d.Label))
            .ToList();
    }

    public bool QueueInBrowser { get; set; }
    public bool UsesObs { get; set; }
    public HashSet<string> KeepRunning { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool PauseNotifications { get; set; }
    public BoostProfile Profile { get; set; } = BoostProfile.Full;

    /// <summary>Pre-fills the questions from the current settings.</summary>
    public static QuickSetup From(AppConfig c) => new()
    {
        QueueInBrowser = c.Browsers == AskMode.NeverClose,
        UsesObs = c.Obs == AskMode.NeverClose,
        KeepRunning = new HashSet<string>(
            Devices.Where(d => c.Apps.FirstOrDefault(a => a.Id == d.Id) is { Enabled: false }).Select(d => d.Id),
            StringComparer.OrdinalIgnoreCase),
        PauseNotifications = c.QuestionsDone && c.PauseNotifications,
        Profile = c.Profile,
    };

    public void ApplyTo(AppConfig c)
    {
        // Browser queue: never close the browser (the FACEIT tab lives there).
        c.Browsers = QueueInBrowser ? AskMode.NeverClose : AskMode.Ask;
        // Recording: never close OBS mid-recording.
        c.Obs = UsesObs ? AskMode.NeverClose : AskMode.Ask;
        c.Profile = Profile;
        foreach (var (id, _, _) in Devices)
        {
            var app = c.Apps.FirstOrDefault(a => a.Id == id);
            if (app != null) app.Enabled = !KeepRunning.Contains(id);
        }
        c.PauseNotifications = PauseNotifications;
        c.QuestionsDone = true;
        c.FirstRunDone = true;
    }
}
