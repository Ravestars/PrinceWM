using System.Text.Json;

namespace PrinceWM.Core;

internal static class DrawStore
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrinceWM");
    private static readonly string FilePath = Path.Combine(Dir, "drawings.json");

    public static List<Stroke> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            return JsonSerializer.Deserialize<List<Stroke>>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception ex) { Log.Ex("DrawStore.Load", ex); return new(); }
    }

    public static void Save(List<Stroke> strokes)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(strokes));
        }
        catch (Exception ex) { Log.Ex("DrawStore.Save", ex); }
    }
}
