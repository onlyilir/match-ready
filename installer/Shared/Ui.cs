using System.Drawing;
using System.Windows.Forms;

namespace MatchReady.Installer
{
    /// <summary>Same dark look as the app.</summary>
    internal static class Ui
    {
        public static readonly Color Back = Color.FromArgb(22, 24, 29);
        public static readonly Color Panel = Color.FromArgb(32, 35, 42);
        public static readonly Color Text = Color.FromArgb(232, 234, 237);
        public static readonly Color Muted = Color.FromArgb(154, 160, 166);
        public static readonly Color Green = Color.FromArgb(38, 166, 91);
        public static readonly Color Red = Color.FromArgb(214, 64, 69);
        public static readonly Color Yellow = Color.FromArgb(227, 179, 65);

        public static Font Font(float size, FontStyle style = FontStyle.Regular) => new Font("Segoe UI", size, style);

        public static void Apply(Form f, string title, int width, int height)
        {
            f.Text = title;
            f.BackColor = Back;
            f.ForeColor = Text;
            f.Font = Font(10);
            f.AutoScaleMode = AutoScaleMode.Dpi;
            f.FormBorderStyle = FormBorderStyle.FixedSingle;
            f.MaximizeBox = false;
            f.StartPosition = FormStartPosition.CenterScreen;
            f.ClientSize = new Size(width, height);
            try { f.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { /* default icon */ }
        }

        public static Button Button(string text, Color back, int x, int y, int w = 130, int h = 38)
        {
            var b = new Button
            {
                Text = text, FlatStyle = FlatStyle.Flat, BackColor = back, ForeColor = Text,
                Location = new Point(x, y), Size = new Size(w, h), Cursor = Cursors.Hand,
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        public static Label Label(string text, int x, int y, int w, int h, Color? color = null, float size = 10)
        {
            return new Label
            {
                Text = text, Location = new Point(x, y), Size = new Size(w, h),
                ForeColor = color ?? Text, Font = Font(size),
            };
        }

        public static bool YesNo(IWin32Window owner, string message, string title, bool defaultYes)
        {
            return MessageBox.Show(owner, message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                defaultYes ? MessageBoxDefaultButton.Button1 : MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }
    }
}
