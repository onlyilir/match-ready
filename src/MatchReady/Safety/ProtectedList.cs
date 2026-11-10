namespace MatchReady.Safety;

/// <summary>
/// Hard-coded list of processes and services the booster must never touch.
/// Nothing in config.json can override this. A trailing '*' means "starts with".
/// </summary>
public static class ProtectedList
{
    public static readonly string[] Processes =
    {
        // The game, Steam, FACEIT and other anti-cheats
        "cs2", "csgo", "steam*", "faceit*", "EasyAntiCheat*", "BEService*", "vgc", "vgtray",
        // Voice chat
        "discord*",
        // Core Windows
        "explorer", "dwm", "csrss", "winlogon", "lsass", "services", "svchost", "smss", "wininit",
        "audiodg", "System", "Idle", "Registry", "Memory Compression", "Secure System",
        "fontdrvhost", "sihost", "ctfmon", "conhost", "RuntimeBroker", "taskhostw", "spoolsv",
        // Security: Windows Security and Norton
        "MsMpEng", "NisSrv", "SecurityHealth*", "smartscreen", "norton*", "ns*Svc",
        // GPU, audio and chipset drivers
        "nvcontainer", "NVDisplay.Container", "nvidia*", "radeon*", "amd*", "atie*", "igfx*", "intel*",
        "realtek*", "rtk*",
        // Peripheral core services (the apps themselves are handled in config)
        "razer*", "lghub_updater",
        // Overlays that hook into games: warn only, never close while the game may be running
        "RTSS*", "EncoderServer*", "MSIAfterburner",
        // Ourselves (and our old name)
        "MatchReady", "CS2Booster",
    };

    /// <summary>
    /// Names that match a protected prefix above but are separate user apps
    /// config.json is allowed to close (all default OFF except the browser).
    /// </summary>
    public static readonly string[] ProcessExemptions =
    {
        "AMDNoiseSuppression", // AMD mic noise suppression: toggle, default keep
        "RazerAppEngine",      // Razer Synapse UI: toggle, default keep
        "NortonBrowser*",      // Norton Private Browser is a web browser, not the antivirus
    };

    public static readonly string[] Services =
    {
        "faceit*", "steam*", "Steam Client Service", "EasyAntiCheat*", "BEService*", "vgc", "vgk",
        "WinDefend", "Sense", "WdNisSvc", "WdBoot", "WdFilter", "SecurityHealthService", "wscsvc",
        "mpssvc", "BFE", "norton*", "ns*Svc",
        "Audiosrv", "AudioEndpointBuilder", "RtkAudioUniversalService", "realtek*",
        "Dhcp", "Dnscache", "NlaSvc", "nsi", "netprofm", "Wcmsvc", "WlanSvc", "LanmanWorkstation", "iphlpsvc",
        "RpcSs", "RpcEptMapper", "DcomLaunch", "PlugPlay", "Power", "EventLog", "gpsvc", "Winmgmt",
        "Schedule", "ProfSvc", "UserManager", "CryptSvc", "BthServ",
        "NVIDIA*", "NVDisplay*", "AMD*", "amdfendr*", "razer*", "discord*",
    };

    public static bool IsProtectedProcess(string name)
    {
        var n = Normalize(name);
        if (n.Length == 0) return true; // no name: refuse
        if (Matches(ProcessExemptions, n)) return false;
        return Matches(Processes, n);
    }

    public static bool IsProtectedService(string name)
    {
        var n = Normalize(name);
        if (n.Length == 0) return true;
        return Matches(Services, n);
    }

    static string Normalize(string? name)
    {
        var n = (name ?? "").Trim();
        if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n[..^4];
        return n;
    }

    static bool Matches(IEnumerable<string> patterns, string name)
    {
        foreach (var p in patterns)
        {
            if (Wildcard(p, name)) return true;
        }
        return false;
    }

    // Supports '*' anywhere (e.g. "ns*Svc"). Case-insensitive.
    static bool Wildcard(string pattern, string text)
    {
        var parts = pattern.Split('*');
        if (parts.Length == 1) return string.Equals(pattern, text, StringComparison.OrdinalIgnoreCase);
        var pos = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Length == 0) continue;
            if (i == 0)
            {
                if (!text.StartsWith(part, StringComparison.OrdinalIgnoreCase)) return false;
                pos = part.Length;
                continue;
            }
            if (i == parts.Length - 1)
            {
                return text.Length - part.Length >= pos &&
                       text.EndsWith(part, StringComparison.OrdinalIgnoreCase);
            }
            var idx = text.IndexOf(part, pos, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return false;
            pos = idx + part.Length;
        }
        return true;
    }
}
