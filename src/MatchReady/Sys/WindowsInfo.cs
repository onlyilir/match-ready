using System.Security.Principal;
using Microsoft.Win32;

namespace MatchReady.Sys;

/// <summary>Read-only facts about Windows, plus the optional restore point.</summary>
public static class WindowsInfo
{
    public static bool IsAdmin()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static string BuildText()
    {
        using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var build = k?.GetValue("CurrentBuild") as string ?? Environment.OSVersion.Version.Build.ToString();
        var ubr = k?.GetValue("UBR") is int u ? "." + u : "";
        return build + ubr;
    }

    /// <summary>True when this Windows is an Insider (preview) build. FACEIT does not support those.</summary>
    public static bool IsInsiderBuild()
    {
        try
        {
            using var app = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\WindowsSelfHost\Applicability");
            if (app != null)
            {
                if (app.GetValue("BranchName") is string b && b.Length > 0) return true;
                if (app.GetValue("EnablePreviewBuilds") is int e && e != 0) return true;
                if (app.GetValue("IsBuildFlightingEnabled") is int f && f != 0) return true;
            }
            using var ui = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\WindowsSelfHost\UI\Selection");
            if (ui?.GetValue("UIBranch") is string uib && uib.Length > 0) return true;

            using var cv = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var lab = (cv?.GetValue("BuildLabEx") as string ?? "") + (cv?.GetValue("BuildBranch") as string ?? "");
            return lab.Contains("prerelease", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Creates a System Restore point. Windows may skip it if one was made in the last 24 hours.</summary>
    public static (bool ok, string message) CreateRestorePoint()
    {
        try
        {
            var info = new NativeMethods.RESTOREPOINTINFOW
            {
                dwEventType = 100, dwRestorePtType = 12, szDescription = "Before Match Ready first use",
            };
            if (!NativeMethods.SRSetRestorePointW(ref info, out var status))
                return (false, $"Windows did not create a restore point (code {status.nStatus}). System Protection may be off.");
            var end = new NativeMethods.RESTOREPOINTINFOW
            {
                dwEventType = 101, dwRestorePtType = 12, llSequenceNumber = status.llSequenceNumber, szDescription = "",
            };
            NativeMethods.SRSetRestorePointW(ref end, out _);
            return (true, "Restore point created.");
        }
        catch (Exception ex)
        {
            return (false, "Restore point failed: " + ex.Message);
        }
    }
}
