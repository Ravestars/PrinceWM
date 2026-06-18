namespace PrinceWM.Core;

internal static class Log
{
    private static readonly string Path =
        System.IO.Path.Combine(AppContext.BaseDirectory, "PrinceWM.log");
    private static readonly object Gate = new();

    public static void Write(string msg)
    {
        try
        {
            lock (Gate)
                System.IO.File.AppendAllText(Path, $"{DateTime.Now:HH:mm:ss.fff}  {msg}{Environment.NewLine}");
        }
        catch { }
    }

    public static void Ex(string where, Exception ex) =>
        Write($"EX {where}: {ex.GetType().Name}: {ex.Message}");
}
