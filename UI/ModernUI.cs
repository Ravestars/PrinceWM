using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PrinceWM.UI;

internal static class ModernUI
{
    public static readonly Color Accent = Color.FromArgb(120, 205, 255);
    public static readonly Color Text = Color.FromArgb(236, 240, 248);
    public static readonly Color SubText = Color.FromArgb(150, 160, 178);
    public static readonly Color TrackOff = Color.FromArgb(64, 70, 84);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void RoundCorners(IntPtr hwnd)
    {
        int pref = 2;
        DwmSetWindowAttribute(hwnd, 33, ref pref, sizeof(int));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    public static void Acrylic(IntPtr hwnd, uint tintArgb)
    {

        uint a = (tintArgb >> 24) & 0xFF, r = (tintArgb >> 16) & 0xFF, g = (tintArgb >> 8) & 0xFF, b = tintArgb & 0xFF;
        uint abgr = (a << 24) | (b << 16) | (g << 8) | r;

        var accent = new AccentPolicy { AccentState = 4, GradientColor = abgr };
        int size = Marshal.SizeOf(accent);
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new WindowCompositionAttributeData { Attribute = 19, Data = ptr, SizeOfData = size };
            SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    public static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

internal sealed class FlatToggle : Control
{
    private bool _checked;
    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set { if (_checked == value) return; _checked = value; CheckedChanged?.Invoke(this, EventArgs.Empty); Invalidate(); }
    }

    public FlatToggle()
    {
        Size = new Size(44, 24);
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var track = new Rectangle(0, (Height - 20) / 2, 42, 20);
        using (var path = ModernUI.RoundedRect(track, 10))
        using (var brush = new SolidBrush(_checked ? ModernUI.Accent : ModernUI.TrackOff))
            g.FillPath(brush, path);

        int knobX = _checked ? track.Right - 18 : track.Left + 2;
        using var knob = new SolidBrush(Color.White);
        g.FillEllipse(knob, knobX, track.Top + 2, 16, 16);
    }
}

internal sealed class FlatSlider : Control
{
    private int _min, _max = 100, _value;
    private bool _drag;
    public event EventHandler? ValueChanged;

    public int Minimum { get => _min; set { _min = value; Invalidate(); } }
    public int Maximum { get => _max; set { _max = value; Invalidate(); } }
    public int Value
    {
        get => _value;
        set { int v = Math.Clamp(value, _min, _max); if (v == _value) return; _value = v; ValueChanged?.Invoke(this, EventArgs.Empty); Invalidate(); }
    }

    public FlatSlider()
    {
        Height = 24;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    private void SetFromX(int x)
    {
        float t = Math.Clamp((x - 8f) / Math.Max(1f, Width - 16f), 0f, 1f);
        Value = _min + (int)MathF.Round(t * (_max - _min));
    }

    protected override void OnMouseDown(MouseEventArgs e) { _drag = true; SetFromX(e.X); base.OnMouseDown(e); }
    protected override void OnMouseMove(MouseEventArgs e) { if (_drag) SetFromX(e.X); base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _drag = false; base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float t = _max > _min ? (_value - _min) / (float)(_max - _min) : 0f;
        int cy = Height / 2;
        int x0 = 8, x1 = Width - 8;
        int hx = x0 + (int)(t * (x1 - x0));

        using (var back = new Pen(ModernUI.TrackOff, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawLine(back, x0, cy, x1, cy);
        using (var fill = new Pen(ModernUI.Accent, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawLine(fill, x0, cy, hx, cy);
        using (var knob = new SolidBrush(Color.White))
            g.FillEllipse(knob, hx - 7, cy - 7, 14, 14);
    }
}

internal sealed class SwatchButton : Control
{
    private Color _color = Color.White;
    public Color Color { get => _color; set { _color = value; Invalidate(); } }

    public SwatchButton()
    {
        Size = new Size(64, 26);
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = ModernUI.RoundedRect(r, 7);
        using (var brush = new SolidBrush(_color)) g.FillPath(brush, path);
        using (var pen = new Pen(Color.FromArgb(90, 255, 255, 255), 1f)) g.DrawPath(pen, path);
    }
}
