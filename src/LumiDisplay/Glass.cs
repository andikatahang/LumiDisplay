using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
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
/// "Liquid glass" for the settings window, over the app's own soft gradient background image
/// (Assets/background.jpg).
///
/// Two layers are pre-rendered at the window's client size, so every control copies exactly the
/// pixels behind it (no seams between controls, no per-paint blurring):
///   Base    — the window background (the image, slightly blurred and lightly washed so it shows between the panels)
///   Surface — the image blurred and washed dark enough for text, plus the faint white wash of a
///             glass panel (cards, sidebar)
/// Rebuilt when the window is shown, resized or its DPI changes.
/// </summary>
internal sealed class GlassBackdrop : IDisposable
{
    public Bitmap? Base { get; private set; }
    public Bitmap? Surface { get; private set; }

    /// <summary>Average color along the top edge of the glass, for a title bar that blends in.</summary>
    public Color TopColor { get; private set; } = Theme.Window;

    public void Rebuild(Form form)
    {
        if (!form.IsHandleCreated || form.ClientSize.Width <= 0 || form.ClientSize.Height <= 0) return;

        var size = form.ClientSize;
        using var image = Glass.CoverImage(size);

        var baseLayer = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(baseLayer))
        {
            if (image != null)
            {
                using var soft = Glass.Blur(image, 3); // just a touch softer than the image itself
                g.DrawImageUnscaled(soft, 0, 0);
            }
            else
            {
                // Image missing: a calm graphite field
                using var grad = new LinearGradientBrush(new Rectangle(Point.Empty, size),
                    Color.FromArgb(58, 60, 64), Color.FromArgb(42, 43, 46), 35f);
                g.FillRectangle(grad, 0, 0, size.Width, size.Height);
            }
            using var wash = new SolidBrush(Color.FromArgb(Glass.BackgroundWashAlpha, Glass.WindowWash));
            g.FillRectangle(wash, 0, 0, size.Width, size.Height);
        }

        float scale = form.DeviceDpi / 96f;
        var surfaceLayer = image != null
            ? Glass.Blur(image, Math.Max(3, (int)Math.Round(4 * scale)))
            : new Bitmap(baseLayer);
        using (var g = Graphics.FromImage(surfaceLayer))
        {
            using (var wash = new SolidBrush(Color.FromArgb(Glass.SafeWashAlpha(surfaceLayer), Glass.WindowWash)))
                g.FillRectangle(wash, 0, 0, size.Width, size.Height);
            using (var wash = new SolidBrush(Glass.SurfaceWash))
                g.FillRectangle(wash, 0, 0, size.Width, size.Height);
        }

        TopColor = AverageTop(surfaceLayer);
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
    /// <summary>Dark gray wash over the background image: keeps the glass dark and calm.</summary>
    public static readonly Color WindowWash = Color.FromArgb(200, 40, 41, 44);

    /// <summary>Wash over the background image between the glass panels (no text sits there).</summary>
    public const int BackgroundWashAlpha = 40;

    /// <summary>Lightest wash a glass panel gets, even over a dark image.</summary>
    public const int WindowWashAlpha = 200;

    /// <summary>OSD wash alphas; bright backgrounds get the denser one so text stays readable.</summary>
    public const int OsdWashAlpha = 150, OsdWashAlphaBright = 225;

    /// <summary>
    /// Wash alpha for a background: <paramref name="normal"/> for mid/dark backgrounds, rising to
    /// <paramref name="bright"/> as the mean luminance of <paramref name="bmp"/> approaches white.
    /// </summary>
    public static int AdaptiveWashAlpha(Bitmap bmp, int normal, int bright)
    {
        double sum = 0;
        int n = 0;
        for (int y = 0; y < bmp.Height; y += 8)
            for (int x = 0; x < bmp.Width; x += 8)
            {
                var c = bmp.GetPixel(x, y);
                sum += (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
                n++;
            }
        double t = n == 0 ? 0 : Math.Clamp((sum / n - 0.4) / 0.45, 0, 1);
        return (int)Math.Round(normal + (bright - normal) * t);
    }

    /// <summary>
    /// Smallest wash alpha (at least <see cref="WindowWashAlpha"/>) that keeps muted text readable
    /// (4.5:1) on a glass panel over the brightest pixel of <paramref name="bmp"/>.
    /// </summary>
    public static int SafeWashAlpha(Bitmap bmp)
    {
        int max = 0;
        for (int y = 0; y < bmp.Height; y += 2)
            for (int x = 0; x < bmp.Width; x += 2)
            {
                var c = bmp.GetPixel(x, y);
                max = Math.Max(max, Math.Max(c.R, Math.Max(c.G, c.B)));
            }
        return SafeWashAlpha(Color.FromArgb(max, max, max));
    }

    public static int SafeWashAlpha(Color brightest)
    {
        for (int a = WindowWashAlpha; a < 255; a++)
            if (Theme.ContrastRatio(Theme.TextMuted, PanelOver(brightest, a)) >= 4.6) return a;
        return 255;
    }

    /// <summary>What a glass panel looks like over <paramref name="under"/> with a wash of <paramref name="alpha"/>.</summary>
    public static Color PanelOver(Color under, int alpha)
    {
        var washed = Theme.Blend(Color.FromArgb(255, under), Color.FromArgb(255, WindowWash), alpha / 255f);
        return Theme.Blend(washed, Color.FromArgb(255, SurfaceWash), SurfaceWash.A / 255f);
    }

    /// <summary>Extra wash of a glass panel (card, sidebar) over the window background.</summary>
    public static readonly Color SurfaceWash = Color.FromArgb(13, 255, 255, 255);

    /// <summary>Bright rim along the top edge of glass, as if lit from above.</summary>
    public static readonly Color RimTop = Color.FromArgb(22, 255, 255, 255);
    public static readonly Color RimBottom = Color.FromArgb(6, 255, 255, 255);

    /// <summary>Faint light outline so glass edges read on dark glass.</summary>
    public static readonly Color Hairline = Color.FromArgb(23, 255, 255, 255);

    /// <summary>Divider between rows inside a card.</summary>
    public static readonly Color Divider = Color.FromArgb(15, 255, 255, 255);

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

    // --- Background image ---

    private static Bitmap? _background;

    /// <summary>The background image (embedded gradient), loaded once. Null if it can't be read.</summary>
    private static Bitmap? Background()
    {
        if (_background != null) return _background;
        try
        {
            using var stream = typeof(Glass).Assembly.GetManifestResourceStream("LumiDisplay.Assets.background.jpg");
            if (stream != null) _background = new Bitmap(stream);
        }
        catch
        {
            _background = null;
        }
        return _background;
    }

    /// <summary>The background image scaled to cover <paramref name="size"/>, centered (caller disposes).</summary>
    public static Bitmap? CoverImage(Size size)
    {
        var image = Background();
        if (image == null) return null;
        float scale = Math.Max(size.Width / (float)image.Width, size.Height / (float)image.Height);
        float srcW = size.Width / scale, srcH = size.Height / scale;
        var bmp = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        using var attrs = new ImageAttributes();
        attrs.SetWrapMode(WrapMode.TileFlipXY);
        g.DrawImage(image, new Rectangle(Point.Empty, size),
            (image.Width - srcW) / 2f, (image.Height - srcH) / 2f, srcW, srcH, GraphicsUnit.Pixel, attrs);
        return bmp;
    }

    /// <summary>Strong, smooth blur: downscale by <paramref name="factor"/> in two passes, then scale back up bicubically.</summary>
    public static Bitmap Blur(Bitmap source, int factor)
    {
        int w = source.Width, h = source.Height;
        using var mid = Resize(source, Math.Max(1, w / 3), Math.Max(1, h / 3), InterpolationMode.HighQualityBilinear);
        using var small = Resize(mid, Math.Max(1, w / factor), Math.Max(1, h / factor), InterpolationMode.HighQualityBilinear);
        return Resize(small, w, h, InterpolationMode.HighQualityBicubic);
    }

    private static Bitmap Resize(Bitmap src, int w, int h, InterpolationMode mode)
    {
        var dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(dst);
        g.InterpolationMode = mode;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingMode = CompositingMode.SourceCopy;
        using var attrs = new ImageAttributes();
        attrs.SetWrapMode(WrapMode.TileFlipXY); // no dark fringe at the edges
        g.DrawImage(src, new Rectangle(0, 0, w, h), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attrs);
        return dst;
    }
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
            using var brush = new SolidBrush(Theme.Overlay(_hover || _dragging ? 90 : 50));
            g.FillPath(brush, path);
        }
    }
}