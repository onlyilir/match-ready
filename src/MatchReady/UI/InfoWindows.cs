using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MatchReady.Sys;

namespace MatchReady.UI;

/// <summary>About: version, license, links, third-party notices.</summary>
public sealed class AboutWindow : Window
{
    public AboutWindow(Window owner, ISystemOps os)
    {
        Width = 480;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        Ui.Prepare(this, "About Match Ready", owner);
        var (root, s, bar) = Ui.Page();

        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(new Image { Source = Ui.AppIcon, Width = 56, Height = 56, Margin = new Thickness(0, 0, 14, 0) });
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock { Text = "Match Ready", Style = Ui.S("Title") });
        names.Children.Add(Ui.Muted($"Version {Ui.Version.TrimStart('v')}{BuildTag()}", new Thickness(0)));
        head.Children.Add(names);
        s.Children.Add(head);

        var intro = Ui.Body("Get your PC ready for a CS2 match with one button, and put everything back afterwards. " +
                            "FACEIT-safe: it never touches the game or the anti-cheat.");
        intro.Margin = new Thickness(0, 14, 0, 0);
        s.Children.Add(intro);
        s.Children.Add(Ui.Muted("© 2026 onlyilir. Free and open source under the MIT License. " +
                                "Not affiliated with Valve, Counter-Strike or FACEIT.", new Thickness(0, 10, 0, 0)));

        var links = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        void L(string text, Action go)
        {
            var b = Ui.Link(text, (_, _) => go());
            b.Margin = new Thickness(0, 0, 20, 6);
            links.Children.Add(b);
        }
        L("GitHub page", () => os.LaunchUnelevated($"https://github.com/{UpdateChecker.Repo}"));
        L("Report a problem", () => os.LaunchUnelevated(CrashReport.IssuesNew + "/choose"));
        L("What's new", () => new WhatsNewWindow(this).ShowDialog());
        L("License", () => new TextWindow(this, "License", Embedded("LICENSE")).ShowDialog());
        L("Third-party notices", () => new TextWindow(this, "Third-party notices", Embedded("THIRD-PARTY-NOTICES.md")).ShowDialog());
        s.Children.Add(links);

        var close = Ui.Button("Close", "SecondaryButton", (_, _) => Close());
        close.IsCancel = true;
        close.IsDefault = true;
        Ui.AddRight(bar, close);
        Content = root;
    }

    /// <summary>" (build abc1234)" from the commit the release was built from, if known.</summary>
    static string BuildTag()
    {
        var info = typeof(AboutWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        var plus = info.IndexOf('+');
        return plus > 0 && info.Length > plus + 7 ? $" (build {info.Substring(plus + 1, 7)})" : "";
    }

    public static string Embedded(string name)
    {
        using var s = typeof(AboutWindow).Assembly.GetManifestResourceStream(name);
        if (s == null) return "";
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}

/// <summary>Plain scrollable text (license, notices).</summary>
public sealed class TextWindow : Window
{
    public TextWindow(Window owner, string title, string text)
    {
        Width = 560;
        Height = 560;
        Ui.Prepare(this, title, owner);
        var (root, s, bar) = Ui.Page();
        s.Children.Add(new TextBlock
        {
            Text = text, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12.5,
        });
        var close = Ui.Button("Close", "SecondaryButton", (_, _) => Close());
        close.IsCancel = true;
        close.IsDefault = true;
        Ui.AddRight(bar, close);
        Content = root;
    }
}

/// <summary>"What's new": this version's part of the changelog, shown once after an update.</summary>
public sealed class WhatsNewWindow : Window
{
    public WhatsNewWindow(Window owner)
    {
        Width = 520;
        Height = 560;
        Ui.Prepare(this, "What's new", owner);
        var (root, s, bar) = Ui.Page();
        s.Children.Add(new TextBlock { Text = $"What's new in {Ui.Version.TrimStart('v')}", Style = Ui.S("Title") });
        var lines = ReleaseNotes.Section(AboutWindow.Embedded("CHANGELOG.md"), Ui.Version.TrimStart('v'));
        if (lines.Count == 0) s.Children.Add(Ui.Body("Small fixes and improvements."));
        foreach (var line in lines)
        {
            if (line.StartsWith("- "))
                s.Children.Add(new TextBlock { Text = "•  " + line[2..], Style = Ui.S("Body"), Margin = new Thickness(4, 3, 0, 0) });
            else if (line.Length > 0)
                s.Children.Add(Ui.SubHeading(line));
        }
        var ok = Ui.Button("Got it", "PrimaryButton", (_, _) => Close());
        ok.IsDefault = true;
        ok.IsCancel = true;
        Ui.AddRight(bar, ok);
        Content = root;
    }
}

/// <summary>Reads one version's section out of CHANGELOG.md.</summary>
public static class ReleaseNotes
{
    /// <summary>Lines under "## {version} …" up to the next "## ", without blank edges. Bold/inline markdown is removed.</summary>
    public static List<string> Section(string changelog, string version)
    {
        var result = new List<string>();
        var inside = false;
        foreach (var raw in changelog.Replace("\r", "").Split('\n'))
        {
            if (raw.StartsWith("## "))
            {
                if (inside) break;
                inside = raw[3..].TrimStart().StartsWith(version + " ") || raw[3..].Trim() == version;
                continue;
            }
            if (!inside) continue;
            result.Add(raw.Replace("**", "").Replace("`", "").TrimEnd());
        }
        while (result.Count > 0 && result[0].Length == 0) result.RemoveAt(0);
        while (result.Count > 0 && result[^1].Length == 0) result.RemoveAt(result.Count - 1);
        return result;
    }
}

/// <summary>Shown when something unexpected breaks. A boost is always put back first.</summary>
public sealed class CrashWindow : Window
{
    public bool Restart { get; private set; }

    public CrashWindow(Exception ex, string report, string reportPath, bool restored, ISystemOps os)
    {
        Width = 500;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        Ui.Prepare(this, "Match Ready", null);
        var (root, s, bar) = Ui.Page();
        s.Children.Add(new TextBlock { Text = "Something went wrong", Style = Ui.S("Title") });
        var what = Ui.Body(restored
            ? "Match Ready hit an unexpected problem. Your boost was stopped and everything was put back, so your PC is as it was."
            : "Match Ready hit an unexpected problem. Nothing was left changed on your PC.");
        what.Margin = new Thickness(0, 10, 0, 0);
        s.Children.Add(what);
        s.Children.Add(Ui.Muted("A report was saved (your Windows user name and PC name are left out): " + CrashReport.Scrub(reportPath),
            new Thickness(0, 10, 0, 0)));
        s.Children.Add(Ui.Muted("Sharing it helps fix the problem: Open an issue copies the report and opens GitHub, " +
                                "where you can paste it.", new Thickness(0, 6, 0, 0)));

        var copy = Ui.Link("Copy report", (_, _) => Clipboard.SetText(report));
        var issue = Ui.Link("Open an issue", (_, _) =>
        {
            Clipboard.SetText(report);
            os.LaunchUnelevated(CrashReport.IssueUrl(ex));
        });
        Ui.AddLeft(bar, copy, issue);
        var restart = Ui.Button("Restart Match Ready", "PrimaryButton", (_, _) => { Restart = true; Close(); });
        restart.IsDefault = true;
        var close = Ui.Button("Close", "SecondaryButton", (_, _) => Close());
        close.IsCancel = true;
        Ui.AddRight(bar, close, restart);
        Content = root;
    }
}
