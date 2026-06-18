using System.Numerics;

namespace PrinceWM.Core;

internal sealed class WindowItem
{
    public required IntPtr Hwnd { get; init; }
    public required string Title { get; set; }

    public string Key { get; set; } = "";

    public string AppKey { get; set; } = "";

    public Vector2 WorldPos;
    public Vector2 WorldSize;

    public bool IsMinimized { get; set; }

    public int StackCount { get; set; } = 1;

    public List<IntPtr> StackHwnds { get; set; } = new();

    public Vector2 WorldCenter => WorldPos + WorldSize * 0.5f;

    public bool Sliding;
    public Vector2 SlideTarget;

    public bool ContainsWorldPoint(Vector2 p) =>
        p.X >= WorldPos.X && p.Y >= WorldPos.Y &&
        p.X <= WorldPos.X + WorldSize.X && p.Y <= WorldPos.Y + WorldSize.Y;
}
