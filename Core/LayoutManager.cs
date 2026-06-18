using System.Numerics;

namespace PrinceWM.Core;

internal static class LayoutManager
{
    public static float Gap = 56f;
    private static readonly Random Rng = new();

    public static void Arrange(IReadOnlyList<WindowItem> items, Dictionary<string, Vector2> saved,
        List<(Vector2 pos, Vector2 size)>? obstacles = null)
    {
        var placed = new List<WindowItem>();
        var toPlace = new List<WindowItem>();

        foreach (var it in items)
        {
            if (saved.TryGetValue(it.AppKey, out var p) && !OverlapsPlaced(p, it.WorldSize, placed))
            {
                it.WorldPos = p;
                placed.Add(it);
            }
            else toPlace.Add(it);
        }

        if (placed.Count == 0)
            NativeLayout.Apply(items);
        else
            foreach (var it in toPlace)
            {
                it.WorldPos = FindFreeSpot(it, placed, obstacles);
                placed.Add(it);
            }

        foreach (var it in items)
            saved[it.AppKey] = it.WorldPos;
    }

    private static Vector2 FindFreeSpot(WindowItem it, List<WindowItem> placed,
    List<(Vector2 pos, Vector2 size)>? obstacles)
    {
        if (placed.Count == 0) return Vector2.Zero;

        for (int attempt = 0; attempt < 80; attempt++)
        {
            var anchor = placed[Rng.Next(placed.Count)];
            foreach (int side in ShuffledSides())
            {
                Vector2 pos = side switch
                {
                    0 => new Vector2(anchor.WorldPos.X + anchor.WorldSize.X + Gap, anchor.WorldPos.Y),
                    1 => new Vector2(anchor.WorldPos.X, anchor.WorldPos.Y + anchor.WorldSize.Y + Gap),
                    2 => new Vector2(anchor.WorldPos.X - it.WorldSize.X - Gap, anchor.WorldPos.Y),
                    _ => new Vector2(anchor.WorldPos.X, anchor.WorldPos.Y - it.WorldSize.Y - Gap),
                };
                if (!Collides(pos, it.WorldSize, placed, obstacles)) return pos;
            }
        }

        float maxX = float.MinValue, minY = float.MaxValue;
        foreach (var p in placed)
        {
            maxX = MathF.Max(maxX, p.WorldPos.X + p.WorldSize.X);
            minY = MathF.Min(minY, p.WorldPos.Y);
        }
        return new Vector2(maxX + Gap, minY);
    }

    private static int[] ShuffledSides()
    {
        var s = new[] { 0, 1, 2, 3 };
        for (int i = s.Length - 1; i > 0; i--)
        {
            int j = Rng.Next(i + 1);
            (s[i], s[j]) = (s[j], s[i]);
        }
        return s;
    }

    private static bool OverlapsPlaced(Vector2 pos, Vector2 size, List<WindowItem> placed)
    {
        foreach (var p in placed)
            if (Collision.Overlaps(pos, size, p.WorldPos, p.WorldSize, 4f)) return true;
        return false;
    }

    private static bool Collides(Vector2 pos, Vector2 size, List<WindowItem> placed,
        List<(Vector2 pos, Vector2 size)>? obstacles)
    {
        foreach (var p in placed)
            if (Collision.Overlaps(pos, size, p.WorldPos, p.WorldSize, Gap)) return true;
        if (obstacles != null)
            foreach (var (op, os) in obstacles)
                if (Collision.Overlaps(pos, size, op, os, Gap)) return true;
        return false;
    }
}
