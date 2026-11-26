using System.Text;
using MatchReady.Sys;

namespace MatchReady.UI;

/// <summary>
/// Builds the crash report: version, Windows build, the error and the end of the log.
/// Personal details (Windows user name, PC name, profile path) are replaced before anything is shown or shared.
/// </summary>
public static class CrashReport
{
    public static string IssuesNew => $"https://github.com/{UpdateChecker.Repo}/issues/new";

    public static string Build(Exception ex, string whatHappened)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Match Ready {Ui.Version}, Windows build {WindowsInfo.BuildText()}, {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine(whatHappened);
        sb.AppendLine();
        sb.AppendLine(ex.ToString());
        sb.AppendLine();
        sb.AppendLine("--- last log lines ---");
        foreach (var l in LastLogLines(60)) sb.AppendLine(l);
        return Scrub(sb.ToString());
    }

    /// <summary>Saves the report next to the log and returns its path.</summary>
    public static string Save(string report)
    {
        Directory.CreateDirectory(AppPaths.DataDir);
        var path = Path.Combine(AppPaths.DataDir, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        File.WriteAllText(path, report);
        return path;
    }

    /// <summary>Pre-filled "New issue" link (short summary only; the full report goes on the clipboard).</summary>
    public static string IssueUrl(Exception ex)
    {
        var title = Uri.EscapeDataString($"Crash: {ex.GetType().Name}");
        var summary = Scrub($"{ex.GetType().FullName}: {ex.Message}\n{string.Join("\n", (ex.StackTrace ?? "").Split('\n').Take(6))}");
        if (summary.Length > 1500) summary = summary[..1500];
        return $"{IssuesNew}?template=bug_report.yml&title={title}" +
               $"&version={Uri.EscapeDataString(Ui.Version)}" +
               $"&windows={Uri.EscapeDataString(WindowsInfo.BuildText())}" +
               $"&what-happened={Uri.EscapeDataString("The app showed \"Something went wrong\". Full report pasted below.\n\n" + summary)}";
    }

    /// <summary>Removes the Windows user name, PC name and profile path.</summary>
    public static string Scrub(string text)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length > 3) text = text.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        var user = Environment.UserName;
        if (user.Length > 2) text = System.Text.RegularExpressions.Regex.Replace(text, $@"\b{System.Text.RegularExpressions.Regex.Escape(user)}\b", "<user>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var pc = Environment.MachineName;
        if (pc.Length > 2) text = text.Replace(pc, "<pc>", StringComparison.OrdinalIgnoreCase);
        return text;
    }

    static IEnumerable<string> LastLogLines(int count)
    {
        try
        {
            if (!File.Exists(AppPaths.LogFile)) return Array.Empty<string>();
            using var fs = new FileStream(AppPaths.LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var r = new StreamReader(fs);
            var lines = r.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r')).ToList();
            return lines.Skip(Math.Max(0, lines.Count - count));
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
