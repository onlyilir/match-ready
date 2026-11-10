namespace MatchReady.Safety;

public sealed class ProtectedTargetException : InvalidOperationException
{
    public ProtectedTargetException(string message) : base(message) { }
}

/// <summary>
/// Every call that closes, kills, stops or starts something goes through here first.
/// If a target is protected, an exception is thrown before any Windows API is called.
/// </summary>
public static class Guard
{
    static readonly string[] SystemDirs =
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32") + "\\",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64") + "\\",
    };

    public static void EnsureProcessAllowed(string name)
    {
        if (ProtectedList.IsProtectedProcess(name))
            throw new ProtectedTargetException($"Refused: process '{name}' is protected.");
    }

    /// <summary>Path check: refuse anything living in System32/SysWOW64, or with an unknown path.</summary>
    public static void EnsureProcessPathAllowed(string name, string? path)
    {
        EnsureProcessAllowed(name);
        if (string.IsNullOrWhiteSpace(path))
            throw new ProtectedTargetException($"Refused: could not confirm where '{name}' lives.");
        if (IsSystemPath(path))
            throw new ProtectedTargetException($"Refused: '{name}' runs from a Windows system folder.");
    }

    public static bool IsSystemPath(string path)
    {
        foreach (var d in SystemDirs)
        {
            if (path.StartsWith(d, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public static void EnsureServiceAllowed(string name)
    {
        if (ProtectedList.IsProtectedService(name))
            throw new ProtectedTargetException($"Refused: service '{name}' is protected.");
    }
}
