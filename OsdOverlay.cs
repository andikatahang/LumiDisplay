using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LumiDisplay;

/// <summary>
/// Translucent on-screen display overlay for brightness/volume/mute feedback.
/// Mirrors BetterDisplay's GlassOSD / AppOSD with 16 chiclets display.
/// 
/// BetterDisplay's OSD system:
///   - 16 chiclets representing the full range
///   - 4 logical steps per chiclet (64 total steps)
///   - Shows icon (brightness sun / volume speaker) + chiclet bar
///   - Glass/rounded appearance with shadow
///   - Auto-dismisses after ~1.5 seconds
///   - Appears at bottom-center of screen
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

    // BetterDisplay uses 16 chiclets in its OSD
    private const int CHICLET_COUNT = 16;
    private const int TOTAL_STEPS = 64; // 16 chiclets × 4 steps each
    private const int STEPS_PER_CHICLET = 4;

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
    /// The actual OSD window rendered as a glass-style overlay.
    /// </summary>
    private sealed class OsdForm : Form
    {
        private OsdType _type;
        private double _fraction;
        private bool _muted;
        private byte _opacity = 230;
        private readonly System.Windows.Forms.Timer _fadeTimer;

        // Layout constants (scaled by DPI at runtime)
        private const int BASE_WIDTH = 280;
        private const int BASE_HEIGHT = 56;
        private const int BASE_PADDING = 12;
        private const int BASE_ICON_SIZE = 28;
        private const int BASE_CHICLET_WIDTH = 12;
        private const int BASE_CHICLET_HEIGHT = 10;
        private const int BASE_CHICLET_GAP = 2;
        private const int BASE_CORNER_RADIUS = 16;

        public OsdForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Black;
            DoubleBuffered = true;
            Visible = false;

            var dpiScale = DeviceDpi / 96.0f;
            Size = new Size((int)(BASE_WIDTH * dpiScale), (int)(BASE_HEIGHT * dpiScale));

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
            _opacity = 230;
            Opacity = _opacity / 255.0;

            // Position at bottom-center of the primary screen
            var screen = Screen.PrimaryScreen!.WorkingArea;
            Location = new Point(
                screen.Left + (screen.Width - Width) / 2,
                screen.Bottom - Height - 60);

            if (!Visible)
                Show();

            Invalidate();
        }

        public void FadeOut()
        {
            _fadeTimer.Start();
        }

        private void FadeTimerTick(object? sender, EventArgs e)
        {
            if (_opacity <= 15)
            {
                _fadeTimer.Stop();
                Hide();
                _opacity = 230;
                return;
            }
            _opacity -= 15;
            Opacity = _opacity / 255.0;
        }

        private Color GetAccentColor()
        {
            if (_muted) return Color.FromArgb(239, 68, 68); // Neon Coral
            return _type switch
            {
                OsdType.Brightness => Color.FromArgb(245, 158, 11), // Solar Gold
                OsdType.Volume     => Color.FromArgb(6, 182, 212),  // Electric Cyan
                OsdType.Contrast   => Color.FromArgb(168, 85, 247), // Radiant Violet
                OsdType.Mute       => Color.FromArgb(239, 68, 68),  // Neon Coral
                _ => Color.FromArgb(245, 158, 11)
            };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            var dpiScale = DeviceDpi / 96.0f;
            int pad = (int)(BASE_PADDING * dpiScale);
            int iconSize = (int)(BASE_ICON_SIZE * dpiScale);
            int chicletW = (int)(BASE_CHICLET_WIDTH * dpiScale);
            int chicletH = (int)(BASE_CHICLET_HEIGHT * dpiScale);
            int chicletGap = (int)(BASE_CHICLET_GAP * dpiScale);
            int cornerR = (int)(BASE_CORNER_RADIUS * dpiScale);

            var accentColor = GetAccentColor();

            // Background: ultra-sleek obsidian glass with subtle accent tint
            var bgRect = new Rectangle(0, 0, Width, Height);
            using var bgPath = RoundedRect(bgRect, cornerR);
            using var bgBrush = new SolidBrush(Color.FromArgb(220, 18, 16, 26));
            g.FillPath(bgBrush, bgPath);

            // Border glow: subtly tinted by the active accent color
            using var borderPen = new Pen(Color.FromArgb(80, accentColor.R, accentColor.G, accentColor.B), 1.2f);
            g.DrawPath(borderPen, bgPath);

            // Icon with active accent color
            int iconX = pad;
            int iconY = (Height - iconSize) / 2;
            DrawIcon(g, _type, _muted, accentColor, new Rectangle(iconX, iconY, iconSize, iconSize));

            // Chiclet bar
            int barX = iconX + iconSize + pad;
            int barY = (Height - chicletH) / 2;

            int filledChiclets = (int)Math.Round(_fraction * CHICLET_COUNT);
            if (_muted) filledChiclets = 0;

            for (int i = 0; i < CHICLET_COUNT; i++)
            {
                var chicletRect = new Rectangle(
                    barX + i * (chicletW + chicletGap), barY, chicletW, chicletH);

                using var chicletBrush = new SolidBrush(
                    i < filledChiclets
                        ? Color.FromArgb(245, accentColor.R, accentColor.G, accentColor.B)
                        : Color.FromArgb(40, 255, 255, 255));

                using var chicletPath = RoundedRect(chicletRect, 2);
                g.FillPath(chicletBrush, chicletPath);
            }

            // Percentage text
            int pct = (int)Math.Round(_fraction * 100);
            string text = _muted ? "Muted" : $"{pct}%";
            using var font = new Font("Segoe UI", 10 * dpiScale, FontStyle.Bold);
            using var textBrush = new SolidBrush(Color.FromArgb(240, 245, 250, 255));
            var textX = barX + CHICLET_COUNT * (chicletW + chicletGap) + pad / 2;
            var textRect = new RectangleF(textX, 0, Width - textX - pad, Height);
            using var sf = new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center
            };
            g.DrawString(text, font, textBrush, textRect, sf);
        }

        private static void DrawIcon(Graphics g, OsdType type, bool muted, Color accentColor, Rectangle rect)
        {
            using var pen = new Pen(accentColor, 2f);
            using var brush = new SolidBrush(accentColor);

            switch (type)
            {
                case OsdType.Brightness:
                    DrawSunIcon(g, pen, brush, rect);
                    break;
                case OsdType.Volume:
                case OsdType.Mute:
                    DrawSpeakerIcon(g, pen, brush, rect, muted);
                    break;
                case OsdType.Contrast:
                    DrawContrastIcon(g, pen, brush, rect);
                    break;
            }
        }

        private static void DrawSunIcon(Graphics g, Pen pen, Brush brush, Rectangle rect)
        {
            int cx = rect.X + rect.Width / 2;
            int cy = rect.Y + rect.Height / 2;
            int r = rect.Width / 4;
            int rayLen = rect.Width / 3;

            // Center circle
            g.FillEllipse(brush, cx - r, cy - r, r * 2, r * 2);

            // Rays
            for (int i = 0; i < 8; i++)
            {
                double angle = i * Math.PI / 4;
                int x1 = cx + (int)((r + 2) * Math.Cos(angle));
                int y1 = cy + (int)((r + 2) * Math.Sin(angle));
                int x2 = cx + (int)(rayLen * Math.Cos(angle));
                int y2 = cy + (int)(rayLen * Math.Sin(angle));
                g.DrawLine(pen, x1, y1, x2, y2);
            }
        }

        private static void DrawSpeakerIcon(Graphics g, Pen pen, Brush brush, Rectangle rect, bool muted)
        {
            int x = rect.X + 4;
            int y = rect.Y + rect.Height / 4;
            int w = rect.Width / 3;
            int h = rect.Height / 2;

            // Speaker body
            var speakerBody = new Point[]
            {
                new(x, y + h / 3),
                new(x + w / 2, y + h / 3),
                new(x + w, y),
                new(x + w, y + h),
                new(x + w / 2, y + h - h / 3),
                new(x, y + h - h / 3),
            };
            g.FillPolygon(brush, speakerBody);

            if (muted)
            {
                // X mark for mute
                int mx = x + w + 6;
                int my = rect.Y + rect.Height / 3;
                int ms = rect.Height / 4;
                using var mutePen = new Pen(Color.FromArgb(239, 68, 68), 2.5f);
                g.DrawLine(mutePen, mx, my, mx + ms, my + ms);
                g.DrawLine(mutePen, mx + ms, my, mx, my + ms);
            }
            else
            {
                // Sound waves
                int wx = x + w + 4;
                int wcy = rect.Y + rect.Height / 2;
                for (int i = 0; i < 2; i++)
                {
                    int wr = 4 + i * 5;
                    g.DrawArc(pen, wx, wcy - wr, wr * 2, wr * 2, -45, 90);
                }
            }
        }

        private static void DrawContrastIcon(Graphics g, Pen pen, Brush brush, Rectangle rect)
        {
            int cx = rect.X + rect.Width / 2;
            int cy = rect.Y + rect.Height / 2;
            int r = rect.Width / 3;

            // Half-filled circle
            g.FillPie(brush, cx - r, cy - r, r * 2, r * 2, -90, 180);
            g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
