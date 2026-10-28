using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace LumiDisplay;

/// <summary>A window that provides the glass backdrop for the controls inside it.</summary>
internal interface IGlassHost
{
    GlassBackdrop Backdrop { get; }
}

/// <summary>A rounded translucent panel (card, sidebar); controls inside it sit on the surface layer.</summary>
internal interface IGlassSurface
{
    bool IsGlassSurface { get; }
}

/// <summary>
/// "Liquid glass" for the settings window, built from the desktop wallpaper the way Windows'
/// Mica is: the wallpaper under the window, blurred heavily and washed toward white.
///
/// Two layers are pre-rendered at the window's client size, so every control copies exactly the
/// pixels behind it (no seams between controls, no per-paint blurring):
///   Base    — the window background (blurred wallpaper + light wash)
///   Surface — Base plus the white wash of a glass panel (cards, sidebar)
/// Rebuilt when the window is shown, moved, resized or the wallpaper changes.
/// </summary>
internal sealed class GlassBackdrop : IDisposable
{
    public Bitmap? Base { get; private set; }
    public Bitmap? Surface { get; private set; }

    /// <summary>Average color along the top edge, for a title bar that blends in.</summary>
    public Color TopColor { get; private set; } = Theme.Window;

    public void Rebuild(Form form)
    {
        if (!form.IsHandleCreated || form.ClientSize.Width <= 0 || form.ClientSize.Height <= 0) return;

        var client = form.RectangleToScreen(form.ClientRectangle);
        var monitor = Screen.FromRectangle(client).Bounds;

        var baseLayer = new Bitmap(client.Width, client.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(baseLayer))
        {
            var wallpaper = Glass.BlurredWallpaper(monitor.Size);
            if (wallpaper != null)
            {
                // The part of the (blurred) wallpaper that sits under the window
                float k = wallpaper.Width / (float)monitor.Width;
                var src = new RectangleF((client.X - monitor.X) * k, (client.Y - monitor.Y) * k, client.Width * k, client.Height * k);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                using var attrs = new ImageAttributes();
                attrs.SetWrapMode(WrapMode.TileFlipXY);
                g.DrawImage(wallpaper, new Rectangle(0, 0, client.Width, client.Height),
                    src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel, attrs);
            }
            else
            {
                // No wallpaper available: a calm blue-gray field
                using var grad = new LinearGradientBrush(new Rectangle(0, 0, client.Width, client.Height),
                    Color.FromArgb(178, 205, 236), Color.FromArgb(214, 226, 242), 35f);
                g.FillRectangle(grad, 0, 0, client.Width, client.Height);
            }
            using var wash = new SolidBrush(Glass.WindowWash);
            g.FillRectangle(wash, 0, 0, client.Width, client.Height);
        }

        var surfaceLayer = new Bitmap(baseLayer);
        using (var g = Graphics.FromImage(surfaceLayer))
        using (var wash = new SolidBrush(Glass.SurfaceWash))
            g.FillRectangle(wash, 0, 0, client.Width, client.Height);

        TopColor = AverageTop(baseLayer);
        Base?.Dispose();
        Surface?.Dispose();
        Base = baseLayer;
        Surface = surfaceLayer;
    }

    private static Color AverageTop(Bitmap bmp)
    {
        long r = 0, gr = 0, b = 0;
        int n = 0;
        int rows = Math.Min(6, bmp.Height);
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < bmp.Width; x += 8)
            {
                var c = bmp.GetPixel(x, y);
                r += c.R; gr += c.G; b += c.B; n++;
            }
        return n == 0 ? Theme.Window : Color.FromArgb((int)(r / n), (int)(gr / n), (int)(b / n));
    }

    public void Dispose()
    {
        Base?.Dispose();
        Surface?.Dispose();
        Base = Surface = null;
    }
}

internal static class Glass
{
    /// <summary>Wash over the wallpaper for the window background: keeps any wallpaper light and calm.</summary>
    public static readonly Color WindowWash = Color.FromArgb(176, 244, 247, 251);

    /// <summary>Extra wash of a glass panel (card, sidebar) over the window background.</summary>
    public static readonly Color SurfaceWash = Color.FromArgb(150, 255, 255, 255);

    /// <summary>Bright rim along the top edge of glass, as if lit from above.</summary>
    public static readonly Color RimTop = Color.FromArgb(235, 255, 255, 255);
    public static readonly Color RimBottom = Color.FromArgb(120, 255, 255, 255);

    /// <summary>Faint dark outline so glass edges read on light wallpapers.</summary>
    public static readonly Color Hairline = Color.FromArgb(26, 15, 23, 42);

    /// <summary>Divider between rows inside a card.</summary>
    public static readonly Color Divider = Color.FromArgb(20, 15, 23, 42);

    // --- Painting helpers ---

    /// <summary>Paints what is behind <paramref name="c"/>: the window background or, inside a glass panel, the panel's surface.</summary>
    public static void PaintBehind(Graphics g, Control c)
    {
        var host = c.FindForm() as IGlassHost;
        bool onSurface = IsOnSurface(c);
        var layer = onSurface ? host?.Backdrop.Surface : host?.Backdrop.Base;
        if (host == null || layer == null)
        {
            using var solid = new SolidBrush(onSurface ? Theme.Surface : Theme.Window);
            g.FillRectangle(solid, 0, 0, c.Width, c.Height);
            return;
        }

        var origin = ((Control)host).PointToClient(c.PointToScreen(Point.Empty));
        var state = g.Save();
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImage(layer, new Rectangle(0, 0, c.Width, c.Height), new Rectangle(origin, c.Size), GraphicsUnit.Pixel);
        g.Restore(state);
    }

    /// <summary>
    /// Fills <paramref name="path"/> (in <paramref name="c"/>'s coordinates) with the glass surface layer,
    /// antialiased, and draws the lit rim and hairline outline. Used by cards and the sidebar.
    /// </summary>
    public static void PaintSurface(Graphics g, Control c, RectangleF rect, float radius)
    {
        using var path = Theme.RoundedRect(rect, radius);
        var host = c.FindForm() as IGlassHost;
        var layer = host?.Backdrop.Surface;
        if (host != null && layer != null)
        {
            var origin = ((Control)host).PointToClient(c.PointToScreen(Point.Empty));
            using var brush = new TextureBrush(layer, WrapMode.Clamp);
            brush.TranslateTransform(-origin.X, -origin.Y);
            g.FillPath(brush, path);
        }
        else
        {
            using var solid = new SolidBrush(Theme.Surface);
            g.FillPath(solid, path);
        }
        DrawRim(g, rect, radius, c.DeviceDpi / 96f);
    }

    public static void DrawRim(Graphics g, RectangleF rect, float radius, float scale)
    {
        using (var outline = Theme.RoundedRect(rect, radius))
        using (var hair = new Pen(Hairline, 1f))
            g.DrawPath(hair, outline);

        var inner = RectangleF.Inflate(rect, -1f, -1f);
        using var rimPath = Theme.RoundedRect(inner, Math.Max(0, radius - 1f));
        using var rimBrush = new LinearGradientBrush(new RectangleF(inner.X, inner.Y - 1, inner.Width, inner.Height + 2),
            RimTop, RimBottom, LinearGradientMode.Vertical);
        using var rim = new Pen(rimBrush, Math.Max(1f, scale * 0.9f));
        g.DrawPath(rim, rimPath);
    }

    private static bool IsOnSurface(Control c)
    {
        for (var p = c.Parent; p != null; p = p.Parent)
        {
            if (p is IGlassSurface s && s.IsGlassSurface) return true;
            if (p is Form) break;
        }
        return false;
    }

    // --- Wallpaper ---

    private static Bitmap? _wallpaper;
    private static string? _wallpaperKey;

    /// <summary>
    /// The desktop wallpaper as it fills a monitor of <paramref name="monitor"/> size, reduced to a small,
    /// heavily blurred image (cached). Null when no wallpaper can be read.
    /// </summary>
    public static Bitmap? BlurredWallpaper(Size monitor)
    {
        string? path = WallpaperPath();
        Color? solid = path == null ? DesktopColor() : null;
        string key = $"{path}|{solid}|{monitor.Width}x{monitor.Height}|{(path != null && System.IO.File.Exists(path) ? System.IO.File.GetLastWriteTimeUtc(path).Ticks : 0)}";
        if (key == _wallpaperKey) return _wallpaper;

        _wallpaper?.Dispose();
        _wallpaper = null;
        _wallpaperKey = key;
        try
        {
            if (path != null)
            {
                using var image = Image.FromFile(path);
                _wallpaper = BlurFill(image, monitor);
            }
            else if (solid != null)
            {
                _wallpaper = new Bitmap(64, Math.Max(1, 64 * monitor.Height / Math.Max(1, monitor.Width)));
                using var g = Graphics.FromImage(_wallpaper);
                g.Clear(solid.Value);
            }
        }
        catch
        {
            _wallpaper = null;
        }
        return _wallpaper;
    }

    /// <summary>Forgets the cached wallpaper (call when Windows reports a desktop change).</summary>
    public static void InvalidateWallpaper() => _wallpaperKey = null;

    private static Bitmap BlurFill(Image image, Size monitor)
    {
        // "Fill" placement: scale to cover the monitor, centered
        float scale = Math.Max(monitor.Width / (float)image.Width, monitor.Height / (float)image.Height);
        float srcW = monitor.Width / scale, srcH = monitor.Height / scale;
        var src = new RectangleF((image.Width - srcW) / 2f, (image.Height - srcH) / 2f, srcW, srcH);

        // Down to a tiny image (the blur), then up to a smooth mid-size image to sample from
        int tinyW = 48, tinyH = Math.Max(1, (int)Math.Round(48f * monitor.Height / monitor.Width));
        using var tiny = new Bitmap(tinyW, tinyH, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(tiny))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(image, new Rectangle(0, 0, tinyW, tinyH), src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel);
        }

        int midW = 384, midH = Math.Max(1, (int)Math.Round(384f * monitor.Height / monitor.Width));
        var mid = new Bitmap(midW, midH, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(mid))
        using (var attrs = new ImageAttributes())
        {
            attrs.SetWrapMode(WrapMode.TileFlipXY);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(tiny, new Rectangle(0, 0, midW, midH), 0, 0, tinyW, tinyH, GraphicsUnit.Pixel, attrs);
        }
        return mid;
    }

    private static string? WallpaperPath()
    {
        try
        {
            var sb = new StringBuilder(520);
            if (SystemParametersInfo(SPI_GETDESKWALLPAPER, (uint)sb.Capacity, sb, 0))
            {
                var p = sb.ToString();
                if (!string.IsNullOrWhiteSpace(p) && System.IO.File.Exists(p)) return p;
            }
            // Windows keeps a copy of the current wallpaper here (also for Spotlight / slideshow)
            var transcoded = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "Windows", "Themes", "TranscodedWallpaper");
            if (System.IO.File.Exists(transcoded)) return transcoded;
        }
        catch { }
        return null;
    }

    private static Color? DesktopColor()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Colors");
            var parts = (key?.GetValue("Background") as string)?.Split(' ');
            if (parts is { Length: 3 })
                return Color.FromArgb(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
        }
        catch { }
        return null;
    }

    private const uint SPI_GETDESKWALLPAPER = 0x0073;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, StringBuilder pvParam, uint fWinIni);
}

/// <summary>
/// Panel on the glass: paints what is behind it, and optionally its own rounded glass surface
/// (sidebar) or an extra rounded fill (status block, input frame).
/// </summary>
internal class GlassPanel : Panel, IGlassSurface
{
    public GlassPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint
               | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.UserPaint
               | ControlStyles.ResizeRedraw
               | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    /// <summary>Draw a rounded glass panel (children then sit on the surface layer).</summary>
    public bool IsSurface { get; set; }
    public bool IsGlassSurface => IsSurface;

    /// <summary>Optional rounded fill drawn on top (e.g. white for an input frame).</summary>
    public Color? Fill { get; set; }

    /// <summary>Hairline outline around the rounded fill.</summary>
    public bool Outline { get; set; }

    public float Radius { get; set; } = Theme.Radius;

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        Glass.PaintBehind(g, this);

        float s = DeviceDpi / 96f;
        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        if (IsSurface)
            Glass.PaintSurface(g, this, rect, Radius * s);

        if (Fill is Color fill)
        {
            using var path = Theme.RoundedRect(rect, Radius * s);
            using var brush = new SolidBrush(fill);
            g.FillPath(brush, path);
            if (Outline)
            {
                using var pen = new Pen(Glass.Hairline, 1f);
                g.DrawPath(pen, path);
            }
        }
    }

    // Scrolling moves painted pixels; the glass behind must be redrawn for the new positions
    protected override void OnScroll(ScrollEventArgs se)
    {
        base.OnScroll(se);
        Invalidate(true);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (AutoScroll) Invalidate(true);
    }
}

/// <summary>
/// Scrollable page on the glass, with a slim overlay scrollbar instead of the classic Windows one.
/// Controls go into <see cref="Content"/>; call <see cref="UpdateContentHeight"/> after adding them.
/// </summary>
internal sealed class GlassScrollPanel : GlassPanel
{
    public GlassPanel Content { get; } = new();
    private readonly ThinScrollBar _bar;
    private int _offset;

    public GlassScrollPanel()
    {
        // The bar must exist before the first Controls.Add: adding a control runs OnLayout
        _bar = new ThinScrollBar(this) { Anchor = AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom };
        Content.Location = Point.Empty;
        Controls.Add(Content);
        Controls.Add(_bar);
        _bar.BringToFront();
    }

    public int Offset => _offset;
    public int MaxOffset => Math.Max(0, Content.Height - ClientSize.Height);

    /// <summary>Sizes the content to its controls (plus bottom breathing room) and refreshes the scrollbar.</summary>
    public void UpdateContentHeight(int bottomPadding)
    {
        int bottom = 0;
        foreach (Control c in Content.Controls)
            bottom = Math.Max(bottom, c.Bottom);
        Content.Size = new Size(ClientSize.Width, bottom + bottomPadding);
        ScrollTo(_offset);
    }

    public void ScrollTo(int offset)
    {
        offset = Math.Clamp(offset, 0, MaxOffset);
        _offset = offset;
        Content.Top = -offset;
        _bar.Visible = MaxOffset > 0;
        _bar.Invalidate();
        Content.Invalidate(true); // the glass behind moved relative to the content
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        float s = DeviceDpi / 96f;
        int w = (int)(10 * s);
        _bar.SetBounds(ClientSize.Width - w - (int)(4 * s), (int)(6 * s), w, ClientSize.Height - (int)(12 * s));
        if (Content.Width != ClientSize.Width)
            Content.Width = ClientSize.Width;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        float s = DeviceDpi / 96f;
        ScrollTo(_offset - Math.Sign(e.Delta) * (int)(56 * s));
        base.OnMouseWheel(e);
    }

    /// <summary>The scrollbar: a rounded translucent thumb that darkens on hover and can be dragged.</summary>
    private sealed class ThinScrollBar : Control
    {
        private readonly GlassScrollPanel _owner;
        private bool _hover, _dragging;
        private int _dragStartY, _dragStartOffset;

        public ThinScrollBar(GlassScrollPanel owner)
        {
            _owner = owner;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Visible = false;
        }

        private RectangleF Thumb()
        {
            float track = Height;
            float view = _owner.ClientSize.Height, total = Math.Max(1, _owner.Content.Height);
            float h = Math.Max(track * view / total, 28 * DeviceDpi / 96f);
            float y = _owner.MaxOffset == 0 ? 0 : (track - h) * _owner.Offset / _owner.MaxOffset;
            return new RectangleF(0, y, Width, h);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            var thumb = Thumb();
            if (thumb.Contains(e.Location))
            {
                _dragging = true;
                _dragStartY = e.Y;
                _dragStartOffset = _owner.Offset;
                Capture = true;
            }
            else
            {
                // Click on the track: page up / down
                int page = _owner.ClientSize.Height - (int)(40 * DeviceDpi / 96f);
                _owner.ScrollTo(_owner.Offset + (e.Y < thumb.Y ? -page : page));
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging)
            {
                var thumb = Thumb();
                float free = Math.Max(1, Height - thumb.Height);
                _owner.ScrollTo(_dragStartOffset + (int)((e.Y - _dragStartY) * _owner.MaxOffset / free));
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _dragging = false;
            Capture = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            _owner.OnMouseWheel(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Glass.PaintBehind(g, this);
            var thumb = Thumb();
            float w = _hover || _dragging ? Width * 0.7f : Width * 0.45f;
            var r = new RectangleF((Width - w) / 2f, thumb.Y + 1, w, thumb.Height - 2);
            using var path = Theme.RoundedRect(r, w / 2f);
            using var brush = new SolidBrush(Color.FromArgb(_hover || _dragging ? 110 : 70, 15, 23, 42));
            g.FillPath(brush, path);
        }
    }
}