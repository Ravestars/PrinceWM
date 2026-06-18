using System.Text.Json;

namespace PrinceWM.Core;

internal static class ThemeStore
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrinceWM");
    private static readonly string FilePath = Path.Combine(Dir, "theme.json");

    public static Theme Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Theme>(File.ReadAllText(FilePath)) ?? new Theme();
        }
        catch (Exception ex)
        {
            Log.Ex("ThemeStore.Load", ex);
        }
        return new Theme();
    }

    public static void Save(Theme theme)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(theme));
        }
        catch (Exception ex)
        {
            Log.Ex("ThemeStore.Save", ex);
        }
    }
}
