using System.Windows;
using System.Windows.Controls;
using MatchReady.Boost;
using MatchReady.Config;
using MatchReady.Sys;

namespace MatchReady.UI;

/// <summary>
/// "Pro" tab: a checklist of manual tips. The app never does these itself;
/// it only explains them and opens the right Windows page or website.
/// </summary>
public static class ProTips
{
    sealed record Tip(string Id, string Title, string Body, string? ButtonText = null, Action<Button>? ButtonAction = null);

    public static void Fill(StackPanel list, AppConfig cfg, ISystemOps os, Action save)
    {
        list.Children.Clear();
        list.Children.Add(Ui.Muted("Do these yourself, once, and tick them off. Match Ready never changes these for you.",
            new Thickness(0, 0, 0, 6)));
        foreach (var tip in Tips(os)) list.Children.Add(Card(tip, cfg, save));
    }

    static IEnumerable<Tip> Tips(ISystemOps os)
    {
        var gm = os.ReadUserDword(RegistryTargets.GameBarKey, RegistryTargets.GameModeValue);
        var gmOn = !gm.Exists || gm.Value != 0; // not set = Windows default, which is on
        yield return new Tip("gamemode", "Keep Windows Game Mode on",
            $"Right now it's {(gmOn ? "on ✓" : "off")}. Settings > Gaming > Game Mode. The boost turns it on while active; this keeps it on all the time.",
            "Open Game Mode settings", _ => os.LaunchUnelevated("ms-settings:gaming-gamemode"));

        yield return new Tip("fso", "Turn off fullscreen optimizations (optional)",
            "Steam > right-click Counter-Strike 2 > Manage > Browse local files > game\\bin\\win64 > right-click cs2.exe > " +
            "Properties > Compatibility > tick \"Disable fullscreen optimizations\". FACEIT has no official guidance on it. " +
            "It's a Windows setting, doesn't change game files, and may make no difference. Untick it if CS2 feels worse.");

        var (vendor, driverPage) = DriverPage(PcInfo.DetectGpus().FirstOrDefault() ?? "");
        yield return new Tip("gpudriver", "Keep the graphics driver up to date",
            "Use the driver app from AMD, NVIDIA or Intel, or their own websites. Avoid third-party driver-updater tools.",
            $"Open {vendor}'s driver page", _ => os.LaunchUnelevated(driverPage));

        yield return new Tip("high", "Add -high to CS2's launch options",
            "Steam > right-click Counter-Strike 2 > Properties > General > Launch options > type  -high   " +
            "It's Valve's own option. Match Ready never touches the game.",
            "Copy -high", b => { Clipboard.SetText("-high"); b.Content = "Copied ✓"; });

        yield return new Tip("hpet", "Only if FACEIT makes the game stutter: AMD fix",
            "FACEIT's own fix for some AMD CPUs (HPET): Command Prompt as admin, run  bcdedit /set useplatformclock false  " +
            "and restart. Skip it if the game runs smoothly.",
            "Open FACEIT's article", _ => os.LaunchUnelevated("https://support.faceit.com/hc/en-us/articles/14605964372124-Performance-issues-with-AMD-CPUs"));
    }

    /// <summary>The official driver page for the graphics card this PC has (AMD if unknown).</summary>
    public static (string Vendor, string Url) DriverPage(string gpu) =>
        gpu.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ? ("NVIDIA", "https://www.nvidia.com/en-us/drivers/")
        : gpu.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? ("Intel", "https://www.intel.com/content/www/us/en/support/detect.html")
        : ("AMD", "https://www.amd.com/en/support/download/drivers.html");

    static Border Card(Tip tip, AppConfig cfg, Action save)
    {
        var stack = new StackPanel();
        var check = new CheckBox
        {
            Content = new TextBlock { Text = tip.Title, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap },
            IsChecked = cfg.ProDone.Contains(tip.Id),
        };
        System.Windows.Automation.AutomationProperties.SetName(check, tip.Title);
        check.Click += (_, _) =>
        {
            cfg.ProDone.Remove(tip.Id);
            if (check.IsChecked == true) cfg.ProDone.Add(tip.Id);
            save();
        };
        stack.Children.Add(check);
        stack.Children.Add(Ui.Muted(tip.Body, new Thickness(28, 2, 0, 0)));
        if (tip.ButtonText != null)
        {
            var b = Ui.Button(tip.ButtonText, "QuietButton");
            b.HorizontalAlignment = HorizontalAlignment.Left;
            b.Margin = new Thickness(18, 4, 0, 0);
            b.Click += (_, _) =>
            {
                try { tip.ButtonAction!(b); }
                catch (Exception ex) { Log.Write($"Pro tip action failed: {ex.Message}"); }
            };
            stack.Children.Add(b);
        }
        return new Border { Style = Ui.S("Card"), Margin = new Thickness(0, 0, 0, 8), Child = stack };
    }
}
