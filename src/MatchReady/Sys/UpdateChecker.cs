using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace MatchReady.Sys;

/// <summary>
/// Opt-in updates from GitHub releases. It reads the public "latest release" data, and only when the user clicks
/// Update does it download the Setup, check its SHA256 against the release's SHA256SUMS.txt, and run it.
/// This is the app's only network code. Never runs while CS2 is running.
/// </summary>
public static class UpdateChecker
{
    public const string Repo = "onlyilir/match-ready";
    public const string SetupAsset = "MatchReady-Setup.exe";
    public const string SumsAsset = "SHA256SUMS.txt";
    public static string ReleasesPage => $"https://github.com/{Repo}/releases/latest";

    public sealed record Result(bool Ok, bool NewerAvailable, string Latest, string Url, string Message,
        string? SetupUrl = null, string? SumsUrl = null, string Notes = "");

    static HttpClient Client(Version current, TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        // Generic client name; no personal details are sent.
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"MatchReady/{current.ToString(3)}");
        return http;
    }

    public static async Task<Result> CheckAsync(Version current)
    {
        try
        {
            using var http = Client(current, TimeSpan.FromSeconds(10));
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            using var resp = await http.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest");
            if (!resp.IsSuccessStatusCode)
                return new Result(false, false, "", ReleasesPage, $"Couldn't check for updates (GitHub said {(int)resp.StatusCode}).");
            return Parse(await resp.Content.ReadAsStringAsync(), current);
        }
        catch (Exception ex)
        {
            return new Result(false, false, "", ReleasesPage, "Couldn't check for updates: " + ex.Message);
        }
    }

    /// <summary>Reads GitHub's "latest release" JSON.</summary>
    public static Result Parse(string json, Version current)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var url = root.TryGetProperty("html_url", out var u) ? u.GetString() ?? ReleasesPage : ReleasesPage;
        var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        string? setup = null, sums = null;
        if (root.TryGetProperty("assets", out var assets))
        {
            foreach (var a in assets.EnumerateArray())
            {
                var name = a.GetProperty("name").GetString();
                var link = a.GetProperty("browser_download_url").GetString();
                if (name == SetupAsset) setup = link;
                if (name == SumsAsset) sums = link;
            }
        }
        if (!TryParse(tag, out var latest))
            return new Result(false, false, tag, url, "Couldn't read the latest version number.");
        var newer = latest > current;
        return new Result(true, newer, latest.ToString(3), url, newer
            ? $"Version {latest.ToString(3)} is available."
            : $"You have the latest version ({current.ToString(3)}).", setup, sums, notes);
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

    /// <summary>Finds the hash for a file in a "HASH  name" list (sha256sum format, upper or lower case).</summary>
    public static string? HashFor(string sums, string fileName)
    {
        foreach (var line in sums.Replace("\r", "").Split('\n'))
        {
            var parts = line.Trim().Split(new[] { ' ', '\t', '*' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[1].Equals(fileName, StringComparison.OrdinalIgnoreCase) && parts[0].Length == 64)
                return parts[0].ToUpperInvariant();
        }
        return null;
    }

    /// <summary>
    /// Downloads the new Setup into a private temp folder and checks its SHA256. Returns the path only when it matches.
    /// </summary>
    public static async Task<(string? Path, string Message)> DownloadAsync(Result r, Version current, IProgress<double> progress, CancellationToken ct)
    {
        if (r.SetupUrl == null || r.SumsUrl == null)
            return (null, "This release has no checked download. Opening the download page instead.");
        using var http = Client(current, TimeSpan.FromMinutes(10));

        var sums = await http.GetStringAsync(r.SumsUrl, ct);
        var expected = HashFor(sums, SetupAsset);
        if (expected == null) return (null, "The release's checksum list doesn't include the Setup. Not installing.");

        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MatchReady-Update");
        Directory.CreateDirectory(dir);
        var file = System.IO.Path.Combine(dir, $"MatchReady-Setup-{r.Latest}.exe");
        using (var resp = await http.GetAsync(r.SetupUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? 0;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(file);
            var buf = new byte[81920];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                if (total > 0) progress.Report((double)done / total);
            }
        }

        string actual;
        await using (var fs = File.OpenRead(file)) actual = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(file);
            return (null, "The download didn't match its published checksum, so it was deleted and nothing was installed.");
        }
        return (file, "Download checked (SHA256 matches).");
    }

    /// <summary>True when this copy is the installed one (Setup can update it); false for the portable exe.</summary>
    public static bool IsInstalledCopy()
    {
        var installDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Match Ready");
        var exeDir = System.IO.Path.GetDirectoryName(Environment.ProcessPath) ?? "";
        return string.Equals(System.IO.Path.GetFullPath(exeDir).TrimEnd('\\'), System.IO.Path.GetFullPath(installDir).TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase);
    }
}
