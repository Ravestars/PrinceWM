using System.Drawing.Imaging;
using PrinceWM.Core;
using PrinceWM.Native;
using Vortice.Direct2D1;
using Vortice.DXGI;
using DcAlphaMode = Vortice.DCommon.AlphaMode;

namespace PrinceWM.Capture;

internal sealed class IconCache : IDisposable
{
    private readonly ID2D1DeviceContext _d2d;
    private readonly Dictionary<string, ID2D1Bitmap?> _icons = new(StringComparer.Ordinal);

    public IconCache(ID2D1DeviceContext d2d) => _d2d = d2d;

    public ID2D1Bitmap? Get(WindowItem it)
    {
        if (_icons.TryGetValue(it.AppKey, out var cached)) return cached;
        ID2D1Bitmap? bmp = null;
        try { bmp = Extract(it.Hwnd); } catch { }
        _icons[it.AppKey] = bmp;
        return bmp;
    }

    private ID2D1Bitmap? Extract(IntPtr hwnd)
    {
        IntPtr hicon = NativeMethods.GetWindowIconHandle(hwnd);
        if (hicon == IntPtr.Zero) return null;

        using var icon = System.Drawing.Icon.FromHandle(hicon);
        using var bmp = icon.ToBitmap();
        var rect = new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            var props = new BitmapProperties1(
                new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, DcAlphaMode.Premultiplied),
                96f, 96f, BitmapOptions.None);
            return _d2d.CreateBitmap(new Vortice.Mathematics.SizeI(bmp.Width, bmp.Height), data.Scan0, (uint)data.Stride, props);
        }
        finally { bmp.UnlockBits(data); }
    }

    public void Dispose()
    {
        foreach (var b in _icons.Values) b?.Dispose();
        _icons.Clear();
    }
}
