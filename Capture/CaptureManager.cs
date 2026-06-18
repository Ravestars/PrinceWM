using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using PrinceWM.Core;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace PrinceWM.Capture;

internal sealed class CaptureManager : IDisposable
{
    private readonly ID3D11Device _d3d;
    private readonly ID3D11DeviceContext _ctx;
    private readonly ID2D1DeviceContext _d2d;
    private readonly IDirect3DDevice _rtDevice;

    private readonly Dictionary<IntPtr, WindowCapture> _caps = new();
    private readonly IconCache _iconCache;

    public ID2D1Bitmap? GetIcon(WindowItem it) => _iconCache.Get(it);
    private WindowCapture? _wallpaper;
    private IntPtr _wallpaperHwnd;
    private ID2D1Bitmap? _staticWallpaper;
    private string? _staticPath;

    public ID2D1Bitmap? WallpaperBitmap => _wallpaper?.Bitmap ?? _staticWallpaper;

    public static bool IsSupported
    {
        get { try { return GraphicsCaptureSession.IsSupported(); } catch { return false; } }
    }

    public CaptureManager(ID3D11Device d3d, ID3D11DeviceContext ctx, ID2D1DeviceContext d2d)
    {
        _d3d = d3d;
        _ctx = ctx;
        _d2d = d2d;

        using var dxgiDevice = d3d.QueryInterface<IDXGIDevice>();
        _rtDevice = CaptureInterop.CreateDirect3DDevice(dxgiDevice.NativePointer);
        _iconCache = new IconCache(d2d);
    }

    public void Sync(IReadOnlyList<WindowItem> items)
    {
        var wanted = new HashSet<IntPtr>();
        foreach (var it in items)
        {
            wanted.Add(it.Hwnd);
            _appKeys[it.Hwnd] = it.AppKey;
            if (_caps.TryGetValue(it.Hwnd, out var existing))
            {
                if (!existing.IsClosed) continue;

                existing.Dispose();
                _caps.Remove(it.Hwnd);
            }
            try
            {
                _caps[it.Hwnd] = new WindowCapture(it.Hwnd, _d3d, _ctx, _d2d, _rtDevice);
            }
            catch (Exception ex)
            {

                Core.Log.Ex($"WindowCapture ctor '{it.Title}'", ex);
            }
        }

        var stale = _caps.Keys.Where(h => !wanted.Contains(h)).ToList();
        foreach (var h in stale)
        {
            _caps[h].Dispose();
            _caps.Remove(h);
            _healAttempts.Remove(h);
        }
    }

    private readonly Dictionary<IntPtr, int> _healAttempts = new();
    private readonly Dictionary<IntPtr, int> _failCounts = new();

    public void Update()
    {
        List<IntPtr>? heal = null;
        List<IntPtr>? drop = null;
        foreach (var (hwnd, cap) in _caps)
            PumpOne(hwnd, cap, ref heal, ref drop);
        ApplyDropHeal(drop, heal);
        try { _wallpaper?.Update(); } catch { }
    }

    private readonly List<IntPtr> _keyBuf = new();
    private int _rrCursor;

    public void UpdateStaggered(int budget)
    {
        if (_caps.Count > 0)
        {
            _keyBuf.Clear();
            _keyBuf.AddRange(_caps.Keys);
            int n = _keyBuf.Count;
            budget = Math.Min(budget, n);
            List<IntPtr>? heal = null, drop = null;
            for (int k = 0; k < budget; k++)
            {
                if (_rrCursor >= n) _rrCursor = 0;
                IntPtr hwnd = _keyBuf[_rrCursor++];
                if (_caps.TryGetValue(hwnd, out var cap))
                    PumpOne(hwnd, cap, ref heal, ref drop);
            }
            ApplyDropHeal(drop, heal);
        }
        try { _wallpaper?.Update(); } catch { }
    }

    private void PumpOne(IntPtr hwnd, WindowCapture cap, ref List<IntPtr>? heal, ref List<IntPtr>? drop)
    {
        try
        {
            cap.Update();
            _failCounts.Remove(hwnd);
        }
        catch (Exception ex)
        {

            int f = _failCounts.GetValueOrDefault(hwnd) + 1;
            _failCounts[hwnd] = f;
            if (f >= 3) { Core.Log.Ex("Capture dropped (repeated failure)", ex); (drop ??= new()).Add(hwnd); }
            return;
        }

        if (cap.HasContent && !cap.IsClosed) { _healAttempts.Remove(hwnd); return; }

        bool dead = cap.IsClosed || (!cap.HasContent && cap.LooksDead);
        if (dead && Native.NativeMethods.IsWindow(hwnd) &&
            _healAttempts.GetValueOrDefault(hwnd) < 5)
            (heal ??= new()).Add(hwnd);
    }

    private void ApplyDropHeal(List<IntPtr>? drop, List<IntPtr>? heal)
    {
        if (drop != null)
            foreach (var hwnd in drop)
            {
                try { _caps[hwnd].Dispose(); } catch { }
                _caps.Remove(hwnd);
                _healAttempts.Remove(hwnd);
                _failCounts.Remove(hwnd);
            }

        if (heal != null)
            foreach (var hwnd in heal)
                Rebuild(hwnd);
    }

    private void Rebuild(IntPtr hwnd)
    {
        _caps[hwnd].Dispose();
        _caps.Remove(hwnd);
        _healAttempts[hwnd] = _healAttempts.GetValueOrDefault(hwnd) + 1;
        try
        {
            _caps[hwnd] = new WindowCapture(hwnd, _d3d, _ctx, _d2d, _rtDevice);
        }
        catch (Exception ex) { Core.Log.Ex("Rebuild capture", ex); }
    }

    public void SetWallpaper(IntPtr hwnd)
    {
        if (hwnd == _wallpaperHwnd && _wallpaper != null) return;
        _wallpaper?.Dispose();
        _wallpaper = null;
        _wallpaperHwnd = hwnd;
        if (hwnd == IntPtr.Zero) return;
        try { _wallpaper = new WindowCapture(hwnd, _d3d, _ctx, _d2d, _rtDevice); }
        catch (Exception ex) { Core.Log.Ex("Wallpaper capture", ex); }
    }

    public void ClearWallpaper()
    {
        _wallpaper?.Dispose();
        _wallpaper = null;
        _wallpaperHwnd = IntPtr.Zero;
    }

    public void SetStaticWallpaper(string? path)
    {
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) { ClearStaticWallpaper(); return; }
        if (path == _staticPath && _staticWallpaper != null) return;
        ClearStaticWallpaper();
        _staticPath = path;
        try
        {
            using var bmp = new System.Drawing.Bitmap(path);
            var rect = new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            try
            {
                var props = new BitmapProperties1(new Vortice.DCommon.PixelFormat(
                    Vortice.DXGI.Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
                    96f, 96f, BitmapOptions.None);
                _staticWallpaper = _d2d.CreateBitmap(
                    new Vortice.Mathematics.SizeI(bmp.Width, bmp.Height), data.Scan0, (uint)data.Stride, props);
            }
            finally { bmp.UnlockBits(data); }
        }
        catch (Exception ex) { Core.Log.Ex("Static wallpaper load", ex); }
    }

    public void ClearStaticWallpaper()
    {
        _staticWallpaper?.Dispose();
        _staticWallpaper = null;
        _staticPath = null;
    }

    public ID2D1Bitmap? GetBitmap(IntPtr hwnd) =>
        _caps.TryGetValue(hwnd, out var cap) && !cap.IsClosed ? cap.Bitmap : null;

    private readonly Dictionary<IntPtr, string> _appKeys = new();
    private readonly Dictionary<string, ID2D1Bitmap?> _snapCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _snapSaved = new(StringComparer.Ordinal);

    public ID2D1Bitmap? GetSnapshot(string appKey)
    {
        if (_snapCache.TryGetValue(appKey, out var cached)) return cached;
        ID2D1Bitmap? bmp = null;
        string? path = SnapStore.PathIfExists(appKey);
        if (path != null) bmp = LoadD2D(path);
        _snapCache[appKey] = bmp;
        return bmp;
    }

    public void SaveSnapshots()
    {
        var now = DateTime.UtcNow;
        foreach (var (hwnd, cap) in _caps)
        {
            if (!cap.HasContent || cap.IsClosed) continue;
            if (!_appKeys.TryGetValue(hwnd, out var appKey)) continue;

            if (_snapSaved.TryGetValue(appKey, out var when) && (now - when).TotalSeconds < 1.5) continue;

            using var bmp = cap.ReadGoodBitmap();
            if (bmp == null) continue;
            SnapStore.Save(appKey, bmp);
            _snapSaved[appKey] = now;

            if (_snapCache.Remove(appKey, out var old)) old?.Dispose();
        }
    }

    private ID2D1Bitmap? LoadD2D(string path)
    {
        try
        {
            using var bmp = new System.Drawing.Bitmap(path);
            var rect = new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            try
            {
                var props = new BitmapProperties1(new Vortice.DCommon.PixelFormat(
                    Vortice.DXGI.Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
                    96f, 96f, BitmapOptions.None);
                return _d2d.CreateBitmap(new Vortice.Mathematics.SizeI(bmp.Width, bmp.Height),
                    data.Scan0, (uint)data.Stride, props);
            }
            finally { bmp.UnlockBits(data); }
        }
        catch (Exception ex) { Core.Log.Ex("LoadD2D snapshot", ex); return null; }
    }

    public float GetContentFade(IntPtr hwnd) =>
    _caps.TryGetValue(hwnd, out var cap) ? cap.ContentFade : 1f;

    public void PauseAll()
    {
        if (WindowCapture.BorderRemovable) return;
        foreach (var cap in _caps.Values) cap.Pause();
        _wallpaper?.Pause();
    }

    public void ResumeAll()
    {
        if (WindowCapture.BorderRemovable) return;
        foreach (var cap in _caps.Values) cap.Resume();
        _wallpaper?.Resume();
    }

    public void RefreshAll()
    {
        foreach (var cap in _caps.Values)
        {
            if (cap.IsClosed || !cap.HasContent) cap.Refresh();
            else cap.Resume();
        }
    }

    public void ResetHeal() => _healAttempts.Clear();

    public void Clear()
    {
        foreach (var cap in _caps.Values) cap.Dispose();
        _caps.Clear();
    }

    public void Dispose()
    {
        Clear();
        ClearWallpaper();
        ClearStaticWallpaper();
        foreach (var b in _snapCache.Values) b?.Dispose();
        _snapCache.Clear();
        _iconCache.Dispose();
        try { _rtDevice?.Dispose(); } catch { }
    }
}
