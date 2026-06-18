using System.Numerics;

namespace PrinceWM.Core;

internal static class NativeLayout
{
    private static float Gap => LayoutManager.Gap;

    public static void Apply(IReadOnlyList<WindowItem> items)
    {
        int n = items.Count;
        if (n == 0) return;

        float totalArea = 0f;
        foreach (var it in items)
            totalArea += (it.WorldSize.X + Gap) * (it.WorldSize.Y + Gap);
        float targetRowWidth = MathF.Sqrt(totalArea) * 1.25f;

        float x = 0f, y = 0f, rowHeight = 0f;

        foreach (var it in items)
        {
            if (x > 0f && x + it.WorldSize.X > targetRowWidth)
            {

                x = 0f;
                y += rowHeight + Gap;
                rowHeight = 0f;
            }

            it.WorldPos = new Vector2(x, y);
            x += it.WorldSize.X + Gap;
            rowHeight = MathF.Max(rowHeight, it.WorldSize.Y);
        }
    }
}
