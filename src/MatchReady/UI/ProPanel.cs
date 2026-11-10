using MatchReady.Boost;
using MatchReady.Config;
using MatchReady.Sys;

namespace MatchReady.UI;

/// <summary>
/// "Pro" tab: a checklist of manual tips. The booster never does these itself;
/// it only explains them and opens the right Windows page or website.
/// </summary>
public sealed class ProPanel : Stack
{
    sealed record Tip(string Id, string Title, string Body, string? ButtonText = null, Action? ButtonAction = null);

    readonly AppConfig _cfg;
    readonly ISystemOps _os;
    readonly Action _save;

    public ProPanel(AppConfig cfg, ISystemOps os, Action save)
    {
        _cfg = cfg;
        _os = os;
        _save = save;
        Rebuild();
    }

    /// <summary>Rebuilds the list so live facts (like Game Mode) are current.</summary>
    public void Rebuild()
    {
        SuspendLayout();
        foreach (Control c in Controls.Cast<Control>().ToList()) c.Dispose();
        Add(Theme.MutedText("Do these yourself, once, and tick them off. I never change these for you.", 9.5f))
            .Margin = new Padding(0, 0, 0, 6);
        foreach (var tip in Tips()) AddTip(tip);
        ResumeLayout();
    }

    IEnumerable<Tip> Tips()
    {
        var gm = _os.ReadUserDword(RegistryTargets.GameBarKey, RegistryTargets.GameModeValue);
        var gmOn = !gm.Exists || gm.Value != 0; // not set = Windows default, which is on
        yield return new Tip("gamemode", "Keep Windows Game Mode on",
            $"Right now: {(gmOn ? "ON ✓" : "OFF")}. Settings > Gaming > Game Mode. The Boost turns it on while active; " +
            "this keeps it on all the time.",
            "Open Game Mode settings", () => Open("ms-settings:gaming-gamemode"));

        yield return new Tip("fso", "Turn off fullscreen optimizations (optional)",
            "Steam > right-click Counter-Strike 2 > Manage > Browse local files > game\\bin\\win64 > right-click cs2.exe > " +
            "Properties > Compatibility > tick \"Disable fullscreen optimizations\". FACEIT has no official guidance on it. " +
            "It's a Windows setting, doesn't change game files, and may make no difference. Untick it if CS2 feels worse.");

        yield return new Tip("gpudriver", "Update the AMD graphics driver",
            "AMD Software: Adrenalin Edition > Home > Check for updates. Only install drivers from AMD's own app or amd.com.",
            "Open AMD's driver page", () => Open("https://www.amd.com/en/support/download/drivers.html"));

        yield return new Tip("high", "Add -high to CS2's launch options",
            "Steam > right-click Counter-Strike 2 > Properties > General > Launch options > type  -high   " +
            "It's Valve's own option. The booster never touches the game.",
            "Copy -high", () => Clipboard.SetText("-high"));

        yield return new Tip("hpet", "Only if FACEIT makes the game stutter: AMD fix",
            "FACEIT's own fix for some AMD CPUs (HPET): Command Prompt as admin, run  bcdedit /set useplatformclock false  " +
            "and restart. Skip it if the game runs smoothly.",
            "Open FACEIT's article", () => Open("https://support.faceit.com/hc/en-us/articles/14605964372124-Performance-issues-with-AMD-CPUs"));
    }

    void AddTip(Tip tip)
    {
        var check = Add(new CheckBox
        {
            Text = tip.Title, Checked = _cfg.ProDone.Contains(tip.Id), AutoSize = true,
            Font = Theme.UiFont(10, FontStyle.Bold), Margin = new Padding(0, 10, 0, 2),
        });
        check.CheckedChanged += (_, _) =>
        {
            _cfg.ProDone.Remove(tip.Id);
            if (check.Checked) _cfg.ProDone.Add(tip.Id);
            _save();
        };
        Add(Theme.MutedText(tip.Body)).Margin = new Padding(20, 0, 0, 2);

        if (tip.ButtonText != null)
        {
            var link = Add(Theme.LinkText(tip.ButtonText));
            link.Margin = new Padding(20, 0, 0, 2);
            link.LinkClicked += (_, _) =>
            {
                try
                {
                    tip.ButtonAction!();
                    if (tip.Id == "high") link.Text = "Copied ✓";
                }
                catch (Exception ex) { Log.Write($"Pro tip action failed: {ex.Message}"); }
            };
        }
    }

    // We run as admin. Opening through Explorer starts the page as the normal user, like a click in Start.
    void Open(string target) => _os.LaunchUnelevated(target);
}
