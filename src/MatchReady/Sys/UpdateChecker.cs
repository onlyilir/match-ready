using System.Net.Http;
using System.Text.Json;

namespace MatchReady.Sys;

/// <summary>
/// Opt-in check for a newer release on GitHub. It only reads the public "latest release" page data;
/// it never downloads or installs anything. Off by default; never runs while CS2 is running.
/// This is the app's only network code.
/// </summary>
public static class UpdateChecker
{
    public const string Repo = "onlyilir/match-ready";
    public static string ReleasesPage => $"https://github.com/{Repo}/releases/latest";

    public sealed record Result(bool Ok, bool NewerAvailable, string Latest, string Url, string Message);

    public static async Task<Result> CheckAsync(Version current)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            // Generic client name; no personal details are sent.
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"MatchReady/{current.ToString(3)}");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            using var resp = await http.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest");
            if (!resp.IsSuccessStatusCode)
                return new Result(false, false, "", ReleasesPage, $"Couldn't check for updates (GitHub said {(int)resp.StatusCode}).");
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            var url = doc.RootElement.TryGetProperty("html_url", out var u) ? u.GetString() ?? ReleasesPage : ReleasesPage;
            if (!TryParse(tag, out var latest))
                return new Result(false, false, tag, url, "Couldn't read the latest version number.");
            var newer = latest > current;
            return new Result(true, newer, latest.ToString(3), url, newer
                ? $"Version {latest.ToString(3)} is available."
                : $"You have the latest version ({current.ToString(3)}).");
        }
        catch (Exception ex)
        {
            return new Result(false, false, "", ReleasesPage, "Couldn't check for updates: " + ex.Message);
        }
    }

    /// <summary>"v1.2.0" or "1.2" -> 1.2.0</summary>
    public static bool TryParse(string tag, out Version v)
    {
        var s = tag.Trim().TrimStart('v', 'V');
        if (Version.TryParse(s, out var p))
        {
            v = new Version(p.Major, p.Minor, Math.Max(0, p.Build));
            return true;
        }
        v = new Version(0, 0, 0);
        return false;
    }
}
