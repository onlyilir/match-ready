using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace MatchReady.Sys;

/// <summary>
/// "Start with Windows": a plain shortcut in the user's Start-up folder that opens the app in the tray.
/// No scheduled task, no registry Run key, nothing hidden. Windows shows its admin prompt at sign-in.
/// </summary>
public static class StartupShortcut
{
    public static string Path => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Match Ready.lnk");

    public static bool IsEnabled => File.Exists(Path);

    public static void Set(bool on)
    {
        if (!on)
        {
            if (File.Exists(Path)) File.Delete(Path);
            Log.Write("Start with Windows: off.");
            return;
        }
        var exe = Environment.ProcessPath!;
        var link = (IShellLinkW)new CShellLink();
        try
        {
            link.SetPath(exe);
            link.SetArguments("--tray");
            link.SetWorkingDirectory(System.IO.Path.GetDirectoryName(exe)!);
            link.SetDescription("Match Ready (starts in the tray)");
            link.SetIconLocation(exe, 0);
            ((IPersistFile)link).Save(Path, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
        Log.Write("Start with Windows: on (" + Path + ").");
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class CShellLink { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int max, IntPtr findData, uint flags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int max);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int max);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int max);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int max, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}
