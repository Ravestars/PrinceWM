using System.Numerics;

namespace PrinceWM.Core;

internal enum StrokeKind { Free, Line, Rect, Ellipse }

internal enum DrawTool { Pen, Line, Rect, Ellipse, Fill, Eraser }

internal sealed class Stroke
{
    public StrokeKind Kind { get; set; }
    public int Color { get; set; } = 0xFFFFFF;
    public float Thickness { get; set; } = 4f;
    public int Fill { get; set; } = -1;
    public List<float> Pts { get; set; } = new();

    public void Add(Vector2 p) { Pts.Add(p.X); Pts.Add(p.Y); }
    public int Count => Pts.Count / 2;
    public Vector2 At(int i) => new(Pts[i * 2], Pts[i * 2 + 1]);

    public (float x, float y, float w, float h) Bounds()
    {
        if (Pts.Count < 2) return (0, 0, 0, 0);
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        for (int i = 0; i + 1 < Pts.Count; i += 2)
        {
            minX = MathF.Min(minX, Pts[i]); maxX = MathF.Max(maxX, Pts[i]);
            minY = MathF.Min(minY, Pts[i + 1]); maxY = MathF.Max(maxY, Pts[i + 1]);
        }
        return (minX, minY, maxX - minX, maxY - minY);
    }

    public bool EnclosesPoint(Vector2 p)
    {
        var (x, y, w, h) = Bounds();
        if (w <= 0 || h <= 0) return false;
        if (Kind == StrokeKind.Rect) return p.X >= x && p.X <= x + w && p.Y >= y && p.Y <= y + h;
        if (Kind == StrokeKind.Ellipse)
        {
            float cx = x + w * 0.5f, cy = y + h * 0.5f, rx = w * 0.5f, ry = h * 0.5f;
            if (rx <= 0 || ry <= 0) return false;
            float dx = (p.X - cx) / rx, dy = (p.Y - cy) / ry;
            return dx * dx + dy * dy <= 1f;
        }
        return false;
    }
}

internal sealed class DrawState
{
    public bool Active;
    public DrawTool Tool;
    public int Color = 0xFFFFFF;
    public float Size = 4f;
    public IReadOnlyList<Stroke>? Strokes;
    public Stroke? InProgress;
    public bool HoverToggle;
    public int HoverButton = -1;
}
