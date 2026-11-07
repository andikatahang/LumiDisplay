using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace LumiDisplay;

/// <summary>
/// Inter (SIL OFL, bundled in Fonts/) loaded from embedded resources: into a GDI+
/// PrivateFontCollection for <see cref="Font"/> objects and Graphics.DrawString, and into GDI
/// (AddFontMemResourceEx) so TextRenderer and standard controls can use the same faces.
/// Falls back to Segoe UI if anything fails.
/// </summary>
internal static class UiFonts
{
    private static readonly PrivateFontCollection Collection = new();
    private static FontFamily? _regular, _medium, _semibold;

    public static void Load()
    {
        try
        {
            var asm = typeof(UiFonts).Assembly;
            foreach (var file in new[] { "Inter-Regular.ttf", "Inter-Medium.ttf", "Inter-SemiBold.ttf" })
            {
                using var stream = asm.GetManifestResourceStream("LumiDisplay.Fonts." + file);
                if (stream == null) continue;
                var data = new byte[stream.Length];
                stream.ReadExactly(data);

                // GDI+ needs the memory for the life of the collection: allocate once, never free
                IntPtr mem = Marshal.AllocCoTaskMem(data.Length);
                Marshal.Copy(data, 0, mem, data.Length);
                Collection.AddMemoryFont(mem, data.Length);
                uint installed = 0;
                AddFontMemResourceEx(mem, (uint)data.Length, IntPtr.Zero, ref installed);
            }

            foreach (var family in Collection.Families)
            {
                switch (family.Name)
                {
                    case "Inter": _regular = family; break;
                    case "Inter Medium": _medium = family; break;
                    case "Inter SemiBold": _semibold = family; break;
                }
            }
        }
        catch
        {
            _regular = _medium = _semibold = null;
        }
    }

    public static Font Create(float size, Weight weight, FontStyle style = FontStyle.Regular, GraphicsUnit unit = GraphicsUnit.Point)
    {
        var family = weight switch
        {
            Weight.SemiBold => _semibold ?? _medium,
            Weight.Medium => _medium,
            _ => _regular
        };
        if (family != null)
            return new Font(family, size, style, unit);
        return weight == Weight.Regular
            ? new Font("Segoe UI", size, style, unit)
            : new Font("Segoe UI Semibold", size, style, unit);
    }

    public enum Weight { Regular, Medium, SemiBold }

    [DllImport("gdi32.dll")]
    private static extern IntPtr AddFontMemResourceEx(IntPtr pFileView, uint cjSize, IntPtr pvReserved, ref uint pNumFonts);
}

/// <summary>
/// LumiDisplay dark gray glass palette with a light gray accent.
/// Shared by the settings window, tray menu, OSD and the application icon.
/// </summary>
internal static class Theme
{
    public static readonly Color Window = Color.FromArgb(42, 43, 46);           // #2A2B2E page background
    public static readonly Color Surface = Color.FromArgb(51, 52, 55);          // #333437 cards, sidebar, menus
    public static readonly Color SurfaceAlt = Color.FromArgb(59, 60, 64);       // #3B3C40 hover / tiles
    public static readonly Color Border = Color.FromArgb(63, 64, 68);           // #3F4044
    public static readonly Color BorderStrong = Color.FromArgb(85, 86, 91);     // #55565B
    public static readonly Color Text = Color.FromArgb(236, 236, 238);          // #ECECEE
    public static readonly Color TextSecondary = Color.FromArgb(201, 202, 206); // #C9CACE
    public static readonly Color TextMuted = Color.FromArgb(163, 164, 169);     // #A3A4A9
    public static readonly Color TextFaint = Color.FromArgb(142, 143, 149);     // #8E8F95
    public static readonly Color Accent = Color.FromArgb(236, 236, 238);        // #ECECEE light gray
    public static readonly Color AccentHover = Color.White;                     // #FFFFFF
    public static readonly Color AccentPressed = Color.FromArgb(201, 202, 206); // #C9CACE
    public static readonly Color AccentSoft = Color.FromArgb(59, 60, 64);       // #3B3C40 accent tint
    public static readonly Color OnAccent = Color.FromArgb(38, 39, 42);         // #26272A text/glyphs on Accent
    public static readonly Color Danger = Color.FromArgb(248, 113, 113);        // #F87171
    public static readonly Color Success = Color.FromArgb(74, 222, 128);        // #4ADE80

    /// <summary>White at <paramref name="alpha"/>: hover, tracks and fills on dark glass.</summary>
    public static Color Overlay(int alpha) => Color.FromArgb(Math.Clamp(alpha, 0, 255), 255, 255, 255);

    /// <summary>Black at <paramref name="alpha"/>: drop shadows.</summary>
    public static Color Shadow(int alpha) => Color.FromArgb(Math.Clamp(alpha, 0, 255), 0, 0, 0);

    /// <summary>WCAG 2.x contrast ratio between two opaque colors (1 to 21).</summary>
    public static double ContrastRatio(Color fg, Color bg)
    {
        static double Lum(Color c)
        {
            static double Ch(int v) { double x = v / 255.0; return x <= 0.04045 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }
            return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
        }
        double a = Lum(fg), b = Lum(bg);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    /// <summary>Debug self-check: text stays readable on the dark palette.</summary>
    public static void SelfCheck()
    {
        Debug.Assert(Math.Abs(ContrastRatio(Color.White, Color.Black) - 21.0) < 0.01);
        Debug.Assert(ContrastRatio(Text, Surface) >= 7.0);
        Debug.Assert(ContrastRatio(TextSecondary, Surface) >= 4.5);
        Debug.Assert(ContrastRatio(TextMuted, Surface) >= 4.5);
        Debug.Assert(ContrastRatio(OnAccent, Accent) >= 7.0);
        Debug.Assert(ContrastRatio(Text, Window) >= 7.0);
    }

    /// <summary>Corner radius (96-DPI px) for cards, buttons, inputs, list items and the OSD.</summary>
    public const float Radius = 12f;

    // Inter in three weights; Medium for labels and buttons, SemiBold for titles
    public static Font Font(float size, FontStyle style = FontStyle.Regular) => UiFonts.Create(size, UiFonts.Weight.Regular, style);
    public static Font MediumFont(float size) => UiFonts.Create(size, UiFonts.Weight.Medium);
    public static Font SemiboldFont(float size) => UiFonts.Create(size, UiFonts.Weight.SemiBold);

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
