using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using PrinceWM.Native;
using static PrinceWM.Native.NativeMethods;

namespace PrinceWM.Core;

internal static class WindowScanner
{

    private static readonly HashSet<string> ClassBlocklist = new(StringComparer.Ordinal)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "Windows.UI.Core.CoreWindow",
        "XamlExplorerHostIslandWindow",
        "ForegroundStaging",
        "MSCTFIME UI",
        "Default IME",
    };

    public static List<WindowItem> Scan(IntPtr selfHwnd, bool includeMinimized = true,
    Dictionary<string, Vector2>? sizeCache = null)
    {
        var results = new List<WindowItem>();
        IntPtr shell = GetShellWindow();

        EnumWindows((hWnd, _) =>
        {
            if (hWnd == selfHwnd) return true;
            if (hWnd == shell) return true;
            if (!IsWindowVisible(hWnd)) return true;

            int len = GetWindowTextLength(hWnd);
            if (len == 0) return true;

            long exStyle = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();
            bool toolWindow = (exStyle & WS_EX_TOOLWINDOW) != 0;
            bool appWindow = (exStyle & WS_EX_APPWINDOW) != 0;
            if (toolWindow && !appWindow) return true;

            if (!appWindow)
            {
                IntPtr root = GetAncestor(hWnd, GA_ROOTOWNER);
                if (LastVisibleActivePopup(root) != hWnd) return true;
            }

            if (IsCloaked(hWnd)) return true;

            string cls = GetClass(hWnd);
            if (ClassBlocklist.Contains(cls)) return true;

            string title = GetTitle(hWnd, len);
            if (string.IsNullOrWhiteSpace(title)) return true;

            bool minimized = IsIconic(hWnd);
            if (minimized && !includeMinimized) return true;

            int x, y, w, h;
            if (minimized)
            {

                var wp = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
                if (GetWindowPlacement(hWnd, ref wp))
                {
                    RECT nr = wp.rcNormalPosition;
                    x = nr.Left; y = nr.Top; w = nr.Width; h = nr.Height;
                }
                else { x = 0; y = 0; w = 960; h = 600; }
            }
            else
            {
                if (!GetWindowRect(hWnd, out RECT r)) return true;
                x = r.Left; y = r.Top; w = r.Width; h = r.Height;
            }
            if (w <= 0 || h <= 0) { w = 960; h = 600; }

            string appKey = $"{ProcessNameOf(hWnd)}|{cls}";

            var size = new Vector2(w, h);
            if (sizeCache != null)
            {
                bool sane = w >= 150 && h >= 120 && w <= 16000 && h <= 16000;
                bool hasGood = sizeCache.TryGetValue(appKey, out var good);
                if (!minimized && sane) sizeCache[appKey] = size;
                else if (hasGood) size = good;
                else if (sane) sizeCache[appKey] = size;
            }

            results.Add(new WindowItem
            {
                Hwnd = hWnd,
                Title = title,
                AppKey = appKey,
                Key = $"{appKey}|{title}",
                WorldPos = new Vector2(x, y),
                WorldSize = size,
                IsMinimized = minimized,
            });
            return true;
        }, IntPtr.Zero);

        return results;
    }

    private static string ProcessNameOf(IntPtr hWnd)
    {
        try
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch { return "?"; }
    }

    private static IntPtr LastVisibleActivePopup(IntPtr window)
    {
        IntPtr cur = window;
        for (int i = 0; i < 32; i++)
        {
            IntPtr popup = GetLastActivePopup(cur);
            if (IsWindowVisible(popup)) return popup;
            if (popup == cur) return IntPtr.Zero;
            cur = popup;
        }
        return IntPtr.Zero;
    }

    private static bool IsCloaked(IntPtr hWnd)
    {
        int cloaked = 0;
        int hr = DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out cloaked, sizeof(int));
        return hr == 0 && cloaked != 0;
    }

    private static string GetTitle(IntPtr hWnd, int len)
    {
        var sb = new StringBuilder(len + 1);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string GetClass(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static void Activate(IntPtr hWnd)
    {
        if (IsIconic(hWnd))
            ShowWindow(hWnd, SW_RESTORE);

        uint foreThread = GetWindowThreadProcessId(GetForegroundWindowSafe(), out _);
        uint thisThread = GetCurrentThreadId();
        uint targetThread = GetWindowThreadProcessId(hWnd, out _);

        // Only do the AttachThreadInput dance when ANOTHER process owns the foreground. When we
        // already own it (the normal case when committing from the overlay), plain
        // SetForegroundWindow is permitted - and attaching our input queue to the target's thread
        // can stall if that thread is busy (a game sitting in its render loop), which froze the
        // switch near the end. Skipping the attach in that case keeps the hand-off instant.
        bool attach = foreThread != thisThread && foreThread != targetThread;
        if (attach)
        {
            AttachThreadInput(thisThread, foreThread, true);
            AttachThreadInput(thisThread, targetThread, true);
        }

        BringWindowToTop(hWnd);
        SetForegroundWindow(hWnd);

        if (attach)
        {
            AttachThreadInput(thisThread, foreThread, false);
            AttachThreadInput(thisThread, targetThread, false);
        }
    }

    private static IntPtr GetForegroundWindowSafe()
    {

        return ForegroundImport.GetForegroundWindow();
    }

    private static class ForegroundImport
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();
    }
}
