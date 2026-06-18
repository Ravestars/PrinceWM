using System.Windows.Forms;
using PrinceWM.Core;

namespace PrinceWM;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Ex("UI ThreadException", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Write($"FATAL UnhandledException: {e.ExceptionObject}");
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
            { Log.Ex("UnobservedTask", e.Exception); e.SetObserved(); };

        using var overlay = new OverlayForm();

        _ = overlay.Handle;

        Application.Run(new ApplicationContext());
    }
}
