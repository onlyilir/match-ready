namespace MatchReady;

public static class AppPaths
{
    static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static string DataDir { get; private set; } = Path.Combine(LocalAppData, "MatchReady");

    /// <summary>Data folder of the old name (CS2 Booster, v1.0-1.1).</summary>
    public static string LegacyDataDir { get; private set; } = Path.Combine(LocalAppData, "CS2Booster");

    /// <summary>Tests point the old-name folder somewhere harmless too.</summary>
    public static void OverrideLegacyDataDir(string dir) => LegacyDataDir = dir;

    public static string SessionFile => Path.Combine(DataDir, "session.json");
    public static string LastSessionFile => Path.Combine(DataDir, "session-last.json");
    public static string HistoryDir => Path.Combine(DataDir, "history");
    public static string LogFile => Path.Combine(DataDir, "log.txt");
    public static string OldLogFile => Path.Combine(DataDir, "log.old.txt");

    /// <summary>config.json lives next to the exe. Falls back to the data folder if that isn't writable.</summary>
    public static string ConfigFile
    {
        get
        {
            var nextToExe = Path.Combine(AppContext.BaseDirectory, "config.json");
            if (File.Exists(nextToExe) || CanWrite(AppContext.BaseDirectory)) return nextToExe;
            return Path.Combine(DataDir, "config.json");
        }
    }

    /// <summary>Tests point the data folder somewhere harmless.</summary>
    public static void OverrideDataDir(string dir) => DataDir = dir;

    /// <summary>
    /// First start after the rename: bring over the old log, history and, most importantly, an unfinished boost
    /// (session.json), so crash recovery still puts everything back. Files already in the new folder win.
    /// </summary>
    public static void MigrateLegacyData()
    {
        try
        {
            if (!Directory.Exists(LegacyDataDir)) return;
            Directory.CreateDirectory(DataDir);
            foreach (var name in new[] { "session.json", "session-last.json", "log.txt" })
            {
                var from = Path.Combine(LegacyDataDir, name);
                var to = Path.Combine(DataDir, name);
                if (File.Exists(from) && !File.Exists(to)) File.Move(from, to);
            }
            Log.Write("Moved data over from the old CS2 Booster folder.");
        }
        catch (Exception ex)
        {
            Log.Write("Could not move the old CS2 Booster data: " + ex.Message);
        }
    }

    static bool CanWrite(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, ".write-test-" + Environment.ProcessId);
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

public static class Log
{
    static readonly object Gate = new();

    /// <summary>The log is kept under this size; the previous part is kept once as log.old.txt.</summary>
    public const long MaxBytes = 1024 * 1024;

    /// <summary>Extra sink, e.g. the console in --dry-run mode.</summary>
    public static Action<string>? Echo { get; set; }

    public static void Write(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}";
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.DataDir);
                var log = new FileInfo(AppPaths.LogFile);
                if (log.Exists && log.Length > MaxBytes) File.Move(AppPaths.LogFile, AppPaths.OldLogFile, overwrite: true);
                File.AppendAllText(AppPaths.LogFile, line + Environment.NewLine);
            }
            catch
            {
                // Logging must never break the booster.
            }
        }
        Echo?.Invoke(message);
    }
}
