using System.Text.Json;

namespace PrinceWM.Core;

internal static class PinStore
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrinceWM");
    private static readonly string FilePath = Path.Combine(Dir, "pins.json");

    public static string ImagesDir => Path.Combine(Dir, "pins");

    public static List<Pin> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            var list = JsonSerializer.Deserialize<List<Pin>>(File.ReadAllText(FilePath));
            if (list == null) return new();

            list.RemoveAll(p => p.Kind == PinKind.Image &&
                (string.IsNullOrEmpty(p.ImageFile) || !File.Exists(Path.Combine(ImagesDir, p.ImageFile))));
            return list;
        }
        catch (Exception ex)
        {
            Log.Ex("PinStore.Load", ex);
            return new();
        }
    }

    public static void Save(List<Pin> pins)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(pins));
        }
        catch (Exception ex) { Log.Ex("PinStore.Save", ex); }
    }

    public static string? SaveImage(System.Drawing.Image img)
    {
        try
        {
            Directory.CreateDirectory(ImagesDir);
            string name = Guid.NewGuid().ToString("N") + ".png";
            img.Save(Path.Combine(ImagesDir, name), System.Drawing.Imaging.ImageFormat.Png);
            return name;
        }
        catch (Exception ex) { Log.Ex("PinStore.SaveImage", ex); return null; }
    }

    public static void DeleteImage(string? file)
    {
        if (string.IsNullOrEmpty(file)) return;
        try { File.Delete(Path.Combine(ImagesDir, file)); } catch { }
    }
}
