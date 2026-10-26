using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace LumiDisplay;

/// <summary>
/// LumiDisplay light palette: white and soft-gray surfaces with a dark blue accent.
/// Shared by the settings window, tray menu, OSD and the application icon.
/// </summary>
internal static class Theme
{
    public static readonly Color Window = Color.FromArgb(244, 246, 250);        // #F4F6FA page background
    public static readonly Color Surface = Color.White;                         // cards, sidebar, menus
    public static readonly Color SurfaceAlt = Color.FromArgb(241, 245, 249);    // #F1F5F9 hover / tiles
    public static readonly Color Border = Color.FromArgb(226, 232, 240);        // #E2E8F0
    public static readonly Color BorderStrong = Color.FromArgb(203, 213, 225);  // #CBD5E1
    public static readonly Color Text = Color.FromArgb(15, 23, 42);             // #0F172A
    public static readonly Color TextSecondary = Color.FromArgb(51, 65, 85);    // #334155
    public static readonly Color TextMuted = Color.FromArgb(100, 116, 139);     // #64748B
    public static readonly Color TextFaint = Color.FromArgb(148, 163, 184);     // #94A3B8
    public static readonly Color Accent = Color.FromArgb(30, 58, 138);          // #1E3A8A dark blue
    public static readonly Color AccentHover = Color.FromArgb(30, 64, 175);     // #1E40AF
    public static readonly Color AccentPressed = Color.FromArgb(23, 37, 84);    // #172554
    public static readonly Color AccentSoft = Color.FromArgb(234, 239, 250);    // #EAEFFA accent tint
    public static readonly Color Danger = Color.FromArgb(220, 38, 38);          // #DC2626
    public static readonly Color Success = Color.FromArgb(22, 163, 74);         // #16A34A

    public const string FontFamily = "Segoe UI";
    public const string FontFamilySemibold = "Segoe UI Semibold";

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static Color Blend(Color from, Color to, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb(
            (int)(from.A + (to.A - from.A) * t),
            (int)(from.R + (to.R - from.R) * t),
            (int)(from.G + (to.G - from.G) * t),
            (int)(from.B + (to.B - from.B) * t));
    }
}

/// <summary>
/// Line-style vector glyphs drawn on a 24-unit grid, used in the sidebar, tray menu and OSD.
/// </summary>
internal enum Glyph
{
    Gear,
    Monitor,
    Sun,
    Speaker,
    SpeakerMuted,
    Contrast,
    Keyboard,
    Sliders,
    Info,
    Power,
}

internal static class Glyphs
{
    public static void Draw(Graphics g, Glyph glyph, RectangleF r, Color color, float stroke = 1.8f)
    {
        var state = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TranslateTransform(r.X, r.Y);
        g.ScaleTransform(r.Width / 24f, r.Height / 24f);

        using var pen = new Pen(color, stroke)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        using var brush = new SolidBrush(color);

        switch (glyph)
        {
            case Glyph.Monitor:
                using (var screen = Theme.RoundedRect(new RectangleF(3, 4, 18, 12.5f), 2.2f))
                    g.DrawPath(pen, screen);
                g.DrawLine(pen, 12, 16.5f, 12, 19.5f);
                g.DrawLine(pen, 8, 20, 16, 20);
                break;

            case Glyph.Sun:
                g.DrawEllipse(pen, 8, 8, 8, 8);
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4;
                    g.DrawLine(pen,
                        12 + (float)(6.8 * Math.Cos(a)), 12 + (float)(6.8 * Math.Sin(a)),
                        12 + (float)(9.4 * Math.Cos(a)), 12 + (float)(9.4 * Math.Sin(a)));
                }
                break;

            case Glyph.Speaker:
            case Glyph.SpeakerMuted:
                var body = new[]
                {
                    new PointF(3.5f, 9.5f), new PointF(7.5f, 9.5f), new PointF(12, 5.5f),
                    new PointF(12, 18.5f), new PointF(7.5f, 14.5f), new PointF(3.5f, 14.5f)
                };
                g.DrawPolygon(pen, body);
                if (glyph == Glyph.SpeakerMuted)
                {
                    g.DrawLine(pen, 16, 9.5f, 21, 14.5f);
                    g.DrawLine(pen, 21, 9.5f, 16, 14.5f);
                }
                else
                {
                    g.DrawArc(pen, 9.5f, 8.5f, 7, 7, -50, 100);
                    g.DrawArc(pen, 7, 5, 14, 14, -50, 100);
                }
                break;

            case Glyph.Contrast:
                g.DrawEllipse(pen, 3.5f, 3.5f, 17, 17);
                g.FillPie(brush, 3.5f, 3.5f, 17, 17, 90, 180);
                break;

            case Glyph.Keyboard:
                using (var kb = Theme.RoundedRect(new RectangleF(2.5f, 6, 19, 12), 2.2f))
                    g.DrawPath(pen, kb);
                foreach (float x in new[] { 6.5f, 10f, 13.5f, 17f })
                    g.FillEllipse(brush, x - 0.95f, 9.6f - 0.95f, 1.9f, 1.9f);
                g.DrawLine(pen, 8, 14.3f, 16, 14.3f);
                break;

            case Glyph.Sliders:
                g.DrawLine(pen, 4, 7, 20, 7);
                g.DrawLine(pen, 4, 12, 20, 12);
                g.DrawLine(pen, 4, 17, 20, 17);
                foreach (var (x, y) in new[] { (9f, 7f), (15f, 12f), (7f, 17f) })
                {
                    using var knob = new SolidBrush(color);
                    g.FillEllipse(knob, x - 2.4f, y - 2.4f, 4.8f, 4.8f);
                }
                break;

            case Glyph.Gear:
                g.DrawEllipse(pen, 9, 9, 6, 6);
                g.DrawEllipse(pen, 5.2f, 5.2f, 13.6f, 13.6f);
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4 + Math.PI / 8;
                    g.DrawLine(pen,
                        12 + (float)(6.8 * Math.Cos(a)), 12 + (float)(6.8 * Math.Sin(a)),
                        12 + (float)(9.2 * Math.Cos(a)), 12 + (float)(9.2 * Math.Sin(a)));
                }
                break;

            case Glyph.Info:
                g.DrawEllipse(pen, 3, 3, 18, 18);
                g.DrawLine(pen, 12, 11, 12, 16.5f);
                g.FillEllipse(brush, 10.9f, 6.7f, 2.2f, 2.2f);
                break;

            case Glyph.Power:
                g.DrawArc(pen, 5, 5.5f, 14, 14, 305, 290);
                g.DrawLine(pen, 12, 3.5f, 12, 11);
                break;
        }

        g.Restore(state);
    }

    /// <summary>Renders a glyph into a new bitmap, e.g. for tray menu items.</summary>
    public static Bitmap ToBitmap(Glyph glyph, int size, Color color)
    {
        var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        Draw(g, glyph, new RectangleF(0, 0, size, size), color);
        return bmp;
    }
}

/// <summary>
/// The LumiDisplay mark, in the same language as the UI: a white rounded "card" tile with a
/// hairline gray border, holding a dark blue line-art monitor (soft blue screen) and a sun.
/// Used for the .ico file, the tray icon fallback and in-app branding.
/// </summary>
internal static class Logo
{
    private static readonly Color TileTop = Color.White;
    private static readonly Color TileBottom = Color.FromArgb(241, 245, 249);   // #F1F5F9

    public static void Draw(Graphics g, RectangleF r)
    {
        var state = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        float s = Math.Min(r.Width, r.Height);
        bool tiny = s <= 20;
        bool small = s <= 40;

        // White card tile with a hairline border (darker at small sizes so it reads on white)
        float borderW = Math.Max(1f, s * 0.018f);
        var tileRect = RectangleF.Inflate(r, -borderW / 2f, -borderW / 2f);
        using var tile = Theme.RoundedRect(tileRect, s * 0.225f);
        using (var grad = new LinearGradientBrush(RectangleF.Inflate(r, 0, 1), TileTop, TileBottom, LinearGradientMode.Vertical))
            g.FillPath(grad, tile);
        using (var border = new Pen(s <= 24 ? Theme.TextFaint : Theme.BorderStrong, borderW))
            g.DrawPath(border, tile);

        // Monitor: dark blue outline over a soft accent-tinted screen
        float stroke = Math.Max(1.3f, s * (tiny ? 0.095f : 0.066f));
        var screen = new RectangleF(r.X + s * 0.18f, r.Y + s * 0.21f, s * 0.64f, s * 0.45f);
        using var pen = new Pen(Theme.Accent, stroke)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        using (var screenPath = Theme.RoundedRect(screen, s * 0.075f))
        {
            using var glass = new SolidBrush(Theme.AccentSoft);
            g.FillPath(glass, screenPath);
            g.DrawPath(pen, screenPath);
        }

        float cx = r.X + s / 2f;
        g.DrawLine(pen, cx, screen.Bottom, cx, r.Y + s * 0.77f);
        g.DrawLine(pen, r.X + s * 0.34f, r.Y + s * 0.785f, r.X + s * 0.66f, r.Y + s * 0.785f);

        // Sun in the middle of the screen
        var c = new PointF(cx, screen.Y + screen.Height / 2f);
        float sunR = s * (tiny ? 0.1f : 0.078f);
        using (var sun = new SolidBrush(Theme.Accent))
            g.FillEllipse(sun, c.X - sunR, c.Y - sunR, sunR * 2, sunR * 2);

        if (!tiny)
        {
            using var rayPen = new Pen(Theme.AccentHover, Math.Max(1f, s * 0.032f))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            float r1 = sunR + s * 0.045f;
            float r2 = sunR + s * (small ? 0.085f : 0.095f);
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4;
                g.DrawLine(rayPen,
                    c.X + (float)(r1 * Math.Cos(a)), c.Y + (float)(r1 * Math.Sin(a)),
                    c.X + (float)(r2 * Math.Cos(a)), c.Y + (float)(r2 * Math.Sin(a)));
            }
        }

        g.Restore(state);
    }

    public static Bitmap ToBitmap(int size)
    {
        var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        Draw(g, new RectangleF(0, 0, size, size));
        return bmp;
    }
}
