using System.Text.Json;
using System.Text.Json.Serialization;

namespace MatchReady.Restore;

public enum SessionState { Boosting, Active, Restoring, Restored }

public sealed class RegistryChange
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Existed { get; set; }
    public int OldValue { get; set; }
    public int NewValue { get; set; }
    public bool Applied { get; set; }
    public string Label { get; set; } = "";
}

public sealed class ServiceChange
{
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";
    public bool WasRunning { get; set; }
    public bool Stopped { get; set; }
    public string? Note { get; set; }
}

public sealed class AppChange
{
    /// <summary>The allow-list entry this came from (for "Keep running next time"). Empty in journals from v1.0.</summary>
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Process { get; set; } = "";
    public string? ExePath { get; set; }
    public bool Reopen { get; set; }
    public bool Closed { get; set; }
    public string? How { get; set; }
}

public sealed class PowerChange
{
    public Guid Original { get; set; }
    public string OriginalName { get; set; } = "";
    public Guid Applied { get; set; }
    public string AppliedName { get; set; } = "";
    /// <summary>Set when we had to create a temporary copy of High Performance. Deleted on restore.</summary>
    public Guid? CreatedTemp { get; set; }
    public bool Done { get; set; }
}

/// <summary>
/// The crash-safe record of everything the boost changed. Each change is written here
/// BEFORE it happens, so a crash or a Task Manager kill can always be undone on next start.
/// </summary>
public sealed class SessionJournal
{
    public int Version { get; set; } = 1;
    public SessionState State { get; set; } = SessionState.Boosting;
    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? EndedUtc { get; set; }
    /// <summary>Made by the self-check (no apps closed). Left out of the history list.</summary>
    public bool IsSelfCheck { get; set; }
    /// <summary>Free RAM gained by closing apps (measured, approximate).</summary>
    public long RamFreedBytes { get; set; }
    public bool Auto { get; set; }
    /// <summary>Settings were put back during a Windows shutdown/logoff; services and apps are finished on next start.</summary>
    public bool ShutdownRestore { get; set; }
    public PowerChange? Power { get; set; }
    public List<RegistryChange> Registry { get; set; } = new();
    public List<ServiceChange> Services { get; set; } = new();
    public List<AppChange> Apps { get; set; } = new();
    public List<string> Skipped { get; set; } = new();

    [JsonIgnore] public string? FilePath { get; set; }

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public int AppsClosed => Apps.Count(a => a.Closed);
    public int ServicesPaused => Services.Count(s => s.Stopped);

    /// <summary>Atomic write: a crash mid-save never leaves a half-written journal.</summary>
    public void Save()
    {
        if (FilePath == null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, FilePath, overwrite: true);
    }

    public static SessionJournal? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var j = JsonSerializer.Deserialize<SessionJournal>(File.ReadAllText(path), Json);
            if (j != null) j.FilePath = path;
            return j;
        }
        catch (Exception ex)
        {
            Log.Write($"session.json could not be read: {ex.Message}");
            return null;
        }
    }

    /// <summary>Keeps the finished session for the "Show what was stopped" list, and clears the active one.</summary>
    public void Archive(string lastPath)
    {
        if (FilePath == null) return;
        try
        {
            if (File.Exists(FilePath))
            {
                File.Move(FilePath, lastPath, overwrite: true);
                SaveToHistory(lastPath);
            }
        }
        catch (Exception ex)
        {
            Log.Write($"Could not archive session.json: {ex.Message}");
        }
        FilePath = lastPath;
    }

    /// <summary>How many finished boosts "What was stopped" can show.</summary>
    public const int HistoryLimit = 10;

    static void SaveToHistory(string finishedFile)
    {
        var dir = AppPaths.HistoryDir;
        Directory.CreateDirectory(dir);
        File.Copy(finishedFile, Path.Combine(dir, $"boost-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json"), overwrite: true);
        foreach (var old in Directory.GetFiles(dir, "boost-*.json").OrderByDescending(f => f).Skip(HistoryLimit))
            File.Delete(old);
    }

    /// <summary>Finished boosts, newest first.</summary>
    public static List<SessionJournal> LoadHistory()
    {
        var list = new List<SessionJournal>();
        if (!Directory.Exists(AppPaths.HistoryDir)) return list;
        foreach (var f in Directory.GetFiles(AppPaths.HistoryDir, "boost-*.json").OrderByDescending(f => f).Take(HistoryLimit))
        {
            var j = TryLoad(f);
            if (j != null && !j.IsSelfCheck) list.Add(j);
        }
        return list;
    }
}
