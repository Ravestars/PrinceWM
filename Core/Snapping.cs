namespace PrinceWM.Core;

internal static class Snapping
{
    public static HashSet<WindowItem> GetCluster(WindowItem start, IReadOnlyList<WindowItem> all,
    float epsilon = 26f)
    {
        var cluster = new HashSet<WindowItem> { start };
        var queue = new Queue<WindowItem>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            foreach (var o in all)
            {
                if (cluster.Contains(o)) continue;
                if (Adjacent(cur, o, epsilon))
                {
                    cluster.Add(o);
                    queue.Enqueue(o);
                }
            }
        }
        return cluster;
    }

    private static bool Adjacent(WindowItem a, WindowItem b, float eps)
    {
        float aL = a.WorldPos.X, aR = aL + a.WorldSize.X, aT = a.WorldPos.Y, aB = aT + a.WorldSize.Y;
        float bL = b.WorldPos.X, bR = bL + b.WorldSize.X, bT = b.WorldPos.Y, bB = bT + b.WorldSize.Y;

        bool yOverlap = aT < bB && aB > bT;
        bool xOverlap = aL < bR && aR > bL;

        bool touchVert = yOverlap && (MathF.Abs(aR - bL) <= eps || MathF.Abs(aL - bR) <= eps);
        bool touchHorz = xOverlap && (MathF.Abs(aB - bT) <= eps || MathF.Abs(aT - bB) <= eps);
        return touchVert || touchHorz;
    }
}
