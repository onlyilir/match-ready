using System.Drawing;
using System.Drawing.Imaging;
using WinForms = System.Windows.Forms;

namespace MatchReady.UI;

/// <summary>
/// The tray icon: green bolt while a boost is on, gray when idle, with a dark right-click menu.
/// (WPF has no tray icon, so this one piece uses Windows Forms.)
/// </summary>
public sealed class Tray : IDisposable
{
    readonly WinForms.NotifyIcon _icon;
    readonly WinForms.ToolStripMenuItem _last, _update, _boost, _auto;
    readonly Icon _green, _gray;

    public event Action? OpenClicked, BoostClicked, ExitClicked, UpdateClicked;
    public event Action<bool>? AutoChanged;

    public Tray(bool autoMode)
    {
        _green = LoadIcon(WinForms.SystemInformation.SmallIconSize);
        _gray = MakeGray(_green);

        var menu = new WinForms.ContextMenuStrip { Renderer = new DarkMenuRenderer(), ShowImageMargin = true, Font = new Font("Segoe UI", 9.5f) };
        var open = new WinForms.ToolStripMenuItem("Open Match Ready") { Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
        open.Click += (_, _) => OpenClicked?.Invoke();
        _last = new WinForms.ToolStripMenuItem("") { Enabled = false, Visible = false };
        _update = new WinForms.ToolStripMenuItem("") { Visible = false };
        _update.Click += (_, _) => UpdateClicked?.Invoke();
        _boost = new WinForms.ToolStripMenuItem("Boost now");
        _boost.Click += (_, _) => BoostClicked?.Invoke();
        _auto = new WinForms.ToolStripMenuItem("Auto mode") { Checked = autoMode, CheckOnClick = true };
        _auto.CheckedChanged += (_, _) => AutoChanged?.Invoke(_auto.Checked);
        var exit = new WinForms.ToolStripMenuItem("Exit (restores everything)");
        exit.Click += (_, _) => ExitClicked?.Invoke();
        menu.Items.AddRange(new WinForms.ToolStripItem[]
            { open, _last, _update, new WinForms.ToolStripSeparator(), _boost, _auto, new WinForms.ToolStripSeparator(), exit });

        _icon = new WinForms.NotifyIcon { Icon = _gray, Text = "Match Ready: Ready", ContextMenuStrip = menu, Visible = true };
        _icon.MouseClick += (_, e) => { if (e.Button == WinForms.MouseButtons.Left) OpenClicked?.Invoke(); };
        _icon.BalloonTipClicked += (_, _) => OpenClicked?.Invoke();
    }

    public void SetState(bool boosted, bool busy, string tooltip)
    {
        _icon.Icon = boosted ? _green : _gray;
        _icon.Text = tooltip.Length <= 63 ? tooltip : tooltip[..62] + "…";
        _boost.Text = boosted ? "Restore everything" : "Boost now";
        _boost.Enabled = !busy;
    }

    public void SetAuto(bool on) { if (_auto.Checked != on) _auto.Checked = on; }

    public void SetLast(string? text)
    {
        _last.Visible = text != null;
        _last.Text = text ?? "";
    }

    public void SetUpdate(string? text)
    {
        _update.Visible = text != null;
        _update.Text = text ?? "";
    }

    /// <summary>Fallback when Windows notifications are unavailable.</summary>
    public void Balloon(string title, string text) => _icon.ShowBalloonTip(5000, title, text, WinForms.ToolTipIcon.None);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _green.Dispose();
        _gray.Dispose();
    }

    public static Icon LoadIcon(Size? size)
    {
        using var s = typeof(Tray).Assembly.GetManifestResourceStream("MatchReady.ico");
        if (s == null) return (Icon)SystemIcons.Application.Clone();
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
                new[] { .24f, .24f, .24f, 0, 0 },
                new[] { .47f, .47f, .47f, 0, 0 },
                new[] { .09f, .09f, .09f, 0, 0 },
                new[] { 0f, 0, 0, 1, 0 },
                new[] { .06f, .06f, .06f, 0, 1 },
            });
            using var attrs = new ImageAttributes();
            attrs.SetColorMatrix(m);
            g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attrs);
        }
        var h = gray.GetHicon();
        try { return (Icon)Icon.FromHandle(h).Clone(); }
        finally { Sys.NativeMethods.DestroyIcon(h); }
    }

    /// <summary>Dark right-click menu.</summary>
    sealed class DarkMenuRenderer : WinForms.ToolStripProfessionalRenderer
    {
        static readonly Color Text = Color.FromArgb(232, 234, 237), Muted = Color.FromArgb(154, 160, 168), Green = Color.FromArgb(38, 166, 91);

        public DarkMenuRenderer() : base(new DarkColors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(WinForms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Text : Muted;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(WinForms.ToolStripItemImageRenderEventArgs e)
        {
            var r = new Rectangle(e.ImageRectangle.X + 2, e.ImageRectangle.Y + 2, e.ImageRectangle.Width - 4, e.ImageRectangle.Height - 4);
            using var b = new SolidBrush(Green);
            e.Graphics.FillRectangle(b, r);
            WinForms.TextRenderer.DrawText(e.Graphics, "✓", new Font("Segoe UI", 8, FontStyle.Bold), r, Color.White,
                WinForms.TextFormatFlags.HorizontalCenter | WinForms.TextFormatFlags.VerticalCenter);
        }

        sealed class DarkColors : WinForms.ProfessionalColorTable
        {
            static readonly Color Panel = Color.FromArgb(32, 35, 42), Hover = Color.FromArgb(44, 48, 57), Border = Color.FromArgb(53, 58, 69);
            public override Color ToolStripDropDownBackground => Panel;
            public override Color ImageMarginGradientBegin => Panel;
            public override Color ImageMarginGradientMiddle => Panel;
            public override Color ImageMarginGradientEnd => Panel;
            public override Color MenuBorder => Border;
            public override Color MenuItemBorder => Hover;
            public override Color MenuItemSelected => Hover;
            public override Color SeparatorDark => Border;
            public override Color SeparatorLight => Border;
            public override Color CheckBackground => Green;
            public override Color CheckSelectedBackground => Green;
            public override Color CheckPressedBackground => Green;
        }
    }
}
