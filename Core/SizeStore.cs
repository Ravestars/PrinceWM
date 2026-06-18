using System.Numerics;
using System.Text.Json;

namespace PrinceWM.Core;

internal static class SizeStore
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrinceWM");
    private static readonly string FilePath = Path.Combine(Dir, "sizes.json");

    public static Dictionary<string, Vector2> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            string json = File.ReadAllText(FilePath);
            var raw = JsonSerializer.Deserialize<Dictionary<string, float[]>>(json);
            if (raw == null) return new();
            var dict = new Dictionary<string, Vector2>();
            foreach (var (k, v) in raw)
                if (v.Length == 2) dict[k] = new Vector2(v[0], v[1]);
            return dict;
        }
        catch (Exception ex)
        {
            Log.Ex("SizeStore.Load", ex);
            return new();
        }
    }

    public static void Save(Dictionary<string, Vector2> sizes)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var raw = new Dictionary<string, float[]>();
            foreach (var (k, v) in sizes)
                raw[k] = new[] { v.X, v.Y };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(raw));
        }
        catch (Exception ex)
        {
            Log.Ex("SizeStore.Save", ex);
        }
    }
}
