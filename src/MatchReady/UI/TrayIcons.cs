using System.Drawing.Imaging;

namespace MatchReady.UI;

/// <summary>The app icon (green bolt) and a gray copy for the tray when no boost is active.</summary>
public static class TrayIcons
{
    static Icon? _app, _green, _gray;

    /// <summary>Full icon (all sizes) for windows and the taskbar.</summary>
    public static Icon AppIcon => _app ??= Load(null);

    public static Icon Green => _green ??= Load(SystemInformation.SmallIconSize);

    public static Icon Gray => _gray ??= MakeGray(Green);

    static Icon Load(Size? size)
    {
        using var s = typeof(TrayIcons).Assembly.GetManifestResourceStream("MatchReady.ico");
        if (s == null) return SystemIcons.Application;
        return size is Size z ? new Icon(s, z) : new Icon(s);
    }

    static Icon MakeGray(Icon source)
    {
        using var src = source.ToBitmap();
        using var gray = new Bitmap(src.Width, src.Height);
        using (var g = Graphics.FromImage(gray))
        {
            // Grayscale, slightly dimmed: "on standby".
            var m = new ColorMatrix(new[]
            {
                new[] { .30f * .8f, .30f * .8f, .30f * .8f, 0, 0 },
                new[] { .59f * .8f, .59f * .8f, .59f * .8f, 0, 0 },
                new[] { .11f * .8f, .11f * .8f, .11f * .8f, 0, 0 },
                new[] { 0f, 0, 0, 1, 0 },
                new[] { .06f, .06f, .06f, 0, 1 },
            });
            using var attrs = new ImageAttributes();
            attrs.SetColorMatrix(m);
            g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attrs);
        }
        // Clone so the icon owns its data and the temporary handle can be freed.
        var h = gray.GetHicon();
        try { return (Icon)Icon.FromHandle(h).Clone(); }
        finally { Sys.NativeMethods.DestroyIcon(h); }
    }
}
