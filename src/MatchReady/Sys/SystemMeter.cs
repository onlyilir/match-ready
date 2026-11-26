namespace MatchReady.Sys;

/// <summary>
/// Live numbers for the boost dashboard: whole-PC CPU use and free RAM.
/// System-wide counters only (GetSystemTimes, GlobalMemoryStatusEx): no process, including the game, is opened.
/// </summary>
public sealed class SystemMeter
{
    ulong _lastIdle, _lastKernel, _lastUser;
    bool _primed;

    /// <summary>CPU use since the previous call, 0-100. The first call returns null (needs two samples).</summary>
    public double? CpuPercent()
    {
        if (!NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user)) return null;
        ulong i = ToUlong(idle), k = ToUlong(kernel), u = ToUlong(user);
        double? result = null;
        if (_primed) result = Compute(_lastIdle, _lastKernel, _lastUser, i, k, u);
        (_lastIdle, _lastKernel, _lastUser, _primed) = (i, k, u, true);
        return result;
    }

    /// <summary>Kernel time includes idle time, so busy = (kernel + user - idle) / (kernel + user).</summary>
    public static double? Compute(ulong idle0, ulong kernel0, ulong user0, ulong idle1, ulong kernel1, ulong user1)
    {
        var idle = idle1 - idle0;
        var total = (kernel1 - kernel0) + (user1 - user0);
        if (total == 0) return null;
        return Math.Clamp(100.0 * (total - idle) / total, 0, 100);
    }

    public static long FreeRamBytes()
    {
        var m = new NativeMethods.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
        return NativeMethods.GlobalMemoryStatusEx(ref m) ? (long)m.ullAvailPhys : 0;
    }

    static ulong ToUlong(System.Runtime.InteropServices.ComTypes.FILETIME f) => ((ulong)(uint)f.dwHighDateTime << 32) | (uint)f.dwLowDateTime;
}
