using System.Text.Json;
using System.Text.Json.Serialization;
using MatchReady.Safety;

namespace MatchReady.Config;

public enum AppKind { Normal, Browser, Recorder }

/// <summary>How to treat apps that need a question first (browsers, OBS).</summary>
public enum AskMode { Ask, AlwaysClose, NeverClose }

/// <summary>Full: everything. Light: only close the heavy apps (AI models, wallpaper engines, benchmarks).</summary>
public enum BoostProfile { Full, Light }

public sealed class AppEntry
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Group { get; set; } = "";
    /// <summary>Process names without ".exe". The first one found running is the one reopened on restore.</summary>
    public List<string> Processes { get; set; } = new();
    /// <summary>Optional: only match when the exe path contains this text (for generic names like "Launcher").</summary>
    public string? PathContains { get; set; }
    public bool Enabled { get; set; }
    public bool Reopen { get; set; } = true;
    public AppKind Kind { get; set; } = AppKind.Normal;
    public string? Note { get; set; }
}

public sealed class ServiceEntry
{
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";
    public bool Enabled { get; set; } = true;
    /// <summary>"ssd" = only on an SSD; "no-print-jobs" = only when the print queue is empty.</summary>
    public string? Condition { get; set; }
}

public sealed class AppConfig
{
    public bool AutoMode { get; set; } = true;
    public AskMode Browsers { get; set; } = AskMode.Ask;
    public AskMode Obs { get; set; } = AskMode.Ask;
    /// <summary>Off: closed apps stay closed after CS2 until they are opened again (or restarts the PC).</summary>
    public bool ReopenAppsOnRestore { get; set; }

    /// <summary>Bumped when a default changes in a way existing config.json files must pick up.</summary>
    public const int CurrentSettingsVersion = 2;
    public int SettingsVersion { get; set; }
    public bool SwitchPowerPlan { get; set; } = true;
    /// <summary>Off by default so FACEIT "match found" alerts still come through.</summary>
    public bool PauseNotifications { get; set; }
    public BoostProfile Profile { get; set; } = BoostProfile.Full;
    /// <summary>Mirrors the Start-up folder shortcut (the shortcut is the source of truth).</summary>
    public bool StartWithWindows { get; set; }
    /// <summary>Opt-in: look for a newer release on GitHub at most once a day, never while CS2 runs.</summary>
    public bool CheckForUpdates { get; set; }
    public DateTime? LastUpdateCheckUtc { get; set; }
    public bool EnsureGameMode { get; set; } = true;
    /// <summary>Closing the window keeps the booster in the tray (Auto mode keeps working). Exit is in the tray menu.</summary>
    public bool KeepInTray { get; set; } = true;

    // One-time UI flags
    public bool FirstRunDone { get; set; }
    public bool QuestionsDone { get; set; }
    public bool TrayHintShown { get; set; }
    public bool InsiderNoteSeen { get; set; }
    public bool RestorePointAsked { get; set; }

    /// <summary>Pro tab: ids of the manual tips ticked off.</summary>
    public List<string> ProDone { get; set; } = new();

    public List<AppEntry> Apps { get; set; } = new();
    public List<ServiceEntry> Services { get; set; } = new();

    /// <summary>Set when config.json was unreadable and defaults were used (the broken file is kept as .bak).</summary>
    [JsonIgnore] public string? LoadProblem { get; private set; }

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static AppConfig Load(string path)
    {
        AppConfig cfg;
        string? problem = null;
        try
        {
            cfg = File.Exists(path)
                ? JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), Json) ?? Defaults.Create()
                : Defaults.Create();
        }
        catch (Exception ex)
        {
            Log.Write($"config.json could not be read ({ex.Message}). Using defaults; the broken file is kept as config.json.bak.");
            try { File.Copy(path, path + ".bak", overwrite: true); } catch { /* best effort */ }
            cfg = Defaults.Create();
            problem = "Your settings file was damaged, so defaults are being used. The old file was kept as config.json.bak.";
        }
        cfg.LoadProblem = problem;
        cfg.Migrate();
        cfg.MergeDefaults();
        cfg.StripProtected();
        return cfg;
    }

    void Migrate()
    {
        if (SettingsVersion < 2)
        {
            // v1.0.0 reopened closed apps after CS2, which popped up windows nobody had opened.
            ReopenAppsOnRestore = false;
        }
        SettingsVersion = CurrentSettingsVersion;
    }

    public void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Write($"config.json could not be saved: {ex.Message}");
        }
    }

    /// <summary>Adds entries that exist in a newer default list but not in the user's file.</summary>
    void MergeDefaults()
    {
        var d = Defaults.Create();
        foreach (var a in d.Apps)
        {
            if (!Apps.Any(x => string.Equals(x.Id, a.Id, StringComparison.OrdinalIgnoreCase))) Apps.Add(a);
        }
        foreach (var s in d.Services)
        {
            if (!Services.Any(x => string.Equals(x.Name, s.Name, StringComparison.OrdinalIgnoreCase))) Services.Add(s);
        }
    }

    /// <summary>Removes any protected name someone typed into config.json. The guard would refuse it anyway.</summary>
    void StripProtected()
    {
        foreach (var a in Apps)
        {
            var bad = a.Processes.Where(ProtectedList.IsProtectedProcess).ToList();
            foreach (var b in bad)
            {
                Log.Write($"config.json: ignoring protected process '{b}' in '{a.Label}'.");
                a.Processes.Remove(b);
            }
        }
        foreach (var s in Services.Where(s => ProtectedList.IsProtectedService(s.Name)).ToList())
        {
            Log.Write($"config.json: ignoring protected service '{s.Name}'.");
            Services.Remove(s);
        }
    }
}
