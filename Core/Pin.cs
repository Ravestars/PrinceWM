using System.Numerics;

namespace PrinceWM.Core;

internal enum PinKind { Image, Note }

internal sealed class Pin
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public PinKind Kind { get; set; }

    public float X { get; set; }
    public float Y { get; set; }
    public float W { get; set; }
    public float H { get; set; }

    public string? ImageFile { get; set; }

    public string Text { get; set; } = "";

    public bool Locked { get; set; }

    public long CreatedUnix { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public Vector2 Pos => new(X, Y);
    public Vector2 SizeV => new(W, H);
    public Vector2 Center => new(X + W * 0.5f, Y + H * 0.5f);

    public bool Contains(Vector2 p) => p.X >= X && p.Y >= Y && p.X <= X + W && p.Y <= Y + H;

    public bool OlderThan(TimeSpan age) =>
        DateTimeOffset.UtcNow.ToUnixTimeSeconds() - CreatedUnix > (long)age.TotalSeconds;
}
