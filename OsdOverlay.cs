using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
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
///
/// LumiDisplay renders it as a white card with a soft drop shadow and dark blue chiclets,
/// pushed to a per-pixel-alpha layered window (UpdateLayeredWindow) so the rounded
/// corners and shadow blend cleanly with whatever is behind them.
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
        private readonly System.Windows.Forms.Timer _fadeTimer;

        // Layout constants in 96-DPI units (scaled at runtime)
        private const int BASE_CARD_WIDTH = 320;
        private const int BASE_CARD_HEIGHT = 68;
        private const int BASE_SHADOW = 16;
        private const int BASE_PADDING = 14;
        private const int BASE_ICON_SIZE = 40;
        private const int BASE_CHICLET_HEIGHT = 8;
        private const int BASE_CHICLET_GAP = 3;
        private const int BASE_CORNER_RADIUS = 18;

        public OsdForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Visible = false;

            float s = DeviceDpi / 96.0f;
            Size = new Size((int)((BASE_CARD_WIDTH + BASE_SHADOW * 2) * s), (int)((BASE_CARD_HEIGHT + BASE_SHADOW * 2) * s));

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

            // Position at bottom-center of the primary screen (the shadow margin hangs outside the card)
            float s = DeviceDpi / 96.0f;
            int shadow = (int)(BASE_SHADOW * s);
            var screen = Screen.PrimaryScreen!.WorkingArea;
            Location = new Point(
                screen.Left + (screen.Width - Width) / 2,
                screen.Bottom - Height + shadow - (int)(60 * s));

            if (!IsHandleCreated)
                CreateHandle();

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
            if (_opacity <= 17)
            {
                _fadeTimer.Stop();
                Hide();
                _opacity = 255;
                return;
            }
            _opacity -= 17;
            PushFrame();
        }

        private void RenderFrame()
        {
            _frame?.Dispose();
            _frame = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(_frame);
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            Draw(g);
        }

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

        private string Caption => _type switch
        {
            OsdType.Brightness => "Brightness",
            OsdType.Contrast => "Contrast",
            _ => "Volume"
        };

        private Glyph IconGlyph => _type switch
        {
            OsdType.Brightness => Glyph.Sun,
            OsdType.Contrast => Glyph.Contrast,
            _ => _muted ? Glyph.SpeakerMuted : Glyph.Speaker
        };

        private void Draw(Graphics g)
        {
            float s = DeviceDpi / 96.0f;
            float shadow = BASE_SHADOW * s;
            float pad = BASE_PADDING * s;
            float iconSize = BASE_ICON_SIZE * s;
            float chicletH = BASE_CHICLET_HEIGHT * s;
            float chicletGap = BASE_CHICLET_GAP * s;
            float cornerR = BASE_CORNER_RADIUS * s;

            var card = new RectangleF(shadow, shadow, BASE_CARD_WIDTH * s, BASE_CARD_HEIGHT * s);

            // Soft drop shadow: stacked translucent rounded rects, offset slightly downward
            for (int i = (int)shadow; i >= 1; i -= Math.Max(1, (int)s))
            {
                var r = RectangleF.Inflate(card, i, i);
                r.Offset(0, 4 * s);
                using var sp = Theme.RoundedRect(r, cornerR + i);
                using var sb = new SolidBrush(Color.FromArgb(4, 15, 23, 42));
                g.FillPath(sb, sp);
            }

            // White card with hairline border
            using (var cardPath = Theme.RoundedRect(card, cornerR))
            {
                using var fill = new SolidBrush(Color.FromArgb(250, 255, 255, 255));
                g.FillPath(fill, cardPath);
                using var border = new Pen(Theme.Border, 1f);
                g.DrawPath(border, cardPath);
            }

            // Icon in a tinted rounded square
            var iconRect = new RectangleF(card.X + pad, card.Y + (card.Height - iconSize) / 2, iconSize, iconSize);
            using (var iconPath = Theme.RoundedRect(iconRect, 11 * s))
            using (var iconBg = new SolidBrush(_muted ? Color.FromArgb(254, 226, 226) : Theme.AccentSoft))
                g.FillPath(iconBg, iconPath);
            float glyphSize = iconSize * 0.58f;
            Glyphs.Draw(g, IconGlyph,
                new RectangleF(iconRect.X + (iconSize - glyphSize) / 2, iconRect.Y + (iconSize - glyphSize) / 2, glyphSize, glyphSize),
                _muted ? Theme.Danger : Theme.Accent, 2f);

            // Caption and percentage
            float textX = iconRect.Right + pad;
            float rightX = card.Right - pad - 2 * s;
            using var captionFont = new Font(Theme.FontFamilySemibold, 9.5f * s, GraphicsUnit.Point);
            using var valueFont = new Font(Theme.FontFamilySemibold, 10.5f * s, GraphicsUnit.Point);
            float textTop = card.Y + 13 * s;
            using (var captionBrush = new SolidBrush(Theme.Text))
                g.DrawString(Caption, captionFont, captionBrush, textX, textTop);

            int pct = (int)Math.Round(_fraction * 100);
            string valueText = _muted ? "Muted" : $"{pct}%";
            using (var valueBrush = new SolidBrush(_muted ? Theme.Danger : Theme.Accent))
            using (var sf = new StringFormat { Alignment = StringAlignment.Far })
                g.DrawString(valueText, valueFont, valueBrush, new RectangleF(textX, textTop - 1 * s, rightX - textX, 30 * s), sf);

            // Chiclet bar
            float barY = card.Bottom - 17 * s - chicletH;
            float barW = rightX - textX;
            float chicletW = (barW - chicletGap * (CHICLET_COUNT - 1)) / CHICLET_COUNT;

            int filledChiclets = (int)Math.Round(_fraction * CHICLET_COUNT);
            if (_muted) filledChiclets = 0;

            for (int i = 0; i < CHICLET_COUNT; i++)
            {
                var chicletRect = new RectangleF(textX + i * (chicletW + chicletGap), barY, chicletW, chicletH);
                using var chicletBrush = new SolidBrush(i < filledChiclets ? Theme.Accent : Theme.Border);
                using var chicletPath = Theme.RoundedRect(chicletRect, 2.5f * s);
                g.FillPath(chicletBrush, chicletPath);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _fadeTimer.Dispose();
                _frame?.Dispose();
            }
            base.Dispose(disposing);
        }

        // --- Layered window interop ---

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
