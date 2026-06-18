using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using PrinceWM.Native;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.DCommon;
using AlphaMode = Vortice.DCommon.AlphaMode;

namespace PrinceWM.Capture;

internal sealed class WindowCapture : IDisposable
{
    public IntPtr Hwnd { get; }

    private readonly ID3D11Device _d3d;
    private readonly ID3D11DeviceContext _ctx;
    private readonly ID2D1DeviceContext _d2d;

    private readonly GraphicsCaptureItem _item;
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private readonly IDirect3DDevice _rtDevice;
    private SizeInt32 _poolSize;
    private bool _paused;

    internal static bool BorderRemovable = true;

    private ID3D11Texture2D? _pending;
    private ID3D11Texture2D? _good;
    private ID2D1Bitmap1? _goodBitmap;
    private ID3D11Texture2D? _probe;

    private bool _hasPending, _hasGood, _probePending, _pendingBlack;
    private bool _disposed;
    private bool _closed;
    private int _emptyUpdates;

    public ID2D1Bitmap? Bitmap => _goodBitmap;

    public bool IsClosed => _closed;

    public bool HasContent => _hasGood;

    private long _contentStart;

    public float ContentFade
    {
        get
        {
            if (!_hasGood) return 0f;
            double sec = (Stopwatch.GetTimestamp() - _contentStart) / (double)Stopwatch.Frequency;
            float u = (float)Math.Clamp(sec / 0.12, 0.0, 1.0);
            return 1f - MathF.Pow(1f - u, 3f);
        }
    }

    public bool LooksDead => _emptyUpdates > 40;

    public WindowCapture(IntPtr hwnd, ID3D11Device d3d, ID3D11DeviceContext ctx,
        ID2D1DeviceContext d2d, IDirect3DDevice rtDevice)
    {
        Hwnd = hwnd;
        _d3d = d3d;
        _ctx = ctx;
        _d2d = d2d;

        _rtDevice = rtDevice;
        _item = CaptureInterop.CreateItemForWindow(hwnd);
        SizeInt32 size = _item.Size;
        if (size.Width <= 0) size.Width = 1;
        if (size.Height <= 0) size.Height = 1;
        _poolSize = size;
        _item.Closed += (_, _) => _closed = true;

        StartSession();
    }

    private void StartSession()
    {
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _rtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _poolSize);
        _session = _pool.CreateCaptureSession(_item);

        try { _session.IsBorderRequired = false; } catch { BorderRemovable = false; }
        try { _session.IsCursorCaptureEnabled = false; } catch { }

        _session.StartCapture();

        // Windows 10 only: sessions are torn down and restarted on every open (to drop the yellow
        // capture border, which Win10 can't disable). A fresh session only delivers a frame when
        // the window repaints, so a static already-open window would stay blank. Nudge it to
        // repaint. Win11 keeps its sessions alive, so this isn't needed there - left untouched.
        if (!NativeMethods.IsWindows11)
        {
            try
            {
                NativeMethods.RedrawWindow(Hwnd, IntPtr.Zero, IntPtr.Zero,
                    NativeMethods.RDW_INVALIDATE | NativeMethods.RDW_ERASE | NativeMethods.RDW_ALLCHILDREN);
            }
            catch { }
        }
    }

    public void Pause()
    {
        if (_disposed || _paused) return;
        _paused = true;
        try { _session?.Dispose(); } catch { }
        try { _pool?.Dispose(); } catch { }
        _session = null;
        _pool = null;
    }

    public void Resume()
    {
        if (_disposed || !_paused) return;
        _paused = false;
        if (_closed) return;
        try { StartSession(); } catch (Exception ex) { Core.Log.Ex("Resume capture", ex); }
    }

    public void Refresh()
    {
        if (_disposed) return;
        _paused = false;
        if (_closed) return;
        try { _session?.Dispose(); } catch { }
        try { _pool?.Dispose(); } catch { }
        _session = null;
        _pool = null;
        try { StartSession(); } catch (Exception ex) { Core.Log.Ex("Refresh capture", ex); }
    }

    public void Update()
    {
        if (_disposed || _paused || _pool == null) return;

        if (_probePending)
        {
            _pendingBlack = ReadProbeBlack();
            _probePending = false;
        }

        if (NativeMethods.IsIconic(Hwnd) || NativeMethods.IsWindowCloaked(Hwnd)) return;

        if (_hasPending && _pending != null && _good != null && !_pendingBlack)
        {
            _ctx.CopyResource(_good, _pending);
            if (!_hasGood) _contentStart = Stopwatch.GetTimestamp();
            _hasGood = true;
            _emptyUpdates = 0;
        }
        else if (_hasPending && _pendingBlack && !_hasGood)
        {
            _emptyUpdates++;
        }

        Direct3D11CaptureFrame? latest = null;
        while (true)
        {
            Direct3D11CaptureFrame? f = _pool.TryGetNextFrame();
            if (f == null) break;
            latest?.Dispose();
            latest = f;
        }
        if (latest == null)
        {

            if (!_hasGood) _emptyUpdates++;
            return;
        }

        using (latest)
        {

            SizeInt32 content = latest.ContentSize;
            IntPtr texPtr = CaptureInterop.GetTexturePointer(latest.Surface);
            using var src = new ID3D11Texture2D(texPtr);
            Texture2DDescription sd = src.Description;

            int cw = Math.Min(content.Width, (int)sd.Width);
            int ch = Math.Min(content.Height, (int)sd.Height);
            if (cw > 0 && ch > 0)
            {
                EnsureTextures(cw, ch, sd.Format);
                var box = new Vortice.Mathematics.Box(0, 0, 0, cw, ch, 1);
                _ctx.CopySubresourceRegion(_pending!, 0, 0, 0, 0, src, 0, box);
                _hasPending = true;
                IssueProbe(cw, ch);
            }

            if (content.Width > 0 && content.Height > 0 &&
                (content.Width != _poolSize.Width || content.Height != _poolSize.Height))
            {
                _poolSize = content;
                _pool.Recreate(_rtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, content);
            }
        }
    }

    private void IssueProbe(int cw, int ch)
    {
        if (_probe == null || _pending == null) return;
        int sx = Math.Clamp(cw / 2 - 4, 0, Math.Max(0, cw - 8));
        int sy = Math.Clamp(ch / 2 - 4, 0, Math.Max(0, ch - 8));
        int w = Math.Min(8, cw), h = Math.Min(8, ch);
        var box = new Vortice.Mathematics.Box(sx, sy, 0, sx + w, sy + h, 1);
        _ctx.CopySubresourceRegion(_probe, 0, 0, 0, 0, _pending, 0, box);
        _probePending = true;
    }

    private bool ReadProbeBlack()
    {
        if (_probe == null) return false;
        MappedSubresource map = _ctx.Map(_probe, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            long sum = 0;
            for (int y = 0; y < 8; y++)
            {
                IntPtr row = map.DataPointer + y * (int)map.RowPitch;
                for (int x = 0; x < 8; x++)
                {
                    byte b = Marshal.ReadByte(row, x * 4 + 0);
                    byte g = Marshal.ReadByte(row, x * 4 + 1);
                    byte r = Marshal.ReadByte(row, x * 4 + 2);
                    sum += (int)(0.299f * r + 0.587f * g + 0.114f * b);
                }
            }
            return sum / 64.0 < 6.0;
        }
        finally
        {
            _ctx.Unmap(_probe, 0);
        }
    }

    private void EnsureTextures(int width, int height, Format format)
    {
        if (_pending != null &&
            _pending.Description.Width == (uint)width &&
            _pending.Description.Height == (uint)height)
            return;

        _goodBitmap?.Dispose(); _goodBitmap = null;
        _pending?.Dispose(); _pending = null;
        _good?.Dispose(); _good = null;
        _probe?.Dispose(); _probe = null;
        _hasPending = _hasGood = _probePending = _pendingBlack = false;

        var desc = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None,
        };
        _pending = _d3d.CreateTexture2D(desc);
        _good = _d3d.CreateTexture2D(desc);

        using var surface = _good.QueryInterface<IDXGISurface>();
        var props = new BitmapProperties1(
            new PixelFormat(Format.B8G8R8A8_UNorm, AlphaMode.Ignore), 96f, 96f, BitmapOptions.None);
        _goodBitmap = _d2d.CreateBitmapFromDxgiSurface(surface, props);

        var probeDesc = new Texture2DDescription
        {
            Width = 8,
            Height = 8,
            MipLevels = 1,
            ArraySize = 1,
            Format = format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None,
        };
        _probe = _d3d.CreateTexture2D(probeDesc);
    }

    public System.Drawing.Bitmap? ReadGoodBitmap()
    {
        if (_disposed || !_hasGood || _good == null) return null;
        try
        {
            var desc = _good.Description;
            desc.BindFlags = BindFlags.None;
            desc.CPUAccessFlags = CpuAccessFlags.Read;
            desc.Usage = ResourceUsage.Staging;
            desc.MiscFlags = ResourceOptionFlags.None;
            using var staging = _d3d.CreateTexture2D(desc);
            _ctx.CopyResource(staging, _good);

            var map = _ctx.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                int w = (int)desc.Width, h = (int)desc.Height;
                if (w <= 0 || h <= 0) return null;

                var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
                var bd = bmp.LockBits(new System.Drawing.Rectangle(0, 0, w, h),
                    System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
                try
                {
                    var row = new byte[w * 4];
                    for (int y = 0; y < h; y++)
                    {
                        Marshal.Copy(map.DataPointer + y * (int)map.RowPitch, row, 0, w * 4);
                        Marshal.Copy(row, 0, bd.Scan0 + y * bd.Stride, w * 4);
                    }
                }
                finally { bmp.UnlockBits(bd); }
                return bmp;
            }
            finally { _ctx.Unmap(staging, 0); }
        }
        catch (Exception ex) { Core.Log.Ex("ReadGoodBitmap", ex); return null; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _session?.Dispose(); } catch { }
        try { _pool?.Dispose(); } catch { }
        _goodBitmap?.Dispose();
        _pending?.Dispose();
        _good?.Dispose();
        _probe?.Dispose();
    }
}
