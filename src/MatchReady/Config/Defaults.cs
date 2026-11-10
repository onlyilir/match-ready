namespace MatchReady.Config;

/// <summary>
/// Default allow-list. Only names listed here are ever closed; unknown processes are never touched.
/// Software that runs mice, keyboards, headsets, mics or fans is kept running unless the user ticks it
/// (Quick setup pre-fills this from what is installed on the PC).
/// </summary>
public static class Defaults
{
    public static AppConfig Create()
    {
        var c = new AppConfig { SettingsVersion = AppConfig.CurrentSettingsVersion };

        // Heavy apps: probably the biggest win (they can hold VRAM and RAM)
        App(c, "ollama", "Ollama (AI models)", "Heavy apps", true, "ollama app", "ollama");
        App(c, "lmstudio", "LM Studio (AI models)", "Heavy apps", true, "LM Studio", "lms");
        App(c, "wallpaperengine", "Wallpaper Engine", "Heavy apps", true, "wallpaper32", "wallpaper64", "webwallpaper32");
        App(c, "3dmark", "3DMark", "Heavy apps", true, "3DMark").Reopen = false;
        App(c, "unityhub", "Unity Hub", "Heavy apps", true, "Unity Hub").Reopen = false;

        // Browsers: always asked first (unsaved work), never reopened
        Browser(c, "chrome", "Google Chrome", "chrome");
        Browser(c, "edge", "Microsoft Edge", "msedge");
        Browser(c, "firefox", "Firefox", "firefox");
        Browser(c, "brave", "Brave", "brave");
        Browser(c, "opera", "Opera", "opera");
        Browser(c, "nortonbrowser", "Norton Private Browser", "NortonBrowser");

        var obs = App(c, "obs", "OBS Studio", "Recording", true, "obs64", "obs32");
        obs.Kind = AppKind.Recorder;
        obs.Reopen = false;
        obs.Note = "Asked first: you may be recording.";

        // Cloud and sync
        App(c, "onedrive", "OneDrive", "Cloud & sync", true, "OneDrive");
        App(c, "dropbox", "Dropbox", "Cloud & sync", true, "Dropbox");
        App(c, "googledrive", "Google Drive", "Cloud & sync", true, "GoogleDriveFS");

        // Launchers (never Steam)
        App(c, "epic", "Epic Games Launcher", "Launchers", true, "EpicGamesLauncher", "EpicWebHelper");
        App(c, "ea", "EA app", "Launchers", true, "EADesktop", "EALauncher");
        App(c, "battlenet", "Battle.net", "Launchers", true, "Battle.net");
        var rockstar = App(c, "rockstar", "Rockstar Games Launcher", "Launchers", true, "Launcher", "SocialClubHelper", "RockstarService");
        rockstar.PathContains = "Rockstar Games";
        App(c, "ubisoft", "Ubisoft Connect", "Launchers", true, "upc", "UbisoftConnect");
        App(c, "gog", "GOG Galaxy", "Launchers", true, "GalaxyClient");

        // Chat and media (Discord is protected and never closed)
        App(c, "teams", "Microsoft Teams", "Chat & media", true, "ms-teams", "Teams");
        App(c, "skype", "Skype", "Chat & media", true, "Skype");
        App(c, "slack", "Slack", "Chat & media", true, "slack");
        App(c, "whatsapp", "WhatsApp", "Chat & media", true, "WhatsApp");
        App(c, "telegram", "Telegram", "Chat & media", true, "Telegram");
        App(c, "spotify", "Spotify", "Chat & media", false, "Spotify").Note = "Off by default: you may want music.";

        // Remote access: worth closing during FACEIT matches
        App(c, "teamviewer", "TeamViewer (app)", "Remote access", true, "TeamViewer");

        // Windows extras
        App(c, "widgets", "Windows Widgets", "Windows extras", true, "Widgets", "WidgetService").Reopen = false;
        App(c, "copilot", "Copilot / Cortana", "Windows extras", true, "Copilot", "Cortana").Reopen = false;
        App(c, "phonelink", "Phone Link", "Windows extras", true, "PhoneExperienceHost", "YourPhone").Reopen = false;
        App(c, "xboxapp", "Xbox app", "Windows extras", true, "XboxPcApp").Reopen = false;
        App(c, "adobe", "Adobe updaters", "Windows extras", true, "AdobeARM", "AdobeCollabSync", "CCXProcess").Reopen = false;

        // Device, lighting and voice software: kept running by default (it may run a mouse, headset, mic or fans).
        // Quick setup asks which ones the user needs; unticked ones are closed during games.
        App(c, "steelseries", "SteelSeries GG", "Keep running", false,
            "SteelSeriesGG", "SteelSeriesGGClient", "SteelSeriesEngine", "SteelSeriesPrism")
            .Note = "Only close it if your mouse/keyboard profiles are saved on the device itself.";
        App(c, "razer", "Razer Synapse", "Keep running", false, "RazerAppEngine")
            .Note = "Needed for some Razer headset features, such as THX Spatial Audio.";
        App(c, "icue", "Corsair iCUE", "Keep running", false, "iCUE", "iCUEDevicePluginHost")
            .Note = "If closed, Corsair lighting may go back to its default until iCUE opens again.";
        App(c, "nzxtcam", "NZXT CAM", "Keep running", false, "NZXT CAM")
            .Note = "May control fans and pump. Only close it if it's lighting-only on your PC.";
        App(c, "ghub", "Logitech G HUB", "Keep running", false, "lghub", "lghub_agent", "lghub_system_tray")
            .Note = "Only close it if you have no Logitech device, or its profile is saved on the device.";
        // Never reopened: it needs admin, and Windows starts it again at the next sign-in.
        var gcc = App(c, "gigabyte", "Gigabyte Control Center", "Keep running", false, "GCC", "GigabyteControlCenter", "RGBFusion");
        gcc.Reopen = false;
        gcc.Note = "Motherboard lighting and tools. Usually safe to close during games.";
        App(c, "amdnoise", "AMD Noise Suppression (mic)", "Keep running", false, "AMDNoiseSuppression")
            .Note = "Keeps your mic clean in voice chat.";

        // Services: stop only, never disabled. They start again on restore or reboot.
        Svc(c, "wuauserv", "Windows Update");
        Svc(c, "UsoSvc", "Update Orchestrator");
        Svc(c, "WaaSMedicSvc", "Windows Update Medic (often refuses: skipped)");
        Svc(c, "DoSvc", "Delivery Optimization");
        Svc(c, "BITS", "Background downloads (BITS)");
        Svc(c, "WSearch", "Search indexing");
        Svc(c, "Spooler", "Print Spooler", "no-print-jobs");
        Svc(c, "DiagTrack", "Telemetry");
        Svc(c, "dmwappushservice", "Device management push");
        Svc(c, "SysMain", "SysMain / Superfetch (SSD only)", "ssd");
        Svc(c, "TeamViewer", "TeamViewer remote access service");

        return c;
    }

    static AppEntry App(AppConfig c, string id, string label, string group, bool enabled, params string[] processes)
    {
        var e = new AppEntry { Id = id, Label = label, Group = group, Enabled = enabled, Processes = processes.ToList() };
        c.Apps.Add(e);
        return e;
    }

    static void Browser(AppConfig c, string id, string label, string process)
    {
        var e = App(c, id, label, "Browsers", true, process);
        e.Kind = AppKind.Browser;
        e.Reopen = false;
    }

    static void Svc(AppConfig c, string name, string label, string? condition = null) =>
        c.Services.Add(new ServiceEntry { Name = name, Label = label, Condition = condition });
}
