using System.Text;

namespace PrinceWM.Core;

internal static class SnapStore
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrinceWM", "snaps");

    private static string FileFor(string appKey)
    {
        var sb = new StringBuilder(appKey.Length);
        foreach (char c in appKey) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        string name = sb.ToString();
        if (name.Length > 96) name = name[..96];
        return Path.Combine(Dir, name + ".jpg");
    }

    public static string? PathIfExists(string appKey)
    {
        string p = FileFor(appKey);
        return File.Exists(p) ? p : null;
    }

    public static void Save(string appKey, System.Drawing.Bitmap bmp)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            bmp.Save(FileFor(appKey), System.Drawing.Imaging.ImageFormat.Jpeg);
        }
        catch (Exception ex) { Log.Ex("SnapStore.Save", ex); }
    }
}
