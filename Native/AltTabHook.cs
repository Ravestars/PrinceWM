using System.Runtime.InteropServices;
using static PrinceWM.Native.NativeMethods;

namespace PrinceWM.Native;

internal enum NavKey
{
    None,
    Escape,
    Enter,
    Left,
    Up,
    Right,
    Down,
}

internal sealed class AltTabHook : IDisposable
{

    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hookHandle;

    public bool OverlayOpen { get; set; }

    public event Action<bool>? AltTabPressed;

    public event Action? ScreenshotKey;

    private const uint VK_SNAPSHOT = 0x2C;

    public event Action<NavKey, bool>? NavPressed;

    public AltTabHook()
    {
        _proc = HookCallback;
        _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        if (_hookHandle == IntPtr.Zero)
            throw new InvalidOperationException("Failed to install low-level keyboard hook.");
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        int msg = (int)wParam;

        if ((msg == WM_KEYUP || msg == WM_SYSKEYUP) && OverlayOpen)
        {
            var up = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (up.vkCode == VK_SNAPSHOT) { ScreenshotKey?.Invoke(); return 1; }
        }

        if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            uint vk = data.vkCode;
            bool altDown = (data.flags & LLKHF_ALTDOWN) != 0 || IsDown(VK_MENU);

            if (vk == VK_SNAPSHOT && OverlayOpen) return 1;

            if (vk == VK_TAB && altDown)
            {
                bool shift = IsDown(0x10);
                AltTabPressed?.Invoke(shift);
                return 1;
            }

            if (OverlayOpen)
            {
                NavKey nav = vk switch
                {
                    VK_ESCAPE => NavKey.Escape,
                    VK_RETURN => NavKey.Enter,
                    VK_LEFT => NavKey.Left,
                    VK_UP => NavKey.Up,
                    VK_RIGHT => NavKey.Right,
                    VK_DOWN => NavKey.Down,
                    _ => NavKey.None,
                };
                if (nav != NavKey.None)
                {
                    NavPressed?.Invoke(nav, IsDown(0x10));
                    return 1;
                }
            }
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private static bool IsDown(uint vk) => (GetAsyncKeyState((int)vk) & 0x8000) != 0;

    public void Dispose()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }
}
