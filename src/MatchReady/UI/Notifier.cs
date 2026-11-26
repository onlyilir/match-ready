using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace MatchReady.UI;

/// <summary>
/// Native Windows notifications (with the app's name and icon, kept in Notification Center).
/// Shown only when nothing is being played: after a restore, or for an available update. Never during a game.
/// If Windows notifications aren't available, the tray balloon is used instead.
/// </summary>
public static class Notifier
{
    public const string AppId = "onlyilir.MatchReady";
    public static string RegistryKey => @"Software\Classes\AppUserModelId\" + AppId;

    static bool _registered;
    static Tray? _fallback;
    static Action? _onClick;

    public static void Init(Tray fallback, Action onClick)
    {
        _fallback = fallback;
        _onClick = onClick;
    }

    /// <summary>Tells Windows the app's display name and icon (per user, removed by the uninstaller).</summary>
    static void Register()
    {
        if (_registered) return;
        var icon = Path.Combine(AppPaths.DataDir, "notification-icon.png");
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            if (!File.Exists(icon))
            {
                using var ico = Tray.LoadIcon(new System.Drawing.Size(64, 64));
                using var bmp = ico.ToBitmap();
                bmp.Save(icon, System.Drawing.Imaging.ImageFormat.Png);
            }
        }
        catch { /* no icon: Windows shows a generic one */ }
        using (var k = Registry.CurrentUser.CreateSubKey(RegistryKey))
        {
            k.SetValue("DisplayName", "Match Ready");
            if (File.Exists(icon)) k.SetValue("IconUri", icon);
        }
        _registered = true;
    }

    public static void Show(string title, string body)
    {
        try
        {
            Register();
            var xml = new XmlDocument();
            xml.LoadXml(
                "<toast><visual><binding template=\"ToastGeneric\">" +
                $"<text>{Escape(title)}</text><text>{Escape(body)}</text>" +
                "</binding></visual></toast>");
            var toast = new ToastNotification(xml) { ExpirationTime = DateTimeOffset.Now.AddHours(12) };
            toast.Activated += (_, _) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => _onClick?.Invoke());
            ToastNotificationManager.CreateToastNotifier(AppId).Show(toast);
        }
        catch (Exception ex)
        {
            Log.Write("Windows notification unavailable, using the tray instead: " + ex.Message);
            _fallback?.Balloon(title, body);
        }
    }

    static string Escape(string s) => System.Security.SecurityElement.Escape(s) ?? "";
}
