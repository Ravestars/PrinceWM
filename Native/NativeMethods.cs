using System.Runtime.InteropServices;
using System.Text;

namespace PrinceWM.Native;

internal static class NativeMethods
{

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetParent(IntPtr hWnd);

    public const uint GA_ROOT = 2;
    public const uint GA_ROOTOWNER = 3;

    [DllImport("user32.dll")]
    public static extern IntPtr GetLastActivePopup(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetShellWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
        uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll")]
    public static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc cb, IntPtr lParam);

    public static bool IsWindows11 => Environment.OSVersion.Version.Build >= 22000;

    public static IntPtr FindAnimatedWallpaperWindow()
    {
        IntPtr best = IntPtr.Zero;
        long bestArea = 0;

        void Consider(IntPtr h)
        {
            if (!IsWindowVisible(h)) return;
            string proc = ProcessNameOf(h);

            bool we = proc.StartsWith("wallpaper", StringComparison.OrdinalIgnoreCase)
                      && !proc.StartsWith("wallpaperengine", StringComparison.OrdinalIgnoreCase);

            bool lively = proc.StartsWith("lively", StringComparison.OrdinalIgnoreCase)
                          || proc.Equals("libmpvplayer", StringComparison.OrdinalIgnoreCase);
            if (!we && !lively) return;
            if (!GetWindowRect(h, out RECT r)) return;

            if (!CoversMonitor(r)) return;
            long area = (long)r.Width * r.Height;
            if (area > bestArea) { bestArea = area; best = h; }
        }

        EnumWindows((h, _) => { Consider(h); return true; }, IntPtr.Zero);
        var consider = new EnumWindowsProc((h, _) => { Consider(h); return true; });
        IntPtr progman = FindWindow("Progman", null);
        if (progman != IntPtr.Zero) EnumChildWindows(progman, consider, IntPtr.Zero);
        EnumWindows((top, _) =>
        {
            if (ClassNameOf(top) == "WorkerW") EnumChildWindows(top, consider, IntPtr.Zero);
            return true;
        }, IntPtr.Zero);

        return best;
    }

    private static string ProcessNameOf(IntPtr h)
    {
        try
        {
            GetWindowThreadProcessId(h, out uint pid);
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch { return ""; }
    }

    private static string ClassNameOf(IntPtr h)
    {
        var sb = new System.Text.StringBuilder(64);
        GetClassName(h, sb, sb.Capacity);
        return sb.ToString();
    }

    private static bool CoversMonitor(RECT r)
    {
        foreach (var sc in System.Windows.Forms.Screen.AllScreens)
        {
            var b = sc.Bounds;
            if (Math.Abs(r.Left - b.Left) <= 6 && Math.Abs(r.Top - b.Top) <= 6 &&
                Math.Abs(r.Width - b.Width) <= 6 && Math.Abs(r.Height - b.Height) <= 6)
                return true;
        }
        return false;
    }

    public static IntPtr FindWallpaperWorkerW()
    {
        IntPtr progman = FindWindow("Progman", null);
        SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 1000, out _);

        IntPtr worker = IntPtr.Zero;
        EnumWindows((top, _) =>
        {
            if (FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                worker = FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
            return true;
        }, IntPtr.Zero);

        return worker;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SystemParametersInfo(uint action, uint uiParam, System.Text.StringBuilder pvParam, uint winIni);

    public static string GetWallpaperPath()
    {
        var sb = new System.Text.StringBuilder(520);
        SystemParametersInfo(0x0073, (uint)sb.Capacity, sb, 0);
        string p = sb.ToString();
        if (!string.IsNullOrEmpty(p) && System.IO.File.Exists(p)) return p;

        string td = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Windows", "Themes", "TranscodedWallpaper");
        return System.IO.File.Exists(td) ? td : p;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;

    public const long WS_VISIBLE = 0x10000000L;
    public const long WS_EX_TOOLWINDOW = 0x00000080L;
    public const long WS_EX_APPWINDOW = 0x00040000L;
    public const long WS_EX_NOREDIRECTIONBITMAP = 0x00200000L;

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hWnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hWnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    public const int DWMWA_CLOAKED = 14;
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    public static bool GetExtendedFrameBounds(IntPtr hWnd, out RECT rect) =>
    DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out rect,
        System.Runtime.InteropServices.Marshal.SizeOf<RECT>()) == 0;

    public static bool IsWindowCloaked(IntPtr hWnd) =>
    DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0;

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    public delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    public const int OBJID_WINDOW = 0;

    [DllImport("user32.dll")]
    public static extern bool ClipCursor(IntPtr lpRect);

    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public const uint WM_CLOSE = 0x0010;

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    public const int SW_HIDE = 0;
    public const int SW_SHOW = 5;
    public const int SW_MINIMIZE = 6;
    public const int SW_RESTORE = 9;

    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
    private static extern IntPtr GetClassLongPtr(IntPtr hWnd, int nIndex);

    private const uint WM_GETICON = 0x007F;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const int GCLP_HICON = -14;
    private const int GCLP_HICONSM = -34;

    public static IntPtr GetWindowIconHandle(IntPtr hWnd)
    {
        foreach (IntPtr type in new[] { (IntPtr)1, (IntPtr)2, (IntPtr)0 })
        {
            SendMessageTimeout(hWnd, WM_GETICON, type, IntPtr.Zero, SMTO_ABORTIFHUNG, 120, out IntPtr r);
            if (r != IntPtr.Zero) return r;
        }
        IntPtr h = GetClassLongPtr(hWnd, GCLP_HICON);
        return h != IntPtr.Zero ? h : GetClassLongPtr(hWnd, GCLP_HICONSM);
    }

    public static IntPtr GetDesktopIconHost()
    {
        IntPtr progman = FindWindow("Progman", null);
        IntPtr defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (defView != IntPtr.Zero) return defView;

        IntPtr found = IntPtr.Zero;
        EnumWindows((top, _) =>
        {
            IntPtr dv = FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (dv != IntPtr.Zero) { found = dv; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    public const int WH_KEYBOARD_LL = 13;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP = 0x0105;

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    public const uint LLKHF_ALTDOWN = 0x20;

    public const uint VK_TAB = 0x09;
    public const uint VK_RETURN = 0x0D;
    public const uint VK_ESCAPE = 0x1B;
    public const uint VK_SPACE = 0x20;
    public const uint VK_LEFT = 0x25;
    public const uint VK_UP = 0x26;
    public const uint VK_RIGHT = 0x27;
    public const uint VK_DOWN = 0x28;
    public const uint VK_MENU = 0x12;
    public const uint VK_LSHIFT = 0xA0;
    public const uint VK_RSHIFT = 0xA1;

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPLACEMENT
    {
        public int length;
        public int flags;
        public int showCmd;
        public POINT ptMinPosition;
        public POINT ptMaxPosition;
        public RECT rcNormalPosition;
    }

    [DllImport("user32.dll")]
    public static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern bool IsZoomed(IntPtr hWnd);

    // Nudge a window (and its children) to repaint, so a freshly-(re)started Windows.Graphics
    // capture session gets a frame even for a static window that wouldn't redraw on its own.
    [DllImport("user32.dll")]
    public static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

    public const uint RDW_INVALIDATE = 0x0001;
    public const uint RDW_ERASE = 0x0004;
    public const uint RDW_ALLCHILDREN = 0x0080;

    public static readonly IntPtr HWND_TOP = IntPtr.Zero;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_ASYNCWINDOWPOS = 0x4000;

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    public const uint MONITOR_DEFAULTTOPRIMARY = 0x00000001;
    public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    public const uint PM_NOREMOVE = 0x0000;

    [DllImport("user32.dll")]
    public static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);
}
