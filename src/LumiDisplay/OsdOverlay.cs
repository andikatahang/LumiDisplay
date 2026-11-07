using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LumiDisplay;

/// <summary>
/// On-screen display for brightness, contrast, volume and mute.
///
/// A frosted-glass slider in the macOS style: a 12px-rounded glass card made from the
/// screen behind it (captured and blurred when the OSD appears), a small icon on the left, a
/// large one on the right, and a pill track whose blue fill ends in a round knob. No numbers:
/// the bar is the value.
///
/// Rendered off-screen and pushed to a per-pixel-alpha layered window (UpdateLayeredWindow),
/// so the antialiased corners and soft shadow blend with whatever is behind them.
/// Auto-dismisses after ~1.5 s with a short fade.
/// </summary>
public sealed class OsdOverlay : IDisposable
{
    public enum OsdType
    {
        Brightness,
        Volume,
        Mute,
        Contrast,
    }

    private readonly OsdForm _form;
    private readonly System.Windows.Forms.Timer _hideTimer;
    private bool _disposed;

    /// <summary>Number of steps on a 0–100% scale for a step size (used by the settings preview).</summary>
    public static int SegmentsForStep(int stepPercent) =>
        Math.Clamp((int)Math.Round(100.0 / Math.Max(1, stepPercent)), 5, 25);

    public OsdOverlay()
    {
        _form = new OsdForm();
        _hideTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            _form.FadeOut();
        };
    }

    /// <summary>
    /// Show the OSD with a value bar (brightness, volume, contrast).
    /// </summary>
    public void Show(OsdType type, double fraction)
    {
        fraction = Math.Clamp(fraction, 0.0, 1.0);

        _hideTimer.Stop();
        _form.SetContent(type, fraction, false);
        _form.ShowOsd();
        _hideTimer.Start();
    }

    /// <summary>
    /// Show the OSD for mute state.
    /// </summary>
    public void ShowMute(bool muted)
    {
        _hideTimer.Stop();
        _form.SetContent(OsdType.Mute, muted ? 0.0 : 1.0, muted);
        _form.ShowOsd();
        _hideTimer.Start();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _hideTimer.Stop();
            _hideTimer.Dispose();
            _form.Close();
            _form.Dispose();
        }
    }

    /// <summary>
    /// The actual OSD window: a layered window whose pixels come from an off-screen bitmap.
    /// </summary>
    private sealed class OsdForm : Form
    {
        private OsdType _type;
        private double _fraction;
        private bool _muted;
        private byte _opacity = 255;
        private Bitmap? _frame;
        private Bitmap? _backdrop;
        private int _washAlpha = Glass.OsdWashAlpha;
        private readonly System.Windows.Forms.Timer _fadeTimer;

        // Layout in 96-DPI px (scaled at runtime)
        private const float CardWidth = 360;
        private const float CardHeight = 56;
        private const float ShadowMargin = 18;        // room for the drop shadow around the card
        private const float TrackHeight = 28;
        private const float TrackInsetLeft = 44;
        private const float TrackInsetRight = 88;    // large icon + the value label

        // Dark gray glass, gray fill, light glass knob, light gray icons
        private static readonly Color FillTop = Color.FromArgb(169, 170, 175);    // #A9AAAF
        private static readonly Color FillBottom = Color.FromArgb(139, 140, 146); // #8B8C92
        private static readonly Color Knob = Color.FromArgb(140, 230, 231, 234);  // #E6E7EA, translucent
        private static readonly Color IconColor = Color.FromArgb(201, 202, 206);  // #C9CACE
        private static readonly Color MutedFill = Color.FromArgb(94, 95, 100);    // #5E5F64

        public OsdForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Visible = false;

            float s = DeviceDpi / 96.0f;
            Size = new Size((int)Math.Ceiling((CardWidth + ShadowMargin * 2) * s), (int)Math.Ceiling((CardHeight + ShadowMargin * 2) * s));

            _fadeTimer = new System.Windows.Forms.Timer { Interval = 16 }; // ~60fps
            _fadeTimer.Tick += FadeTimerTick;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_LAYERED
                            | NativeMethods.WS_EX_TOOLWINDOW
                            | NativeMethods.WS_EX_TOPMOST
                            | NativeMethods.WS_EX_NOACTIVATE
                            | NativeMethods.WS_EX_TRANSPARENT;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation => true;

        public void SetContent(OsdType type, double fraction, bool muted)
        {
            _type = type;
            _fraction = fraction;
            _muted = muted;
        }

        public void ShowOsd()
        {
            _fadeTimer.Stop();
            _opacity = 255;

            // Bottom-center of the primary screen (the shadow margin hangs outside the card)
            float s = DeviceDpi / 96.0f;
            var screen = Screen.PrimaryScreen!.WorkingArea;
            Location = new Point(
                screen.Left + (screen.Width - Width) / 2,
                screen.Bottom - Height + (int)(ShadowMargin * s) - (int)(64 * s));

            if (!IsHandleCreated)
                CreateHandle();

            // The glass shows what is behind it: capture that while the OSD is hidden, then keep
            // it for repeated key presses (a capture now would include the OSD itself)
            if (!Visible || _backdrop == null)
                CaptureBackdrop();

            RenderFrame();
            PushFrame();

            if (!Visible)
                Show();
        }

        public void FadeOut()
        {
            _fadeTimer.Start();
        }

        private void FadeTimerTick(object? sender, EventArgs e)
        {
            if (_opacity <= 25)
            {
                _fadeTimer.Stop();
                Hide();
                _opacity = 255;
                return;
            }
            _opacity -= 25;
            PushFrame();
        }

        // --- Backdrop (frosted glass) ---

        private void CaptureBackdrop()
        {
            _backdrop?.Dispose();
            _backdrop = null;
            try
            {
                using var shot = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(shot))
                    g.CopyFromScreen(Location, Point.Empty, Size);
                _backdrop = Blur(shot, DeviceDpi / 96.0f);
                _washAlpha = Glass.AdaptiveWashAlpha(_backdrop, Glass.OsdWashAlpha, Glass.OsdWashAlphaBright);
            }
            catch
            {
                // Secure desktop, locked screen…: plain glass tint is drawn instead
            }
        }

        /// <summary>Strong, smooth blur: downscale ~12× in two passes, then scale back up bicubically.</summary>
        private static Bitmap Blur(Bitmap source, float scale)
        {
            int w = source.Width, h = source.Height;
            int factor = Math.Max(6, (int)Math.Round(12 * scale));
            using var mid = ResizeBitmap(source, Math.Max(1, w / 3), Math.Max(1, h / 3), InterpolationMode.HighQualityBilinear);
            using var small = ResizeBitmap(mid, Math.Max(1, w / factor), Math.Max(1, h / factor), InterpolationMode.HighQualityBilinear);
            return ResizeBitmap(small, w, h, InterpolationMode.HighQualityBicubic);
        }

        private static Bitmap ResizeBitmap(Bitmap src, int w, int h, InterpolationMode mode)
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

        // --- Rendering ---

        private void RenderFrame()
        {
            _frame?.Dispose();
            _frame = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(_frame);
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            Draw(g);
        }

        private void Draw(Graphics g)
        {
            float s = DeviceDpi / 96.0f;
            float m = ShadowMargin * s;
            var card = new RectangleF(m, m, CardWidth * s, CardHeight * s);
            float radius = card.Height / 2f; // pill

            // Soft drop shadow, offset downward
            for (int i = (int)m; i >= 1; i--)
            {
                var r = RectangleF.Inflate(card, i * 0.8f, i * 0.8f);
                r.Offset(0, 4 * s);
                using var sp = Theme.RoundedRect(r, radius + i * 0.8f);
                using var sb = new SolidBrush(Theme.Shadow(Math.Max(1, 60 / (int)m)));
                g.FillPath(sb, sp);
            }

            using var cardPath = Theme.RoundedRect(card, radius);

            // Frosted glass: the blurred screen behind, washed with dark gray
            if (_backdrop != null)
            {
                using var glass = new TextureBrush(_backdrop, WrapMode.Clamp);
                g.FillPath(glass, cardPath);
                using var wash = new SolidBrush(Color.FromArgb(_washAlpha, 44, 45, 48));
                g.FillPath(wash, cardPath);
            }
            else
            {
                using var plain = new SolidBrush(Color.FromArgb(240, 44, 45, 48));
                g.FillPath(plain, cardPath);
            }

            // Edges: a soft inner rim (lit from above) and a dark outer line so it reads on light backgrounds
            using (var outer = new Pen(Theme.Shadow(60), 1f))
            using (var outerPath = Theme.RoundedRect(RectangleF.Inflate(card, 0.5f, 0.5f), radius + 0.5f))
                g.DrawPath(outer, outerPath);
            using (var rimBrush = new LinearGradientBrush(card, Theme.Overlay(56), Theme.Overlay(12), LinearGradientMode.Vertical))
            using (var rim = new Pen(rimBrush, Math.Max(1f, s)))
            using (var rimPath = Theme.RoundedRect(RectangleF.Inflate(card, -0.5f * s, -0.5f * s), radius - 0.5f * s))
                g.DrawPath(rim, rimPath);

            // Icons: small on the left, large on the right
            float cy = card.Y + card.Height / 2f;
            var leftIcon = new RectangleF(card.X + 15 * s, cy - 8 * s, 16 * s, 16 * s);
            var rightIcon = new RectangleF(card.Right - 56 * s - 22 * s, cy - 11 * s, 22 * s, 22 * s);
            using (var iconBrush = new SolidBrush(IconColor))
            using (var iconPen = new Pen(IconColor, 1.8f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                switch (_type)
                {
                    case OsdType.Brightness:
                        DrawSun(g, leftIcon, small: true, iconBrush, iconPen);
                        DrawSun(g, rightIcon, small: false, iconBrush, iconPen);
                        break;
                    case OsdType.Contrast:
                        DrawContrast(g, leftIcon, iconBrush, iconPen);
                        DrawContrast(g, rightIcon, iconBrush, iconPen);
                        break;
                    default:
                        DrawSpeaker(g, leftIcon, waves: 0, muted: _muted, iconBrush, iconPen);
                        DrawSpeaker(g, rightIcon, waves: 2, muted: false, iconBrush, iconPen);
                        break;
                }
            }

            // Track
            float th = TrackHeight * s;
            var track = new RectangleF(card.X + TrackInsetLeft * s, cy - th / 2f, card.Width - (TrackInsetLeft + TrackInsetRight) * s, th);
            using (var trackPath = Theme.RoundedRect(track, th / 2f))
            {
                using var trackBrush = new SolidBrush(Theme.Overlay(26));
                g.FillPath(trackBrush, trackPath);
            }

            // Fill + knob: the knob sits at the end of the fill and never leaves the track
            double frac = _muted ? 0 : Math.Clamp(_fraction, 0, 1);
            float knobX = track.X + th / 2f + (float)frac * (track.Width - th);
            if (frac > 0)
            {
                var fill = new RectangleF(track.X, track.Y, knobX + th / 2f - track.X, th);
                using var fillPath = Theme.RoundedRect(fill, th / 2f);
                using var fillBrush = _muted
                    ? (Brush)new SolidBrush(MutedFill)
                    : new LinearGradientBrush(fill, FillTop, FillBottom, LinearGradientMode.Vertical);
                g.FillPath(fillBrush, fillPath);
            }

            var knob = new RectangleF(knobX - th / 2f, track.Y, th, th);
            using (var knobShadow = new SolidBrush(Theme.Shadow(80)))
                g.FillEllipse(knobShadow, knob.X, knob.Y + 1.5f * s, knob.Width, knob.Height);
            using (var knobBrush = new SolidBrush(_muted ? Color.FromArgb(140, 120, 121, 126) : Knob))
                g.FillEllipse(knobBrush, knob);
            using (var knobPath = new GraphicsPath())
            {
                knobPath.AddEllipse(knob);
                using var highlight = new PathGradientBrush(knobPath)
                {
                    CenterPoint = new PointF(knob.X + knob.Width * 0.38f, knob.Y + knob.Height * 0.3f),
                    CenterColor = Color.FromArgb(90, 255, 255, 255),
                    SurroundColors = new[] { Color.FromArgb(0, 255, 255, 255) }
                };
                g.FillEllipse(highlight, knob);
            }
            using (var knobRim = new Pen(Theme.Overlay(140), Math.Max(1f, s)))
                g.DrawEllipse(knobRim, RectangleF.Inflate(knob, -0.5f * s, -0.5f * s));

            // Value, right-aligned at the end of the pill (mute carries no level, so no number)
            if (_type == OsdType.Mute) return;
            using var valueFont = Theme.SemiboldFont(10f);
            using var valueBrush = new SolidBrush(_muted ? Theme.TextMuted : Theme.Text);
            using var valueFormat = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.DrawString(Math.Round(Math.Clamp(_fraction, 0, 1) * 100).ToString("0"), valueFont, valueBrush,
                new RectangleF(card.Right - 52 * s, card.Y, 36 * s, card.Height), valueFormat);
        }

        // --- Icons (filled, in the style of the reference) ---

        private static void DrawSpeaker(Graphics g, RectangleF r, int waves, bool muted, Brush brush, Pen pen)
        {
            float x = r.X, y = r.Y, w = r.Width, h = r.Height;
            var body = new[]
            {
                new PointF(x + 0.06f * w, y + 0.36f * h), new PointF(x + 0.26f * w, y + 0.36f * h),
                new PointF(x + 0.52f * w, y + 0.12f * h), new PointF(x + 0.52f * w, y + 0.88f * h),
                new PointF(x + 0.26f * w, y + 0.64f * h), new PointF(x + 0.06f * w, y + 0.64f * h)
            };
            g.FillPolygon(brush, body);
            using (var soften = new Pen(((SolidBrush)brush).Color, 0.06f * w) { LineJoin = LineJoin.Round })
                g.DrawPolygon(soften, body);

            using var wavePen = new Pen(pen.Color, Math.Max(1.2f, 0.09f * w)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            if (muted)
            {
                float cx = x + 0.8f * w, cy = y + 0.5f * h, d = 0.13f * w;
                g.DrawLine(wavePen, cx - d, cy - d, cx + d, cy + d);
                g.DrawLine(wavePen, cx + d, cy - d, cx - d, cy + d);
                return;
            }
            for (int i = 1; i <= waves; i++)
            {
                float rad = (0.12f + 0.17f * i) * w;
                g.DrawArc(wavePen, x + 0.52f * w - rad, y + 0.5f * h - rad, rad * 2, rad * 2, -48, 96);
            }
        }

        private static void DrawSun(Graphics g, RectangleF r, bool small, Brush brush, Pen pen)
        {
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, w = r.Width;
            float core = (small ? 0.17f : 0.2f) * w;
            g.FillEllipse(brush, cx - core, cy - core, core * 2, core * 2);
            float r1 = core + (small ? 0.09f : 0.1f) * w;
            float r2 = core + (small ? 0.2f : 0.27f) * w;
            using var rayPen = new Pen(pen.Color, Math.Max(1.2f, 0.09f * w)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4;
                g.DrawLine(rayPen,
                    cx + (float)(r1 * Math.Cos(a)), cy + (float)(r1 * Math.Sin(a)),
                    cx + (float)(r2 * Math.Cos(a)), cy + (float)(r2 * Math.Sin(a)));
            }
        }

        private static void DrawContrast(Graphics g, RectangleF r, Brush brush, Pen pen)
        {
            float d = r.Width * 0.78f;
            var c = new RectangleF(r.X + (r.Width - d) / 2f, r.Y + (r.Height - d) / 2f, d, d);
            using var ring = new Pen(pen.Color, Math.Max(1.2f, 0.09f * r.Width));
            g.FillPie(brush, c.X, c.Y, c.Width, c.Height, 90, 180);
            g.DrawEllipse(ring, c);
        }

        // --- Layered window plumbing ---

        private void PushFrame()
        {
            if (_frame == null || !IsHandleCreated) return;

            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr hBitmap = _frame.GetHbitmap(Color.FromArgb(0));
            IntPtr oldBitmap = SelectObject(memDc, hBitmap);
            try
            {
                var size = new SIZE { cx = _frame.Width, cy = _frame.Height };
                var source = new POINT { x = 0, y = 0 };
                var dest = new POINT { x = Left, y = Top };
                var blend = new BLENDFUNCTION
                {
                    BlendOp = AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = _opacity,
                    AlphaFormat = AC_SRC_ALPHA
                };
                UpdateLayeredWindow(Handle, screenDc, ref dest, ref size, memDc, ref source, 0, ref blend, ULW_ALPHA);
            }
            finally
            {
                SelectObject(memDc, oldBitmap);
                DeleteObject(hBitmap);
                DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _fadeTimer.Dispose();
                _frame?.Dispose();
                _backdrop?.Dispose();
            }
            base.Dispose(disposing);
        }

        private const byte AC_SRC_OVER = 0x00;
        private const byte AC_SRC_ALPHA = 0x01;
        private const int ULW_ALPHA = 0x02;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int cx; public int cy; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
            IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}
