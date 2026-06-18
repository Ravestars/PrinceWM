namespace PrinceWM.Core;

internal static class Grouper
{
    public static List<WindowItem> Stack(List<WindowItem> items, List<IntPtr> mru,
        HashSet<IntPtr>? promoted = null)
    {
        int MruIndex(IntPtr h)
        {
            int i = mru.IndexOf(h);
            return i < 0 ? int.MaxValue : i;
        }

        var groups = new Dictionary<string, List<WindowItem>>(StringComparer.Ordinal);
        foreach (var it in items)
        {
            string key = promoted != null && promoted.Contains(it.Hwnd)
                ? $"{it.AppKey}#{it.Hwnd}"
                : it.AppKey;
            if (!groups.TryGetValue(key, out var list)) { list = new(); groups[key] = list; }
            list.Add(it);
        }

        var result = new List<WindowItem>(groups.Count);
        foreach (var (key, list) in groups)
        {
            list.Sort((a, b) => MruIndex(a.Hwnd).CompareTo(MruIndex(b.Hwnd)));
            WindowItem rep = list[0];
            rep.AppKey = key;
            rep.StackCount = list.Count;
            rep.StackHwnds = list.ConvertAll(x => x.Hwnd);
            result.Add(rep);
        }
        return result;
    }
}
