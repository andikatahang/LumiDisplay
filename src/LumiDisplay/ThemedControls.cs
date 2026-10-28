using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LumiDisplay;

/// <summary>
/// Base for the owner-drawn light theme controls: double buffered, DPI-aware painting.
/// Layout passed in by callers is in 96-DPI units and scaled by the form's AutoScaleMode;
/// offsets used while painting are scaled with <see cref="Px"/>.
/// </summary>
internal abstract class ThemedControl : Control
{
    protected ThemedControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint
               | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.UserPaint
               | ControlStyles.ResizeRedraw, true);
    }

    protected float Scale96 => DeviceDpi / 96f;
    protected float Px(float value) => value * Scale96;
    protected int PxI(float value) => (int)Math.Round(value * Scale96);

    protected static void PrepareGraphics(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
    }
}

/// <summary>
/// iOS / Windows 11 style on-off switch with a short knob animation.
/// </summary>
internal sealed class ToggleSwitch : ThemedControl
{
    private bool _checked;
    private bool _hover;
    private float _knob;
    private readonly System.Windows.Forms.Timer _anim = new() { Interval = 15 };

    public event EventHandler? CheckedChanged;

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.Selectable, true);
        Size = new Size(44, 24);
        Cursor = Cursors.Hand;
        TabStop = true;
        _anim.Tick += (_, _) =>
        {
            float target = _checked ? 1f : 0f;
            _knob += Math.Sign(target - _knob) * 0.18f;
            if (Math.Abs(target - _knob) < 0.18f)
            {
                _knob = target;
                _anim.Stop();
            }
            Invalidate();
        };
    }

    [DefaultValue(false)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            if (Visible && IsHandleCreated) _anim.Start();
            else _knob = value ? 1f : 0f;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnClick(EventArgs e)
    {
        Focus();
        Checked = !Checked;
        base.OnClick(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space)
        {
            Checked = !Checked;
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        PrepareGraphics(g);
        Glass.PaintBehind(g, this);

        float inset = Px(2);
        var track = new RectangleF(inset, inset, Width - inset * 2 - 1, Height - inset * 2 - 1);

        if (Focused && ShowFocusCues)
        {
            using var ring = Theme.RoundedRect(RectangleF.Inflate(track, Px(1.5f), Px(1.5f)), track.Height);
            using var ringPen = new Pen(Color.FromArgb(110, Theme.Accent), Px(1.5f));
            g.DrawPath(ringPen, ring);
        }

        var off = _hover ? Theme.TextFaint : Theme.BorderStrong;
        var on = _hover ? Theme.AccentHover : Theme.Accent;
        using (var trackPath = Theme.RoundedRect(track, track.Height / 2))
        using (var trackBrush = new SolidBrush(Theme.Blend(off, on, _knob)))
            g.FillPath(trackBrush, trackPath);

        float d = track.Height - Px(5);
        float x = track.X + Px(2.5f) + _knob * (track.Width - d - Px(5));
        float y = track.Y + Px(2.5f);
        using (var shadow = new SolidBrush(Color.FromArgb(40, 15, 23, 42)))
            g.FillEllipse(shadow, x, y + Px(1), d, d);
        using (var knob = new SolidBrush(Color.White))
            g.FillEllipse(knob, x, y, d, d);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _anim.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>
/// Rounded flat button. Primary buttons are filled dark blue, secondary are white with a gray outline.
/// </summary>
internal sealed class RoundedButton : ThemedControl
{
    private bool _hover;
    private bool _pressed;

    [DefaultValue(false)]
    public bool Primary { get; set; }

    public RoundedButton()
    {
        SetStyle(ControlStyles.Selectable, true);
        Size = new Size(110, 36);
        Cursor = Cursors.Hand;
        TabStop = true;
        Font = Theme.MediumFont(9.5f);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        if (!Enabled) { _hover = false; _pressed = false; }
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        PrepareGraphics(g);
        Glass.PaintBehind(g, this);

        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        float radius = Px(Theme.Radius);
        using var path = Theme.RoundedRect(rect, radius);

        Color text;
        if (!Enabled)
        {
            // Disabled: barely-there glass, no hover/press feedback
            using var brush = new SolidBrush(Color.FromArgb(70, 255, 255, 255));
            g.FillPath(brush, path);
            using var pen = new Pen(Glass.Hairline, 1f);
            g.DrawPath(pen, path);
            text = Theme.TextFaint;
        }
        else if (Primary)
        {
            // Solid accent with a soft light falloff from the top
            var baseColor = _pressed ? Theme.AccentPressed : _hover ? Theme.AccentHover : Theme.Accent;
            using (var brush = new SolidBrush(baseColor))
                g.FillPath(brush, path);
            using (var sheen = new LinearGradientBrush(rect, Color.FromArgb(46, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical))
                g.FillPath(sheen, path);
            text = Color.White;
        }
        else
        {
            // Glass button: more opaque white the more it is engaged
            int alpha = _pressed ? 235 : _hover ? 205 : 160;
            using (var brush = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255)))
                g.FillPath(brush, path);
            Glass.DrawRim(g, rect, radius, Scale96);
            text = Theme.Text;
        }

        if (Focused && ShowFocusCues && Enabled)
        {
            using var ring = Theme.RoundedRect(RectangleF.Inflate(rect, -Px(2.5f), -Px(2.5f)), Math.Max(0, radius - Px(2.5f)));
            using var ringPen = new Pen(Primary ? Color.FromArgb(160, 255, 255, 255) : Color.FromArgb(120, Theme.Accent), Px(1.2f));
            g.DrawPath(ringPen, ring);
        }

        TextRenderer.DrawText(g, Text, Font, ClientRectangle, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }
}

/// <summary>
/// Glass card: a 12px-rounded translucent panel with a lit rim, and a title (plus optional description).
/// Child controls start at <see cref="ContentTop"/>; an optional header control sits at the top-right.
/// </summary>
internal sealed class Card : Panel, IGlassSurface
{
    public bool IsGlassSurface => true;

    private Control? _headerControl;

    public string Title { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>Y (96-DPI units) where the card's rows begin.</summary>
    public int ContentTop => string.IsNullOrEmpty(Description) ? 52 : 72;

    public Card()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint
               | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.UserPaint
               | ControlStyles.ResizeRedraw
               | ControlStyles.SupportsTransparentBackColor
               | ControlStyles.Opaque, true); // OnPaint draws everything, glass included
        BackColor = Color.Transparent;
    }

    public Control? HeaderControl
    {
        get => _headerControl;
        set
        {
            _headerControl = value;
            if (value == null) return;
            value.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            Controls.Add(value);
            PlaceHeaderControl();
        }
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        PlaceHeaderControl();
    }

    private void PlaceHeaderControl()
    {
        if (_headerControl == null) return;
        float s = DeviceDpi / 96f;
        _headerControl.Location = new Point(
            Width - _headerControl.Width - (int)(20 * s),
            (int)(16 * s) + ((int)(22 * s) - _headerControl.Height) / 2);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        float s = DeviceDpi / 96f;

        Glass.PaintBehind(g, this);
        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Glass.PaintSurface(g, this, rect, Theme.Radius * s);

        using var titleFont = Theme.MediumFont(10.5f);
        TextRenderer.DrawText(g, Title, titleFont, new Point((int)(20 * s), (int)(17 * s)), Theme.Text,
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

        if (!string.IsNullOrEmpty(Description))
        {
            using var descFont = Theme.Font(8.75f);
            TextRenderer.DrawText(g, Description, descFont, new Point((int)(20 * s), (int)(41 * s)), Theme.TextMuted,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }
    }
}

/// <summary>
/// A row inside a <see cref="Card"/>: title and description on the left, an input on the right,
/// separated from the previous row by a hairline divider.
/// </summary>
internal sealed class SettingRow : ThemedControl
{
    private readonly Control _input;
    private readonly string _title;
    private readonly string _description;

    public SettingRow(string title, string description, Control input)
    {
        _title = title;
        _description = description;
        _input = input;
        BackColor = Theme.Surface;
        Height = 60;
        Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        Controls.Add(input);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        _input.Location = new Point(Width - _input.Width - PxI(20), (Height - _input.Height) / 2);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        PrepareGraphics(g);
        Glass.PaintBehind(g, this);

        using (var divider = new Pen(Glass.Divider, 1f))
            g.DrawLine(divider, Px(20), 0, Width - Px(20), 0);

        int textWidth = Math.Max(10, _input.Left - PxI(40));
        using var titleFont = Theme.MediumFont(9.5f);
        using var descFont = Theme.Font(8.75f);

        bool hasDesc = !string.IsNullOrEmpty(_description);
        int titleY = hasDesc ? PxI(12) : (Height - titleFont.Height) / 2;
        TextRenderer.DrawText(g, _title, titleFont,
            new Rectangle(PxI(20), titleY, textWidth, titleFont.Height), Theme.Text,
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        if (hasDesc)
        {
            TextRenderer.DrawText(g, _description, descFont,
                new Rectangle(PxI(20), PxI(33), textWidth, descFont.Height + 2), Theme.TextMuted,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }
}

/// <summary>
/// Draws keyboard key caps ("Ctrl" + "Alt" + "↑") left to right.
/// </summary>
internal static class KeyCaps
{
    /// <summary>Draws the caps starting at <paramref name="x"/>, vertically centred on <paramref name="centerY"/>; returns the end X.</summary>
    public static float Draw(Graphics g, string[] keys, float x, float centerY, float scale, Color textColor, Color borderColor)
    {
        using var keyFont = Theme.MediumFont(8.25f);
        using var plusFont = Theme.Font(8.5f);
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        float capH = 22 * scale;
        float y = centerY - capH / 2f;

        for (int i = 0; i < keys.Length; i++)
        {
            if (i > 0)
            {
                // Grayscale antialiasing: ClearType tints this thin glyph
                var plusHint = g.TextRenderingHint;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using var plusBrush = new SolidBrush(Theme.TextFaint);
                using var plusFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString("+", plusFont, plusBrush, new RectangleF(x, y, 16 * scale, capH), plusFormat);
                g.TextRenderingHint = plusHint;
                x += 16 * scale;
            }

            var textSize = TextRenderer.MeasureText(g, keys[i], keyFont, Size.Empty, flags);
            float capW = Math.Max(capH, textSize.Width + 14 * scale);
            var cap = new RectangleF(x, y, capW, capH);
            using (var capPath = Theme.RoundedRect(cap, 6 * scale))
            {
                using var fill = new SolidBrush(Color.FromArgb(215, 255, 255, 255));
                g.FillPath(fill, capPath);
                using var border = new Pen(borderColor, 1f);
                g.DrawPath(border, capPath);
            }
            // Slightly darker bottom edge gives the cap a little depth
            using (var edge = new Pen(Color.FromArgb(Math.Min(255, borderColor.A + 20), borderColor), 1.2f * scale))
                g.DrawLine(edge, cap.X + 5 * scale, cap.Bottom, cap.Right - 5 * scale, cap.Bottom);

            if (keys[i] is "↑" or "↓" or "←" or "→")
            {
                // Thin arrow glyphs get ClearType colour fringes; draw them grayscale-antialiased
                var hint = g.TextRenderingHint;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using var arrowFont = new Font("Segoe UI Symbol", 9f, FontStyle.Bold);
                using var brush = new SolidBrush(textColor);
                using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(keys[i], arrowFont, brush, cap, sf);
                g.TextRenderingHint = hint;
            }
            else
            {
                TextRenderer.DrawText(g, keys[i], keyFont, Rectangle.Round(cap), textColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
            x += capW;
        }
        return x;
    }
}

/// <summary>
/// Click-to-record shortcut field. Click (or Enter/Space), then press the new combination;
/// Esc cancels, Backspace/Delete clears, the × on hover clears too.
/// </summary>
internal sealed class ShortcutRecorder : ThemedControl
{
    private uint _modifiers;
    private Keys _key = Keys.None;
    private bool _recording;
    private bool _hover;
    private bool _hoverClear;
    private string? _error;
    private readonly System.Windows.Forms.Timer _errorTimer = new() { Interval = 2600 };
    private readonly ToolTip _tip = new();

    /// <summary>Returns an error message to reject a combination (e.g. already used), or null to accept it.</summary>
    public Func<uint, Keys, string?>? Validate { get; set; }

    public event EventHandler? BindingChanged;
    public event EventHandler<bool>? RecordingChanged;

    public uint Modifiers => _modifiers;
    public Keys Key => _key;
    public bool HasBinding => _key != Keys.None;

    private bool _unavailable;

    /// <summary>Marks the shortcut as taken by another app (Windows refused to register it).</summary>
    public bool Unavailable
    {
        get => _unavailable;
        set
        {
            _unavailable = value;
            _tip.SetToolTip(this, value
                ? "Another app is already using this shortcut. Click to choose different keys."
                : "Click, then press a new key combination. Backspace clears it.");
            Invalidate();
        }
    }

    public ShortcutRecorder()
    {
        SetStyle(ControlStyles.Selectable, true);
        Size = new Size(236, 34);
        Cursor = Cursors.Hand;
        TabStop = true;
        BackColor = Theme.Surface;
        Unavailable = false;
        _errorTimer.Tick += (_, _) =>
        {
            _errorTimer.Stop();
            _error = null;
            Invalidate();
        };
    }

    public void SetBinding(uint modifiers, Keys key)
    {
        _modifiers = key == Keys.None ? 0 : modifiers;
        _key = key;
        Invalidate();
    }

    private RectangleF ClearRect => new(Width - Px(30), 0, Px(30), Height);

    private void StartRecording()
    {
        if (_recording) return;
        Focus();
        _recording = true;
        _error = null;
        RecordingChanged?.Invoke(this, true);
        Invalidate();
    }

    private void StopRecording()
    {
        if (!_recording) return;
        _recording = false;
        RecordingChanged?.Invoke(this, false);
        Invalidate();
    }

    private void ShowError(string message)
    {
        _error = message;
        _errorTimer.Stop();
        _errorTimer.Start();
        Invalidate();
    }

    private void Commit(uint modifiers, Keys key)
    {
        SetBinding(modifiers, key);
        StopRecording();
        BindingChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        if (!_recording && HasBinding && ClearRect.Contains(e.Location))
        {
            Focus();
            Commit(0, Keys.None);
            return;
        }
        if (_recording) StopRecording();
        else StartRecording();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        bool overClear = ClearRect.Contains(e.Location);
        if (overClear != _hoverClear) { _hoverClear = overClear; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _hoverClear = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }

    protected override void OnLostFocus(EventArgs e)
    {
        StopRecording();
        Invalidate();
        base.OnLostFocus(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!_recording && e.KeyCode is Keys.Enter or Keys.Space)
        {
            StartRecording();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (_recording) Invalidate(); // live modifier preview
        base.OnKeyUp(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!_recording) return base.ProcessCmdKey(ref msg, keyData);
        HandleRecordedKey(keyData);
        return true;
    }

    private void HandleRecordedKey(Keys keyData)
    {
        Keys key = keyData & Keys.KeyCode;
        Keys mods = keyData & Keys.Modifiers;

        if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin
            or Keys.LControlKey or Keys.RControlKey or Keys.LShiftKey or Keys.RShiftKey or Keys.LMenu or Keys.RMenu)
        {
            Invalidate(); // still waiting for the main key
            return;
        }

        uint modifiers = CurrentModifiers(mods);

        if (modifiers == 0 && key == Keys.Escape) { StopRecording(); return; }
        if (modifiers == 0 && key is Keys.Back or Keys.Delete) { Commit(0, Keys.None); return; }

        if (key is Keys.VolumeUp or Keys.VolumeDown or Keys.VolumeMute)
        {
            ShowError("Volume keys already work on their own");
            return;
        }

        bool isFunctionKey = key >= Keys.F1 && key <= Keys.F24;
        bool hasStrongModifier = (modifiers & (NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_WIN)) != 0;
        if (!isFunctionKey && !hasStrongModifier)
        {
            ShowError("Add Ctrl, Alt or Win to this key");
            return;
        }

        var error = Validate?.Invoke(modifiers, key);
        if (error != null)
        {
            ShowError(error);
            return;
        }

        Commit(modifiers, key);
    }

    private static uint CurrentModifiers(Keys mods)
    {
        uint m = 0;
        if ((mods & Keys.Control) != 0) m |= NativeMethods.MOD_CONTROL;
        if ((mods & Keys.Alt) != 0) m |= NativeMethods.MOD_ALT;
        if ((mods & Keys.Shift) != 0) m |= NativeMethods.MOD_SHIFT;
        if ((GetKeyState((int)Keys.LWin) & 0x8000) != 0 || (GetKeyState((int)Keys.RWin) & 0x8000) != 0)
            m |= NativeMethods.MOD_WIN;
        return m;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        PrepareGraphics(g);
        Glass.PaintBehind(g, this);

        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        bool problem = _error != null || (_unavailable && !_recording);
        Color border = _recording ? Theme.Accent
                     : problem ? Theme.Danger
                     : _hover || Focused ? Color.FromArgb(70, 15, 23, 42)
                     : Glass.Hairline;
        using (var path = Theme.RoundedRect(rect, Px(Theme.Radius)))
        {
            using var fill = new SolidBrush(_recording ? Color.FromArgb(235, 238, 243, 252) : Color.FromArgb(_hover ? 215 : 170, 255, 255, 255));
            g.FillPath(fill, path);
            using var pen = new Pen(border, _recording ? Px(1.6f) : 1f);
            g.DrawPath(pen, path);
        }

        float cy = Height / 2f;
        using var hintFont = Theme.Font(8.75f);
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine
                  | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
        var textRect = new Rectangle(PxI(10), 0, Width - PxI(20), Height);

        if (_error != null)
        {
            TextRenderer.DrawText(g, _error, hintFont, textRect, Theme.Danger, flags);
            return;
        }

        if (_recording)
        {
            // Live preview of the modifiers being held
            uint held = CurrentModifiers(ModifierKeys);
            float x = Px(8);
            if (held != 0)
                x = KeyCaps.Draw(g, HotkeyManager.DescribeKeys(held, Keys.None), x, cy, Scale96, Theme.Accent, Theme.Accent) + Px(8);
            TextRenderer.DrawText(g, held == 0 ? "Press a key combination…" : "+ key…", hintFont,
                new Rectangle((int)x, 0, Width - (int)x - PxI(8), Height), Theme.Accent, flags);
            return;
        }

        if (!HasBinding)
        {
            TextRenderer.DrawText(g, "Not set — click to record", hintFont, textRect, Theme.TextFaint, flags);
            return;
        }

        KeyCaps.Draw(g, HotkeyManager.DescribeKeys(_modifiers, _key), Px(8), cy, Scale96,
            _unavailable ? Theme.Danger : Theme.Accent, _unavailable ? Theme.Danger : Color.FromArgb(46, 15, 23, 42));

        if (_hover)
        {
            // Clear button
            var c = ClearRect;
            float r = Px(9);
            var circle = new RectangleF(c.X + (c.Width - 2 * r) / 2, cy - r, 2 * r, 2 * r);
            using (var bg = new SolidBrush(_hoverClear ? Color.FromArgb(40, 15, 23, 42) : Color.FromArgb(22, 15, 23, 42)))
                g.FillEllipse(bg, circle);
            using var xPen = new Pen(_hoverClear ? Theme.Text : Theme.TextMuted, Px(1.4f));
            float d = Px(3.5f);
            float mx = circle.X + r, my = circle.Y + r;
            g.DrawLine(xPen, mx - d, my - d, mx + d, my + d);
            g.DrawLine(xPen, mx + d, my - d, mx - d, my + d);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _errorTimer.Dispose();
            _tip.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// Horizontal slider with a dark blue fill and a round white thumb.
/// Raises <see cref="Scroll"/> only for user interaction, like <see cref="TrackBar.Scroll"/>.
/// </summary>
internal sealed class SliderBar : ThemedControl
{
    private int _value = 50;
    private bool _dragging;
    private bool _hover;

    public int Minimum { get; set; }
    public int Maximum { get; set; } = 100;

    /// <summary>Values snap to multiples of this (e.g. 10 → 0, 10, 20 … 100).</summary>
    public int Step { get; set; } = 1;

    public event EventHandler? Scroll;

    private int Snap(int v)
    {
        int step = Math.Max(1, Step);
        return Math.Clamp((int)Math.Round((double)v / step) * step, Minimum, Maximum);
    }

    public SliderBar()
    {
        SetStyle(ControlStyles.Selectable, true);
        Height = 28;
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    public int Value
    {
        get => _value;
        set
        {
            value = Math.Clamp(value, Minimum, Maximum);
            if (_value == value) return;
            _value = value;
            Invalidate();
        }
    }

    private float ThumbD => Px(18);
    private float TrackLeft => ThumbD / 2 + 1;
    private float TrackRight => Width - ThumbD / 2 - 1;

    private void SetFromX(int x)
    {
        float t = (x - TrackLeft) / Math.Max(1, TrackRight - TrackLeft);
        int v = Snap(Minimum + (int)Math.Round(Math.Clamp(t, 0f, 1f) * (Maximum - Minimum)));
        if (v != _value)
        {
            Value = v;
            Scroll?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        if (e.Button == MouseButtons.Left)
        {
            _dragging = true;
            Capture = true;
            SetFromX(e.X);
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging) SetFromX(e.X);
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragging = false;
        Capture = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        int step = Math.Max(1, Step);
        int delta = e.KeyCode switch
        {
            Keys.Left or Keys.Down => -step,
            Keys.Right or Keys.Up => step,
            Keys.PageDown => -2 * step,
            Keys.PageUp => 2 * step,
            Keys.Home => Minimum - _value,
            Keys.End => Maximum - _value,
            _ => 0
        };
        if (delta != 0)
        {
            int before = _value;
            Value = Snap(_value + delta);
            if (_value != before) Scroll?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        PrepareGraphics(g);
        Glass.PaintBehind(g, this);

        float cy = Height / 2f;
        float trackH = Px(6);
        float t = (float)(_value - Minimum) / Math.Max(1, Maximum - Minimum);
        float thumbX = TrackLeft + t * (TrackRight - TrackLeft);

        using (var track = Theme.RoundedRect(new RectangleF(TrackLeft, cy - trackH / 2, TrackRight - TrackLeft, trackH), trackH / 2))
        using (var trackBrush = new SolidBrush(Color.FromArgb(34, 15, 23, 42)))
            g.FillPath(trackBrush, track);

        if (thumbX > TrackLeft)
        {
            using var fill = Theme.RoundedRect(new RectangleF(TrackLeft, cy - trackH / 2, thumbX - TrackLeft, trackH), trackH / 2);
            using var fillBrush = new SolidBrush(_hover || _dragging ? Theme.AccentHover : Theme.Accent);
            g.FillPath(fillBrush, fill);
        }

        float d = ThumbD;
        var thumb = new RectangleF(thumbX - d / 2, cy - d / 2, d, d);
        if (Focused && ShowFocusCues)
        {
            using var halo = new SolidBrush(Color.FromArgb(45, Theme.Accent));
            g.FillEllipse(halo, RectangleF.Inflate(thumb, Px(4), Px(4)));
        }
        using (var shadow = new SolidBrush(Color.FromArgb(35, 15, 23, 42)))
            g.FillEllipse(shadow, thumb.X, thumb.Y + Px(1), d, d);
        using (var white = new SolidBrush(Color.White))
            g.FillEllipse(white, thumb);
        using (var ring = new Pen(Theme.Accent, Px(2)))
            g.DrawEllipse(ring, RectangleF.Inflate(thumb, -Px(1), -Px(1)));
    }
}

/// <summary>
/// Sidebar navigation entry with a glyph; the selected entry sits on a brighter glass pill.
/// </summary>
internal sealed class NavItem : ThemedControl
{
    private bool _selected;
    private bool _hover;

    public Glyph Glyph { get; }

    public NavItem(string text, Glyph glyph)
    {
        Text = text;
        Glyph = glyph;
        Size = new Size(200, 40);
        Cursor = Cursors.Hand;
        BackColor = Theme.Surface;
    }

    public bool Selected
    {
        get => _selected;
        set { _selected = value; Invalidate(); }
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        PrepareGraphics(g);
        Glass.PaintBehind(g, this);

        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        if (_selected)
        {
            using var path = Theme.RoundedRect(rect, Px(Theme.Radius));
            using var brush = new SolidBrush(Color.FromArgb(220, 255, 255, 255));
            g.FillPath(brush, path);
            using var hair = new Pen(Glass.Hairline, 1f);
            g.DrawPath(hair, path);
        }
        else if (_hover)
        {
            using var path = Theme.RoundedRect(rect, Px(Theme.Radius));
            using var brush = new SolidBrush(Color.FromArgb(110, 255, 255, 255));
            g.FillPath(brush, path);
        }

        var color = _selected ? Theme.Accent : Theme.TextMuted;
        float gs = Px(18);
        Glyphs.Draw(g, Glyph, new RectangleF(Px(14), (Height - gs) / 2, gs, gs), color);

        using var font = _selected ? Theme.MediumFont(9.5f) : Theme.Font(9.5f);
        TextRenderer.DrawText(g, Text, font, new Rectangle(PxI(44), 0, Width - PxI(48), Height),
            _selected ? Theme.Accent : Theme.TextSecondary,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }
}

/// <summary>
/// Small stat tile: caption on top, value below, on a soft-gray rounded background.
/// </summary>
internal sealed class StatTile : ThemedControl
{
    private string _value = "—";

    public string Caption { get; }

    public StatTile(string caption)
    {
        Caption = caption;
        Size = new Size(120, 60);
        BackColor = Theme.Surface;
    }

    public string Value
    {
        get => _value;
        set { _value = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        PrepareGraphics(g);
        Glass.PaintBehind(g, this);

        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = Theme.RoundedRect(rect, Px(Theme.Radius)))
        {
            using var fill = new SolidBrush(Color.FromArgb(130, 255, 255, 255));
            g.FillPath(fill, path);
            using var pen = new Pen(Glass.Hairline, 1f);
            g.DrawPath(pen, path);
        }

        using var capFont = Theme.Font(8f);
        using var valFont = Theme.MediumFont(11f);
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
        TextRenderer.DrawText(g, Caption.ToUpperInvariant(), capFont,
            new Rectangle(PxI(12), PxI(10), Width - PxI(20), capFont.Height), Theme.TextMuted, flags);
        TextRenderer.DrawText(g, _value, valFont,
            new Rectangle(PxI(12), PxI(28), Width - PxI(20), valFont.Height + 2),
            _value is "—" or "N/A" or "Not supported" or "No reply" ? Theme.TextFaint : Theme.Accent, flags);
    }
}

/// <summary>
/// Visual preview of the stepping configuration: the OSD track at an example level, with a tick
/// at every step of the grid, the 0/50/100% marks and a one-line summary.
/// </summary>
internal sealed class StepPreview : ThemedControl
{
    private int _step = 10;
    private int _fine = 2;

    public StepPreview()
    {
        BackColor = Theme.Surface;
        Height = 92;
        Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
    }

    public void SetSteps(int stepPercent, int fineStepPercent)
    {
        _step = Math.Max(1, stepPercent);
        _fine = Math.Max(1, fineStepPercent);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        PrepareGraphics(g);
        Glass.PaintBehind(g, this);

        var box = new RectangleF(Px(20), Px(4), Width - Px(40), Height - Px(16));
        using (var path = Theme.RoundedRect(box, Px(Theme.Radius)))
        {
            using var fill = new SolidBrush(Color.FromArgb(120, 255, 255, 255));
            g.FillPath(fill, path);
            using var hair = new Pen(Glass.Hairline, 1f);
            g.DrawPath(hair, path);
        }

        float barX = box.X + Px(16);
        float barW = box.Width - Px(32);
        float ch = Px(10);
        float barY = box.Y + Px(14);

        // Example level: the grid value nearest 70%
        double examplePct = Math.Round(70.0 / _step) * _step;
        float fillW = (float)(Math.Clamp(examplePct, 0, 100) / 100.0) * barW;
        using (var track = Theme.RoundedRect(new RectangleF(barX, barY, barW, ch), ch / 2))
        using (var trackBrush = new SolidBrush(Color.FromArgb(34, 15, 23, 42)))
            g.FillPath(trackBrush, track);
        if (fillW > ch)
        {
            using var fill = Theme.RoundedRect(new RectangleF(barX, barY, fillW, ch), ch / 2);
            using var fillBrush = new SolidBrush(Theme.Accent);
            g.FillPath(fillBrush, fill);
        }

        // A tick at every grid step
        int steps = Math.Max(1, (int)Math.Round(100.0 / _step));
        using (var tick = new SolidBrush(Color.FromArgb(150, 255, 255, 255)))
        {
            for (int i = 1; i < steps; i++)
            {
                float tx = barX + barW * i * _step / 100f;
                if (tx >= barX + barW - Px(2)) break;
                g.FillEllipse(tick, tx - Px(1.5f), barY + ch / 2 - Px(1.5f), Px(3), Px(3));
            }
        }

        // 0 / 50 / 100% marks under the bar
        using var markFont = Theme.Font(7.5f);
        var markFlags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        int markY = (int)(barY + ch + Px(3));
        TextRenderer.DrawText(g, "0%", markFont, new Point((int)barX, markY), Theme.TextMuted, markFlags);
        var mid = TextRenderer.MeasureText(g, "50%", markFont, Size.Empty, markFlags);
        TextRenderer.DrawText(g, "50%", markFont, new Point((int)(barX + barW / 2 - mid.Width / 2f), markY), Theme.TextMuted, markFlags);
        var end = TextRenderer.MeasureText(g, "100%", markFont, Size.Empty, markFlags);
        TextRenderer.DrawText(g, "100%", markFont, new Point((int)(barX + barW - end.Width), markY), Theme.TextMuted, markFlags);

        double next1 = Math.Min(100, examplePct + _step), next2 = Math.Min(100, examplePct + 2 * _step);
        string summary = $"Each press {_step}%  ({examplePct:0}% → {next1:0}% → {next2:0}%)   ·   Fine {_fine}%";
        using var font = Theme.Font(8.75f);
        TextRenderer.DrawText(g, summary, font,
            new Rectangle((int)barX, (int)(barY + ch + Px(20)), (int)barW, font.Height + 4), Theme.Accent,
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
    }
}

/// <summary>
/// Compact "−  10%  +" stepper on glass, replacing the Windows NumericUpDown.
/// Click − / +, or use the arrow keys, mouse wheel, Home/End.
/// </summary>
internal sealed class Stepper : ThemedControl
{
    private int _value;
    private int _hoverZone; // -1 = minus, +1 = plus, 0 = none

    public int Minimum { get; set; }
    public int Maximum { get; set; } = 100;
    public string Suffix { get; set; } = "";

    public event EventHandler? ValueChanged;

    public Stepper()
    {
        SetStyle(ControlStyles.Selectable, true);
        Size = new Size(112, 34);
        TabStop = true;
        Cursor = Cursors.Default;
    }

    public int Value
    {
        get => _value;
        set
        {
            value = Math.Clamp(value, Minimum, Maximum);
            if (_value == value) return;
            _value = value;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private float ButtonW => Px(32);

    private int ZoneAt(Point p) => p.X < ButtonW ? -1 : p.X > Width - ButtonW ? 1 : 0;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int zone = ZoneAt(e.Location);
        if (zone != _hoverZone)
        {
            _hoverZone = zone;
            Cursor = zone != 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hoverZone = 0;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        if (e.Button == MouseButtons.Left)
            Value += ZoneAt(e.Location);
        base.OnMouseDown(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        Value += Math.Sign(e.Delta);
        base.OnMouseWheel(e);
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Up: case Keys.Right: Value += 1; e.Handled = true; break;
            case Keys.Down: case Keys.Left: Value -= 1; e.Handled = true; break;
            case Keys.Home: Value = Minimum; e.Handled = true; break;
            case Keys.End: Value = Maximum; e.Handled = true; break;
        }
        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        PrepareGraphics(g);
        Glass.PaintBehind(g, this);

        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        float radius = Px(Theme.Radius);
        using (var path = Theme.RoundedRect(rect, radius))
        {
            using var fill = new SolidBrush(Color.FromArgb(185, 255, 255, 255));
            g.FillPath(fill, path);
            using var pen = new Pen(Focused && ShowFocusCues ? Color.FromArgb(150, Theme.Accent) : Glass.Hairline, 1f);
            g.DrawPath(pen, path);
        }

        // − and + buttons: a soft round highlight on hover, dimmed at the limits
        void Button(float cx, int zone, bool plus)
        {
            bool enabled = plus ? _value < Maximum : _value > Minimum;
            if (enabled && _hoverZone == zone)
            {
                float r = Px(12);
                using var hover = new SolidBrush(Color.FromArgb(28, 15, 23, 42));
                g.FillEllipse(hover, cx - r, Height / 2f - r, 2 * r, 2 * r);
            }
            using var pen = new Pen(enabled ? Theme.Text : Theme.TextFaint, Px(1.6f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            float d = Px(5);
            float cy = Height / 2f;
            g.DrawLine(pen, cx - d, cy, cx + d, cy);
            if (plus) g.DrawLine(pen, cx, cy - d, cx, cy + d);
        }
        Button(ButtonW / 2f, -1, plus: false);
        Button(Width - ButtonW / 2f, 1, plus: true);

        using var font = Theme.MediumFont(10f);
        TextRenderer.DrawText(g, $"{_value}{Suffix}", font,
            new Rectangle((int)ButtonW, 0, Width - 2 * (int)ButtonW, Height), Theme.Accent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }
}