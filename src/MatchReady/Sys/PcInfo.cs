using Microsoft.Win32;

namespace MatchReady.Sys;

/// <summary>
/// What's in this PC, read once from the registry and Windows APIs (read-only, nothing leaves the PC).
/// Used to tune Quick setup and to set honest expectations, instead of hard-coding anyone's PC.
/// </summary>
public sealed class PcInfo
{
    public string Cpu { get; init; } = "";
    public long RamBytes { get; init; }
    public IReadOnlyList<string> Gpus { get; init; } = Array.Empty<string>();
    public bool? SystemDriveIsSsd { get; init; }
    public string WindowsBuild { get; init; } = "";
    public IReadOnlyCollection<string> InstalledApps { get; init; } = Array.Empty<string>();

    public int RamGb => (int)Math.Round(RamBytes / (1024.0 * 1024 * 1024));

    /// <summary>One line for the UI, e.g. "AMD Ryzen 5 7600 · 32 GB RAM · AMD Radeon RX 7800 XT · SSD".</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (Cpu.Length > 0) parts.Add(Cpu);
            if (RamBytes > 0) parts.Add($"{RamGb} GB RAM");
            if (Gpus.Count > 0) parts.Add(Gpus[0]);
            if (SystemDriveIsSsd != null) parts.Add(SystemDriveIsSsd == true ? "SSD" : "hard drive");
            return string.Join("  ·  ", parts);
        }
    }

    /// <summary>Honest expectation, based on how much RAM there is.</summary>
    public string Expectation => RamBytes == 0 ? "" : RamGb switch
    {
        <= 8 => "With 8 GB of RAM or less, closing background apps can make a real difference.",
        <= 16 => "With 16 GB of RAM, expect fewer stutters when heavy apps were open, not big FPS jumps.",
        _ => "This PC is strong: expect steadier frame times and fewer background hiccups, not big FPS jumps.",
    };

    /// <summary>True if an installed program's name contains any of these words.</summary>
    public bool HasInstalled(params string[] nameParts) =>
        InstalledApps.Any(a => nameParts.Any(p => a.Contains(p, StringComparison.OrdinalIgnoreCase)));

    public static PcInfo Detect(ISystemOps os)
    {
        string cpu = "";
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            cpu = (k?.GetValue("ProcessorNameString") as string ?? "").Trim();
            // "AMD Ryzen 5 7600 6-Core Processor" -> "AMD Ryzen 5 7600"
            var i = cpu.IndexOf("-Core", StringComparison.OrdinalIgnoreCase);
            if (i > 0) cpu = cpu[..cpu.LastIndexOf(' ', i)].Trim();
        }
        catch { /* unknown */ }

        return new PcInfo
        {
            Cpu = cpu,
            RamBytes = TotalRam(),
            Gpus = DetectGpus(),
            SystemDriveIsSsd = os.SystemDriveIsSsd(),
            WindowsBuild = WindowsInfo.BuildText(),
            InstalledApps = Installed(),
        };
    }

    static long TotalRam()
    {
        var m = new NativeMethods.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
        return NativeMethods.GlobalMemoryStatusEx(ref m) ? (long)m.ullTotalPhys : 0;
    }

    static List<string> DetectGpus()
    {
        var list = new List<string>();
        try
        {
            // Display adapters class key; each numbered subkey is one adapter.
            using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            foreach (var sub in cls?.GetSubKeyNames().Where(n => n.All(char.IsDigit)) ?? Enumerable.Empty<string>())
            {
                using var k = cls!.OpenSubKey(sub);
                if (k?.GetValue("DriverDesc") is string d && d.Length > 0 &&
                    !d.Contains("Basic Display", StringComparison.OrdinalIgnoreCase) &&
                    !d.Contains("Remote", StringComparison.OrdinalIgnoreCase) && !list.Contains(d))
                    list.Add(d);
            }
        }
        catch { /* unknown */ }
        // Put the dedicated card first: integrated graphics usually say "Graphics" without a model number.
        return list.OrderBy(g => g.EndsWith("Graphics", StringComparison.OrdinalIgnoreCase) || g.Contains("(TM) Graphics") ? 1 : 0).ToList();
    }

    static HashSet<string> Installed()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots = new[]
        {
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        };
        foreach (var (hive, path) in roots)
        {
            try
            {
                using var root = hive.OpenSubKey(path);
                foreach (var sub in root?.GetSubKeyNames() ?? Array.Empty<string>())
                {
                    using var k = root!.OpenSubKey(sub);
                    if (k?.GetValue("DisplayName") is string n && n.Length > 0) names.Add(n.Trim());
                }
            }
            catch { /* skip unreadable keys */ }
        }
        return names;
    }
}
