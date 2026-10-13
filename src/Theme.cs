using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace COTK.Launcher;

internal static class Theme
{
    // The palette follows the Eturnum theme shipped by the game client.
    public static readonly Color BgTop = Hex("#15171d");
    public static readonly Color BgMid = Hex("#090a0e");
    public static readonly Color BgBot = Hex("#11090b");
    public static readonly Color Deep = Hex("#08090c");
    public static readonly Color Panel = Hex("#17191e");
    public static readonly Color Elev = Hex("#22252b");

    public static readonly Color Red300 = Hex("#fb8b8f");
    public static readonly Color Red400 = Hex("#e94e54");
    public static readonly Color Red500 = Hex("#bd2026");
    public static readonly Color Red600 = Hex("#98151a");
    public static readonly Color Red700 = Hex("#761014");

    public static readonly Color Steel = Hex("#4a5a6e");
    public static readonly Color SteelDark = Hex("#252d37");
    public static readonly Color Border = Hex("#58616d");
    public static readonly Color GoldBorder = Hex("#804700");
    public static readonly Color Amber = Hex("#c88c1a");
    public static readonly Color Warn = Hex("#ffc800");
    public static readonly Color Ok = Hex("#7bbf6a");

    public static readonly Color Ink = Hex("#eef1f4");
    public static readonly Color InkDim = Hex("#a7b0ba");
    public static readonly Color InkMute = Hex("#6d7681");

    public static Color Hex(string value) => Color.FromArgb(
        Convert.ToInt32(value.Substring(1, 2), 16),
        Convert.ToInt32(value.Substring(3, 2), 16),
        Convert.ToInt32(value.Substring(5, 2), 16));

    private static readonly PrivateFontCollection FRegular = new();
    private static readonly PrivateFontCollection FSemi = new();
    private static readonly PrivateFontCollection FBold = new();

    static Theme()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "fonts");
        try
        {
            FRegular.AddFontFile(Path.Combine(dir, "oswald-400.ttf"));
            FSemi.AddFontFile(Path.Combine(dir, "oswald-600.ttf"));
            FBold.AddFontFile(Path.Combine(dir, "oswald-700.ttf"));
        }
        catch
        {
            // Arial Narrow preserves the condensed client-menu look when assets are absent.
        }
    }

    public static Font Display(float size, FontStyle style = FontStyle.Regular)
    {
        try
        {
            return style switch
            {
                FontStyle.Bold => new Font(FBold.Families[0], size, FontStyle.Regular),
                (FontStyle)8 => new Font(FSemi.Families[0], size, FontStyle.Regular),
                _ => new Font(FRegular.Families[0], size, FontStyle.Regular),
            };
        }
        catch
        {
            return new Font("Arial Narrow", size, style);
        }
    }
}

internal sealed class ClientPanel : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color AccentColor { get; set; } = Theme.Border;

    public ClientPanel()
    {
        SetStyle(
            ControlStyles.UserPaint
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var fill = new LinearGradientBrush(
                   rect,
                   Color.FromArgb(238, Theme.Elev),
                   Color.FromArgb(244, Theme.Panel),
                   LinearGradientMode.Vertical))
            g.FillRectangle(fill, rect);

        using (var inner = new Pen(Color.FromArgb(28, Color.White)))
            g.DrawRectangle(inner, 1, 1, Width - 3, Height - 3);
        using (var border = new Pen(Color.FromArgb(150, Theme.Border)))
            g.DrawRectangle(border, rect);
        using (var accent = new SolidBrush(AccentColor))
            g.FillRectangle(accent, 0, 0, Width, 3);

        base.OnPaint(e);
    }
}

internal sealed class StatusDot : Control
{
    private Color _dotColor = Theme.InkMute;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color DotColor
    {
        get => _dotColor;
        set { _dotColor = value; Invalidate(); }
    }

    public StatusDot()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Size = new Size(14, 14);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var glow = new SolidBrush(Color.FromArgb(45, DotColor));
        using var dot = new SolidBrush(DotColor);
        e.Graphics.FillEllipse(glow, 0, 0, Width - 1, Height - 1);
        e.Graphics.FillEllipse(dot, 4, 4, Width - 9, Height - 9);
    }
}

internal sealed class GradientButton : Control
{
    private bool _hover;
    private bool _pressed;
    private readonly Color _top;
    private readonly Color _mid;
    private readonly Color _bottom;
    private readonly Color _border;
    private readonly Color _hoverTop;
    private readonly Color _hoverBottom;

    public GradientButton(string text, Color top, Color mid, Color bottom, Color border,
        Color? hoverTop = null, Color? hoverBottom = null)
    {
        SetStyle(
            ControlStyles.UserPaint
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.SupportsTransparentBackColor
            | ControlStyles.Selectable,
            true);
        Text = text.ToUpperInvariant();
        Font = Theme.Display(11F, FontStyle.Bold);
        Cursor = Cursors.Hand;
        Height = 44;
        TabStop = true;
        _top = top;
        _mid = mid;
        _bottom = bottom;
        _border = border;
        _hoverTop = hoverTop ?? top;
        _hoverBottom = hoverBottom ?? bottom;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            e.Handled = true;
            OnClick(EventArgs.Empty);
        }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var path = ButtonPath(new Rectangle(0, 0, Width - 1, Height - 1));
        var top = _hover ? _hoverTop : _top;
        var bottom = _hover ? _hoverBottom : _bottom;
        if (_pressed)
        {
            top = ControlPaint.Dark(top, 0.08f);
            bottom = ControlPaint.Dark(bottom, 0.08f);
        }

        using (var gradient = new LinearGradientBrush(ClientRectangle, Color.Black, Color.Black, LinearGradientMode.Vertical))
        {
            gradient.InterpolationColors = new ColorBlend(3)
            {
                Colors = new[] { top, _mid, bottom },
                Positions = new[] { 0f, 0.52f, 1f },
            };
            g.FillPath(gradient, path);
        }

        using (var highlight = new Pen(Color.FromArgb(_hover ? 92 : 45, Color.White)))
            g.DrawLine(highlight, 6, 1, Width - 7, 1);
        using (var border = new Pen(Focused ? Theme.Warn : _border))
            g.DrawPath(border, path);

        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
        var textColor = Enabled ? Color.White : Color.FromArgb(115, Color.White);
        var textRect = new Rectangle(0, _pressed ? 1 : 0, Width, Height);
        TextRenderer.DrawText(g, Text, Font, textRect, textColor, flags);
    }

    private static GraphicsPath ButtonPath(Rectangle rect)
    {
        const int cut = 5;
        var path = new GraphicsPath();
        path.AddPolygon(new[]
        {
            new Point(rect.Left + cut, rect.Top),
            new Point(rect.Right, rect.Top),
            new Point(rect.Right, rect.Bottom - cut),
            new Point(rect.Right - cut, rect.Bottom),
            new Point(rect.Left, rect.Bottom),
            new Point(rect.Left, rect.Top + cut),
        });
        path.CloseFigure();
        return path;
    }
}
