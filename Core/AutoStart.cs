using System.Diagnostics;

namespace PrinceWM.Core;

internal static class AutoStart
{
    private const string TaskName = "PrinceWMSwitcher";

    public static bool IsEnabled() => Run($"/Query /TN \"{TaskName}\"") == 0;

    public static void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            string exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName!;
            Run($"/Create /TN \"{TaskName}\" /TR \"\\\"{exe}\\\"\" /SC ONLOGON /RL HIGHEST /F");
        }
        else
        {
            Run($"/Delete /TN \"{TaskName}\" /F");
        }
    }

    private static int Run(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return -1;
            p.WaitForExit(4000);
            return p.HasExited ? p.ExitCode : -1;
        }
        catch (Exception ex) { Log.Ex("AutoStart", ex); return -1; }
    }
}
