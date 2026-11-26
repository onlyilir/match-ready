using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MatchReady.Sys;

namespace MatchReady.UI;

/// <summary>Loads the theme and follows Windows' high-contrast setting.</summary>
public static class ThemeManager
{
    static ResourceDictionary? _theme;

    public static void Apply(Application app)
    {
        _theme ??= new ResourceDictionary { Source = new Uri("pack://application:,,,/MatchReady;component/UI/Theme.xaml") };
        if (!app.Resources.MergedDictionaries.Contains(_theme)) app.Resources.MergedDictionaries.Add(_theme);
        ApplyContrast(app);
        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SystemParameters.HighContrast)) app.Dispatcher.Invoke(() => ApplyContrast(app));
        };
    }

    /// <summary>Tests only: render as if high-contrast mode were on, without touching Windows settings.</summary>
    internal static bool ForceHighContrast { get; set; }

    /// <summary>High contrast: every brush becomes the matching Windows system color, so the user's theme wins.</summary>
    internal static void ApplyContrast(Application app)
    {
        var on = SystemParameters.HighContrast || ForceHighContrast;
        var map = new (string Key, Brush Brush)[]
        {
            ("BackBrush", SystemColors.WindowBrush), ("SurfaceBrush", SystemColors.WindowBrush),
            ("SurfaceHighBrush", SystemColors.ControlBrush), ("BorderBrush", SystemColors.WindowTextBrush),
            ("TextBrush", SystemColors.WindowTextBrush), ("MutedBrush", SystemColors.GrayTextBrush),
            ("AccentBrush", SystemColors.HighlightBrush), ("AccentTextBrush", SystemColors.HighlightTextBrush),
            ("DangerBrush", SystemColors.HighlightBrush), ("WarnBrush", SystemColors.WindowTextBrush),
            ("WarnBackBrush", SystemColors.WindowBrush), ("LinkBrush", SystemColors.HotTrackBrush),
            ("FocusBrush", SystemColors.HighlightBrush), ("HoverOverlayBrush", SystemColors.HighlightBrush),
        };
        foreach (var (key, brush) in map)
        {
            if (on) app.Resources[key] = brush;
            else app.Resources.Remove(key); // fall back to the theme's own colors
        }
    }

    public static bool HighContrast => SystemParameters.HighContrast || ForceHighContrast;
}

/// <summary>Small building blocks so every window looks and behaves the same.</summary>
public static class Ui
{
    public static readonly ImageSource AppIcon =
        BitmapFrame.Create(new Uri("pack://application:,,,/MatchReady;component/Assets/MatchReady.ico"));

    /// <summary>Common window setup: theme, icon, dark title bar, centered on its owner.</summary>
    public static void Prepare(Window w, string title, Window? owner = null)
    {
        w.Title = title;
        w.Style = (Style)Application.Current.FindResource("AppWindow");
        w.Icon = AppIcon;
        if (owner != null && owner.IsVisible)
        {
            w.Owner = owner;
            w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            w.ShowInTaskbar = false;
        }
        else
        {
            w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        w.SourceInitialized += (_, _) => DarkTitleBar(new WindowInteropHelper(w).Handle);
    }

    /// <summary>Documented DWM attribute (Windows 10 1809+). Older builds simply keep the light bar.</summary>
    public static void DarkTitleBar(IntPtr hwnd)
    {
        if (ThemeManager.HighContrast) return;
        var on = 1;
        if (NativeMethods.DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)
            NativeMethods.DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
    }

    public static Style S(string key) => (Style)Application.Current.FindResource(key);

    public static TextBlock Heading(string text, double top = 18) =>
        new() { Text = text, Style = S("H2"), Margin = new Thickness(0, top, 0, 6) };

    public static TextBlock SubHeading(string text) => new() { Text = text, Style = S("H3") };

    public static TextBlock Body(string text) => new() { Text = text, Style = S("Body") };

    public static TextBlock Muted(string text, Thickness? margin = null) =>
        new() { Text = text, Style = S("Muted"), Margin = margin ?? new Thickness(0, 0, 0, 4) };

    public static Button Button(string text, string style = "SecondaryButton", RoutedEventHandler? click = null)
    {
        var b = new Button { Content = text, Style = S(style) };
        System.Windows.Automation.AutomationProperties.SetName(b, text);
        if (click != null) b.Click += click;
        return b;
    }

    public static Button Link(string text, RoutedEventHandler click)
    {
        var b = new Button { Content = text, Style = S("LinkButton") };
        System.Windows.Automation.AutomationProperties.SetName(b, text);
        b.Click += click;
        return b;
    }

    public static CheckBox Check(string text, bool value, string? help = null)
    {
        var c = new CheckBox { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, IsChecked = value };
        System.Windows.Automation.AutomationProperties.SetName(c, text);
        if (help != null) System.Windows.Automation.AutomationProperties.SetHelpText(c, help);
        return c;
    }

    /// <summary>A rounded row: text (and an optional note) on the left, an optional action on the right.</summary>
    public static Border Row(string text, UIElement? action = null, string? note = null)
    {
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis });
        if (note != null) left.Children.Add(new TextBlock { Text = note, Style = S("Muted"), Margin = new Thickness(0, 2, 0, 0) });
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(left);
        if (action != null)
        {
            Grid.SetColumn(action, 1);
            if (action is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(action);
        }
        return new Border { Style = S("Card"), Padding = new Thickness(14, 9, 8, 9), Margin = new Thickness(0, 0, 0, 6), Child = grid };
    }

    /// <summary>Scrollable page with a fixed bottom bar of buttons (right-aligned) and optional links (left).</summary>
    public static (DockPanel Root, StackPanel Content, DockPanel Bar) Page(double padding = 24)
    {
        var root = new DockPanel { LastChildFill = true };
        var bar = new DockPanel { Margin = new Thickness(padding, 12, padding, 16), LastChildFill = false };
        DockPanel.SetDock(bar, Dock.Bottom);
        root.Children.Add(bar);
        var content = new StackPanel { Margin = new Thickness(padding, 12, padding, 12) };
        root.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = content, Focusable = false,
        });
        return (root, content, bar);
    }

    public static void AddRight(DockPanel bar, params UIElement[] items)
    {
        foreach (var item in items.Reverse())
        {
            DockPanel.SetDock(item, Dock.Right);
            if (item is FrameworkElement fe) fe.Margin = new Thickness(8, 0, 0, 0);
            bar.Children.Add(item);
        }
    }

    public static void AddLeft(DockPanel bar, params UIElement[] items)
    {
        foreach (var item in items)
        {
            DockPanel.SetDock(item, Dock.Left);
            if (item is FrameworkElement fe) { fe.Margin = new Thickness(0, 0, 16, 0); fe.VerticalAlignment = VerticalAlignment.Center; }
            bar.Children.Add(item);
        }
    }

    /// <summary>A two-button question. True when the first (action) button is chosen; the second is the safe default.</summary>
    public static bool Ask(Window? owner, string title, string message, string yes, string no)
    {
        var w = new Window { Width = 440, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize };
        Prepare(w, title, owner);
        var (root, content, bar) = Page();
        content.Children.Add(new TextBlock { Text = message, Style = S("Body"), FontSize = 15 });
        var bYes = Button(yes, "SecondaryButton", (_, _) => w.DialogResult = true);
        var bNo = Button(no, "PrimaryButton", (_, _) => w.DialogResult = false);
        bNo.IsDefault = true;   // Enter = the safe choice
        bNo.IsCancel = true;
        AddRight(bar, bYes, bNo);
        w.Content = root;
        w.Loaded += (_, _) => bNo.Focus();
        return w.ShowDialog() == true;
    }

    /// <summary>One-button notice.</summary>
    public static void Tell(Window? owner, string title, string message)
    {
        var w = new Window { Width = 440, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize };
        Prepare(w, title, owner);
        var (root, content, bar) = Page();
        content.Children.Add(new TextBlock { Text = message, Style = S("Body"), FontSize = 15 });
        var ok = Button("OK", "PrimaryButton", (_, _) => w.Close());
        ok.IsDefault = true;
        ok.IsCancel = true;
        AddRight(bar, ok);
        w.Content = root;
        w.ShowDialog();
    }

    public static string Version
    {
        get
        {
            var v = typeof(Ui).Assembly.GetName().Version;
            return v == null ? "" : $"v{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    public static string FormatBytes(long b) =>
        b >= 1024L * 1024 * 1024 ? $"{b / (1024.0 * 1024 * 1024):0.0} GB" : $"{b / (1024 * 1024)} MB";

    public static string Duration(TimeSpan t) =>
        t.TotalMinutes < 1 ? "under a minute"
        : t.TotalHours < 1 ? $"{(int)t.TotalMinutes} min"
        : $"{(int)t.TotalHours} h {t.Minutes} min";

    /// <summary>"1:05:09" style clock for the live dashboard.</summary>
    public static string Clock(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
}
