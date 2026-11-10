using System.Diagnostics;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using MatchReady.Safety;
using Microsoft.Win32;

namespace MatchReady.Sys;

/// <summary>
/// Real Windows implementation. Rules:
/// - The process list comes from a system snapshot: no handle is opened to any process.
/// - A handle is only opened to a process after the guard has cleared its name.
/// - Service handles are only opened for services the guard has cleared.
/// </summary>
public sealed partial class WindowsSystemOps : ISystemOps
{
    public IReadOnlyList<ProcInfo> ListProcesses()
    {
        var list = new List<ProcInfo>();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                // ProcessName, Id and SessionId come from the snapshot (NtQuerySystemInformation).
                list.Add(new ProcInfo(p.ProcessName, p.Id, p.SessionId));
            }
        }
        return list;
    }

    public string? GetProcessPath(ProcInfo p)
    {
        Guard.EnsureProcessAllowed(p.Name);
        var h = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, p.Pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            return ImagePath(h);
        }
        finally
        {
            NativeMethods.CloseHandle(h);
        }
    }

    public bool RequestClose(ProcInfo p)
    {
        Guard.EnsureProcessAllowed(p.Name);
        var asked = false;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == p.Pid && NativeMethods.IsWindowVisible(hwnd) &&
                NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER) == IntPtr.Zero)
            {
                asked |= NativeMethods.PostMessage(hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }
            return true;
        }, IntPtr.Zero);
        return asked;
    }

    public bool Kill(ProcInfo p)
    {
        Guard.EnsureProcessAllowed(p.Name);
        var h = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_TERMINATE | NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, p.Pid);
        if (h == IntPtr.Zero) return false;
        try
        {
            // Re-check through the handle: the PID must still be the same allowed exe (PIDs get reused).
            var path = ImagePath(h);
            Guard.EnsureProcessPathAllowed(p.Name, path);
            if (!string.Equals(Path.GetFileNameWithoutExtension(path), p.Name, StringComparison.OrdinalIgnoreCase))
                throw new ProtectedTargetException($"Refused: PID {p.Pid} is no longer '{p.Name}'.");
            return NativeMethods.TerminateProcess(h, 1);
        }
        finally
        {
            NativeMethods.CloseHandle(h);
        }
    }

    public void LaunchUnelevated(string exePath)
    {
        // We run as admin. Starting through Explorer runs the app as the normal user, like a double-click.
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{exePath}\"") { UseShellExecute = false })?.Dispose();
    }

    static readonly byte[][] AdminMarkers =
    {
        Encoding.UTF8.GetBytes("requireAdministrator"),
        Encoding.UTF8.GetBytes("highestAvailable"),
        Encoding.Unicode.GetBytes("requireAdministrator"),
        Encoding.Unicode.GetBytes("highestAvailable"),
    };

    public bool ExeNeedsAdmin(string exePath)
    {
        try
        {
            // The embedded manifest is plain text inside the exe. Scan the file in chunks (with overlap).
            const int chunk = 1 << 20;
            var overlap = AdminMarkers.Max(m => m.Length);
            var buf = new byte[chunk + overlap];
            using var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var carried = 0;
            int read;
            while ((read = fs.Read(buf, carried, chunk)) > 0)
            {
                var span = buf.AsSpan(0, carried + read);
                foreach (var m in AdminMarkers)
                {
                    if (span.IndexOf(m) >= 0) return true;
                }
                carried = Math.Min(overlap, span.Length);
                span[^carried..].CopyTo(buf);
            }
            return false;
        }
        catch
        {
            return true; // can't tell: don't risk a surprise admin prompt
        }
    }

    static string? ImagePath(IntPtr h)
    {
        var sb = new StringBuilder(1024);
        var size = sb.Capacity;
        return NativeMethods.QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString(0, size) : null;
    }

    // ---------------- Services ----------------

    public string? GetServiceStatus(string name)
    {
        // GetServices() enumerates through the Service Control Manager. No per-service handle is opened.
        foreach (var s in ServiceController.GetServices())
        {
            using (s)
            {
                if (string.Equals(s.ServiceName, name, StringComparison.OrdinalIgnoreCase))
                    return s.Status.ToString();
            }
        }
        return null;
    }

    public void StopService(string name, TimeSpan timeout)
    {
        Guard.EnsureServiceAllowed(name);
        using var sc = new ServiceController(name);
        // false = never stop other services that depend on this one. If any are running, this fails and we skip.
        sc.Stop(stopDependentServices: false);
        sc.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
    }

    public void StartService(string name, TimeSpan timeout)
    {
        Guard.EnsureServiceAllowed(name);
        using var sc = new ServiceController(name);
        if (sc.Status == ServiceControllerStatus.Running) return;
        if (sc.Status != ServiceControllerStatus.StartPending) sc.Start();
        sc.WaitForStatus(ServiceControllerStatus.Running, timeout);
    }

    // ---------------- Power plans (powercfg) ----------------

    [GeneratedRegex(@"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\s*\(([^)]*)\)")]
    private static partial Regex SchemeLine();

    public IReadOnlyList<PowerPlan> ListPowerPlans()
    {
        var plans = new List<PowerPlan>();
        foreach (Match m in SchemeLine().Matches(PowerCfg("/list")))
            plans.Add(new PowerPlan(Guid.Parse(m.Groups[1].Value), m.Groups[2].Value.Trim()));
        return plans;
    }

    public Guid? GetActivePowerPlan()
    {
        var m = SchemeLine().Match(PowerCfg("/getactivescheme"));
        return m.Success ? Guid.Parse(m.Groups[1].Value) : null;
    }

    public void SetActivePowerPlan(Guid id) => PowerCfg($"/setactive {id}");

    public Guid? DuplicatePowerPlan(Guid source)
    {
        var m = SchemeLine().Match(PowerCfg($"/duplicatescheme {source}"));
        return m.Success ? Guid.Parse(m.Groups[1].Value) : null;
    }

    public void DeletePowerPlan(Guid id) => PowerCfg($"/delete {id}");

    static string PowerCfg(string args)
    {
        var psi = new ProcessStartInfo(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "powercfg.exe"), args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(15000);
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"powercfg {args} failed: {output.Trim()} {p.StandardError.ReadToEnd().Trim()}");
        return output;
    }

    // ---------------- Registry (HKCU only) ----------------

    public RegValue ReadUserDword(string key, string name)
    {
        using var k = Registry.CurrentUser.OpenSubKey(key);
        var v = k?.GetValue(name);
        return v is int i ? new RegValue(true, i) : new RegValue(v != null, 0);
    }

    public void WriteUserDword(string key, string name, int value)
    {
        using var k = Registry.CurrentUser.CreateSubKey(key, writable: true);
        k.SetValue(name, value, RegistryValueKind.DWord);
    }

    public void DeleteUserValue(string key, string name)
    {
        using var k = Registry.CurrentUser.OpenSubKey(key, writable: true);
        k?.DeleteValue(name, throwOnMissingValue: false);
    }

    // ---------------- Facts ----------------

    public long AvailableMemoryBytes()
    {
        var m = new NativeMethods.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
        return NativeMethods.GlobalMemoryStatusEx(ref m) ? (long)m.ullAvailPhys : 0;
    }

    public bool? SystemDriveIsSsd()
    {
        try
        {
            var drive = Path.GetPathRoot(Environment.SystemDirectory)!.TrimEnd('\\'); // "C:"
            using var h = NativeMethods.CreateFile(@"\\.\" + drive, 0,
                NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE, IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);
            if (h.IsInvalid) return null;
            var q = new NativeMethods.STORAGE_PROPERTY_QUERY { PropertyId = 7, QueryType = 0, AdditionalParameters = new byte[1] };
            if (!NativeMethods.DeviceIoControl(h, NativeMethods.IOCTL_STORAGE_QUERY_PROPERTY,
                    ref q, System.Runtime.InteropServices.Marshal.SizeOf(q),
                    out var d, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.DEVICE_SEEK_PENALTY_DESCRIPTOR>(),
                    out _, IntPtr.Zero))
                return null;
            return !d.IncursSeekPenalty;
        }
        catch
        {
            return null;
        }
    }

    public bool PrintQueueIsEmpty()
    {
        try
        {
            var spool = Path.Combine(Environment.SystemDirectory, "spool", "PRINTERS");
            return !Directory.Exists(spool) || !Directory.EnumerateFiles(spool).Any();
        }
        catch
        {
            return false; // can't tell: treat as "jobs queued" and leave the spooler alone
        }
    }
}
