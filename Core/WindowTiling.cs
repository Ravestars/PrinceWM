using PrinceWM.Native;
using static PrinceWM.Native.NativeMethods;

namespace PrinceWM.Core;

internal static class WindowTiling
{
    public static void SnapSideBySide(IntPtr left, IntPtr right, IntPtr anchor)
    {
        if (left == IntPtr.Zero || right == IntPtr.Zero || left == right) return;

        IntPtr mon = MonitorFromWindow(anchor, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(mon, ref mi)) return;
        RECT wa = mi.rcWork;

        int halfW = wa.Width / 2;
        Place(left, wa.Left, wa.Top, halfW, wa.Height);
        Place(right, wa.Left + halfW, wa.Top, wa.Width - halfW, wa.Height);

        WindowScanner.Activate(right);
        WindowScanner.Activate(left);
    }

    private static void Place(IntPtr hwnd, int x, int y, int w, int h)
    {

        if (IsIconic(hwnd) || IsZoomed(hwnd)) ShowWindow(hwnd, SW_RESTORE);
        SetWindowPos(hwnd, HWND_TOP, x, y, w, h, SWP_SHOWWINDOW | SWP_NOZORDER);
    }
}
