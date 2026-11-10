using MatchReady.Sys;

namespace MatchReady.UI;

/// <summary>Colors, fonts and small building blocks shared by every window.</summary>
public static class Theme
{
    public static readonly Color Back = Color.FromArgb(22, 24, 29);
    public static readonly Color Panel = Color.FromArgb(32, 35, 42);
    public static readonly Color PanelHover = Color.FromArgb(44, 48, 57);
    public static readonly Color Border = Color.FromArgb(52, 56, 66);
    public static readonly Color Text = Color.FromArgb(232, 234, 237);
    public static readonly Color Muted = Color.FromArgb(150, 156, 164);
    public static readonly Color Green = Color.FromArgb(38, 166, 91);
    public static readonly Color GreenHover = Color.FromArgb(46, 190, 106);
    public static readonly Color Red = Color.FromArgb(214, 64, 69);
    public static readonly Color RedHover = Color.FromArgb(232, 84, 89);
    public static readonly Color Yellow = Color.FromArgb(227, 179, 65);
    public static readonly Color YellowBack = Color.FromArgb(58, 50, 25);
    public static readonly Color Link = Color.FromArgb(120, 170, 255);

    public static string Version
    {
        get
        {
            var v = typeof(Theme).Assembly.GetName().Version;
            return v == null ? "" : $"v{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    public static Font UiFont(float size, FontStyle style = FontStyle.Regular) => new("Segoe UI", size, style);

    /// <summary>When a window last opened or came to the front (focus right after that is not from the Tab key).</summary>
    static DateTime LastActivation = DateTime.MinValue;

    /// <summary>Dark colors, DPI scaling (layouts are written for 96 DPI) and a dark title bar.</summary>
    public static void Apply(Form f)
    {
        f.AutoScaleDimensions = new SizeF(96F, 96F);
        f.AutoScaleMode = AutoScaleMode.Dpi;
        f.BackColor = Back;
        f.ForeColor = Text;
        f.Font = UiFont(10);
        f.StartPosition = FormStartPosition.CenterParent;
        f.Icon = TrayIcons.AppIcon;
        f.HandleCreated += (_, _) => DarkTitleBar(f.Handle);
        // Focus that arrives with the window itself isn't keyboard navigation: don't outline it.
        f.Activated += (_, _) => LastActivation = DateTime.UtcNow;
        f.Shown += (_, _) => LastActivation = DateTime.UtcNow;
    }

    /// <summary>Documented DWM attribute (Windows 10 1809+). Older builds simply keep the light bar.</summary>
    public static void DarkTitleBar(IntPtr hwnd)
    {
        var on = 1;
        if (NativeMethods.DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)
            NativeMethods.DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
    }

    public static Button FlatButton(string text, Color back, int width = 120, int height = 36)
    {
        var b = new FlatButtonControl
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = back,
            ForeColor = Text,
            Size = new Size(width, height),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = back == Green ? GreenHover : back == Red ? RedHover : PanelHover;
        b.AccessibleName = text;
        // Keyboard users: a visible outline when the button has focus (Tab key).
        b.FlatAppearance.BorderColor = Link;
        b.GotFocus += (_, _) =>
        {
            var fromWindowOpening = (DateTime.UtcNow - LastActivation).TotalMilliseconds < 500;
            if (Control.MouseButtons == MouseButtons.None && !fromWindowOpening) b.FlatAppearance.BorderSize = 2;
        };
        b.LostFocus += (_, _) => b.FlatAppearance.BorderSize = 0;
        b.MouseDown += (_, _) => b.FlatAppearance.BorderSize = 0;
        return b;
    }

    public static LinkLabel LinkText(string text) => new()
    {
        Text = text,
        AutoSize = true,
        LinkColor = Link,
        ActiveLinkColor = Text,
        VisitedLinkColor = Link,
        LinkBehavior = LinkBehavior.HoverUnderline,
    };

    public static Label Heading(string text, float size = 11.5f) => new()
    {
        Text = text, AutoSize = true, Font = UiFont(size, FontStyle.Bold), Margin = new Padding(0, 14, 0, 4), UseMnemonic = false,
    };

    public static Label MutedText(string text, float size = 9) => new()
    {
        Text = text, AutoSize = true, ForeColor = Muted, Font = UiFont(size), Margin = new Padding(0, 0, 0, 4), UseMnemonic = false,
    };

    /// <summary>A two-button question. Returns true when the first (action) button is chosen; the second is the safe default.</summary>
    public static bool Ask(IWin32Window owner, string title, string message, string yes, string no)
    {
        using var f = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(420, 170),
        };
        Apply(f);
        var lbl = new Label { Text = message, Location = new Point(18, 16), Size = new Size(384, 90), Font = UiFont(10.5f) };
        var bYes = FlatButton(yes, Panel, 180);
        var bNo = FlatButton(no, Green, 180);
        bNo.Location = new Point(222, 118);
        bYes.Location = new Point(18, 118);
        bYes.DialogResult = DialogResult.Yes;
        bNo.DialogResult = DialogResult.No;
        f.Controls.AddRange(new Control[] { lbl, bYes, bNo });
        f.AcceptButton = bNo; // Enter = the safe choice
        f.CancelButton = bNo;
        f.ActiveControl = bNo;
        return f.ShowDialog(owner) == DialogResult.Yes;
    }
}

/// <summary>Flat button without Windows' thin dotted focus box; keyboard focus gets a clear blue outline instead.</summary>
public sealed class FlatButtonControl : Button
{
    protected override bool ShowFocusCues => false;
}

/// <summary>
/// A vertical list that lays itself out from the fonts, so it looks right at any Windows display scale.
/// Text labels wrap to the list's width.
/// </summary>
public class Stack : FlowLayoutPanel
{
    public Stack()
    {
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        AutoScroll = true;
        BackColor = Theme.Back;
        ForeColor = Theme.Text;
    }

    public T Add<T>(T c) where T : Control
    {
        Controls.Add(c);
        return c;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        var width = ClientSize.Width - Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 4;
        if (width > 50)
        {
            foreach (Control c in Controls)
            {
                if (c is Label { AutoSize: true } l) l.MaximumSize = new Size(width - l.Margin.Horizontal, 0);
                else if (c is Row r) r.Width = width - r.Margin.Horizontal;
            }
        }
        base.OnLayout(e);
    }
}

/// <summary>One line with text on the left and an optional action on the right.</summary>
public sealed class Row : Panel
{
    public Label TextLabel { get; }
    public Control? Action { get; }
    readonly Label? _note;

    /// <summary>
    /// Sizes come from the font, so rows look right at any display scale. Build rows after the form has loaded
    /// (or in a list that isn't auto-scaled), so they aren't scaled twice.
    /// </summary>
    public Row(string text, Control? action = null, string? note = null)
    {
        BackColor = Theme.Panel;
        Margin = new Padding(0, 0, 0, 4);
        var font = Theme.UiFont(10);
        var line = TextRenderer.MeasureText("Ag", font).Height;
        TextLabel = new Label
        {
            Text = text, AutoSize = false, Location = new Point(line / 2, line / 3), Height = line + 2,
            Font = font, AutoEllipsis = true, UseMnemonic = false,
        };
        if (note != null)
        {
            var small = Theme.UiFont(8.5f);
            _note = new Label
            {
                Text = note, AutoSize = false, Location = new Point(line / 2, TextLabel.Bottom), ForeColor = Theme.Muted,
                Height = TextRenderer.MeasureText("Ag", small).Height + 2, Font = small, AutoEllipsis = true, UseMnemonic = false,
            };
        }
        Height = (_note?.Bottom ?? TextLabel.Bottom) + line / 3;
        Controls.Add(TextLabel);
        if (_note != null) Controls.Add(_note);
        Action = action;
        if (action != null) Controls.Add(action);
    }

    /// <summary>A small text button for the right side of a row, sized from its text.</summary>
    public static Button SmallButton(string text)
    {
        var font = Theme.UiFont(9);
        var size = TextRenderer.MeasureText(text, font);
        var b = Theme.FlatButton(text, Theme.Back, size.Width + size.Height, size.Height + size.Height / 2);
        b.Font = font;
        b.ForeColor = Theme.Link;
        return b;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (TextLabel == null) return; // setting sizes in the constructor fires this before the label exists
        var pad = TextLabel.Left;
        var right = Width - pad;
        if (Action != null)
        {
            Action.Location = new Point(right - Action.Width, (Height - Action.Height) / 2);
            right = Action.Left - pad;
        }
        TextLabel.Width = Math.Max(20, right - TextLabel.Left);
        if (_note != null) _note.Width = TextLabel.Width;
    }
}
/// <summary>Dark right-click menu for the tray icon.</summary>
public sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkColors()) { RoundedEdges = false; }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Muted;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var r = new Rectangle(e.ImageRectangle.X + 2, e.ImageRectangle.Y + 2, e.ImageRectangle.Width - 4, e.ImageRectangle.Height - 4);
        using var b = new SolidBrush(Theme.Green);
        e.Graphics.FillRectangle(b, r);
        TextRenderer.DrawText(e.Graphics, "✓", Theme.UiFont(8, FontStyle.Bold), r, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    sealed class DarkColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.Panel;
        public override Color ImageMarginGradientBegin => Theme.Panel;
        public override Color ImageMarginGradientMiddle => Theme.Panel;
        public override Color ImageMarginGradientEnd => Theme.Panel;
        public override Color MenuBorder => Theme.Border;
        public override Color MenuItemBorder => Theme.PanelHover;
        public override Color MenuItemSelected => Theme.PanelHover;
        public override Color SeparatorDark => Theme.Border;
        public override Color SeparatorLight => Theme.Border;
        public override Color CheckBackground => Theme.Green;
        public override Color CheckSelectedBackground => Theme.Green;
        public override Color CheckPressedBackground => Theme.Green;
    }
}
