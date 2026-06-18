using System.Numerics;
using PrinceWM.Core;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using Vortice.DCommon;
using AlphaMode = Vortice.DXGI.AlphaMode;
using DcAlphaMode = Vortice.DCommon.AlphaMode;
using FontStyle = Vortice.DirectWrite.FontStyle;
using FeatureLevel = Vortice.Direct3D.FeatureLevel;

namespace PrinceWM.Render;

internal sealed class Renderer : IDisposable
{
    private readonly IntPtr _hwnd;

    private ID3D11Device _d3dDevice = null!;
    private ID3D11DeviceContext _d3dContext = null!;
    private IDXGISwapChain1 _swapChain = null!;
    private ID2D1Factory1 _d2dFactory = null!;
    private ID2D1Device _d2dDevice = null!;
    private ID2D1DeviceContext _d2d = null!;
    private ID2D1Bitmap1? _target;

    private IDWriteFactory _dwrite = null!;
    private IDWriteTextFormat _captionFormat = null!;
    private IDWriteTextFormat _hintFormat = null!;
    private IDWriteTextFormat _badgeFormat = null!;
    private IDWriteTextFormat _titleFormat = null!;

    private ID2D1SolidColorBrush _bg = null!;
    private ID2D1SolidColorBrush _dot = null!;
    private ID2D1SolidColorBrush _shadow = null!;
    private ID2D1SolidColorBrush _placeholder = null!;
    private ID2D1SolidColorBrush _border = null!;
    private ID2D1SolidColorBrush _borderSel = null!;
    private ID2D1SolidColorBrush _glow = null!;
    private ID2D1SolidColorBrush _text = null!;
    private ID2D1SolidColorBrush _textDim = null!;
    private ID2D1SolidColorBrush _red = null!;
    private ID2D1SolidColorBrush _badgeBg = null!;
    private ID2D1SolidColorBrush _tint = null!;
    private ID2D1SolidColorBrush _noteBg = null!;
    private ID2D1SolidColorBrush _noteEdge = null!;
    private ID2D1SolidColorBrush _dyn = null!;
    private IDWriteTextFormat _noteFormat = null!;

    private readonly Dictionary<string, ID2D1Bitmap> _pinImages = new();

    public const int ToolButtonCount = 8;
    private const float BtnSize = 46f, BtnGap = 9f;
    private readonly Vector2[] _btnSpring = new Vector2[ToolButtonCount + 1];
    private ID2D1Bitmap1? _toolGrab;
    private int _toolGrabW, _toolGrabH;
    private bool _blurBroken;
    private Vortice.Direct2D1.Effects.GaussianBlur? _blur;
    private ID2D1StrokeStyle? _roundStroke;

    private int _width, _height;
    private Theme _theme;

    private float _safeTop, _safeBottom;

    private readonly Dictionary<string, Vector2> _hoverSprings = new();
    private float[] _lifts = Array.Empty<float>();

    public ID3D11Device D3DDevice => _d3dDevice;
    public ID3D11DeviceContext D3DContext => _d3dContext;
    public ID2D1DeviceContext D2D => _d2d;

    public void SetSafeArea(float top, float bottom)
    {
        _safeTop = MathF.Max(0f, top);
        _safeBottom = MathF.Max(0f, bottom);
    }

    public System.Drawing.RectangleF ArrowRect =>
    new(_width - 52f, (_safeTop + (_height - _safeBottom)) * 0.5f - 32f, 34f, 64f);

    public Renderer(IntPtr hwnd, int width, int height, Theme theme)
    {
        _hwnd = hwnd;
        _theme = theme;
        _width = Math.Max(1, width);
        _height = Math.Max(1, height);
        CreateDeviceResources();
    }

    private void CreateDeviceResources()
    {
        FeatureLevel[] levels = { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 };

        D3D11.D3D11CreateDevice(
            (IDXGIAdapter?)null,
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            levels,
            out _d3dDevice!).CheckError();
        _d3dContext = _d3dDevice.ImmediateContext;

        using var dxgiDevice = _d3dDevice.QueryInterface<IDXGIDevice>();

        _d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>(Vortice.Direct2D1.FactoryType.SingleThreaded);
        _d2dDevice = _d2dFactory.CreateDevice(dxgiDevice);
        _d2d = _d2dDevice.CreateDeviceContext(DeviceContextOptions.None);

        _d2d.TextAntialiasMode = Vortice.Direct2D1.TextAntialiasMode.Grayscale;

        using var adapter = dxgiDevice.GetAdapter();
        using var factory = adapter.GetParent<IDXGIFactory2>();

        var desc = new SwapChainDescription1
        {
            Width = (uint)_width,
            Height = (uint)_height,
            Format = Format.B8G8R8A8_UNorm,
            Stereo = false,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = 2,
            Scaling = Scaling.Stretch,

            SwapEffect = SwapEffect.Discard,
            AlphaMode = AlphaMode.Ignore,
            Flags = SwapChainFlags.None,
        };
        _swapChain = factory.CreateSwapChainForHwnd(_d3dDevice, _hwnd, desc);

        CreateSizeResources();
        CreateBrushesAndText();
    }

    private void CreateSizeResources()
    {
        _d2d.Target = null;
        _target?.Dispose();
        _target = null;

        using var backBuffer = _swapChain.GetBuffer<IDXGISurface>(0);
        var props = new BitmapProperties1(
            new PixelFormat(Format.B8G8R8A8_UNorm, DcAlphaMode.Ignore),
            96f, 96f,
            BitmapOptions.Target | BitmapOptions.CannotDraw);
        _target = _d2d.CreateBitmapFromDxgiSurface(backBuffer, props);
        _d2d.Target = _target;
    }

    private void CreateBrushesAndText()
    {
        _dwrite = DWrite.DWriteCreateFactory<IDWriteFactory>();
        _captionFormat = _dwrite.CreateTextFormat("Segoe UI", null, FontWeight.SemiBold, FontStyle.Normal,
            FontStretch.Normal, 18f, "en-us");
        _captionFormat.TextAlignment = TextAlignment.Center;
        _hintFormat = _dwrite.CreateTextFormat("Segoe UI", null, FontWeight.Normal, FontStyle.Normal,
            FontStretch.Normal, 13f, "en-us");
        _hintFormat.TextAlignment = TextAlignment.Center;
        _badgeFormat = _dwrite.CreateTextFormat("Segoe UI", null, FontWeight.Bold, FontStyle.Normal,
            FontStretch.Normal, 12f, "en-us");
        _badgeFormat.TextAlignment = TextAlignment.Center;
        _badgeFormat.ParagraphAlignment = ParagraphAlignment.Center;
        _titleFormat = _dwrite.CreateTextFormat("Segoe UI", null, FontWeight.SemiBold, FontStyle.Normal,
            FontStretch.Normal, 12.5f, "en-us");
        _titleFormat.TextAlignment = TextAlignment.Center;
        _titleFormat.ParagraphAlignment = ParagraphAlignment.Center;
        _titleFormat.WordWrapping = WordWrapping.NoWrap;

        _shadow = _d2d.CreateSolidColorBrush(new Color4(0f, 0f, 0f, 0.45f));
        _placeholder = _d2d.CreateSolidColorBrush(new Color4(0.14f, 0.15f, 0.18f, 1f));
        _text = _d2d.CreateSolidColorBrush(new Color4(0.93f, 0.95f, 0.98f, 1f));
        _textDim = _d2d.CreateSolidColorBrush(new Color4(0.62f, 0.66f, 0.74f, 1f));
        _red = _d2d.CreateSolidColorBrush(new Color4(0.92f, 0.28f, 0.30f, 1f));
        _badgeBg = _d2d.CreateSolidColorBrush(new Color4(0.10f, 0.11f, 0.14f, 0.92f));
        _noteBg = _d2d.CreateSolidColorBrush(new Color4(0.05f, 0.06f, 0.08f, 0.74f));
        _noteEdge = _d2d.CreateSolidColorBrush(new Color4(1f, 1f, 1f, 0.13f));
        _dyn = _d2d.CreateSolidColorBrush(new Color4(1f, 1f, 1f, 1f));
        _noteFormat = _dwrite.CreateTextFormat("Segoe UI", null, FontWeight.Normal, FontStyle.Normal,
            FontStretch.Normal, 15f, "en-us");
        _noteFormat.TextAlignment = TextAlignment.Leading;
        _noteFormat.ParagraphAlignment = ParagraphAlignment.Near;
        _noteFormat.WordWrapping = WordWrapping.Wrap;

        BuildThemeBrushes();

        _roundStroke = _d2dFactory.CreateStrokeStyle(new StrokeStyleProperties
        {
            StartCap = CapStyle.Round,
            EndCap = CapStyle.Round,
            DashCap = CapStyle.Round,
            LineJoin = Vortice.Direct2D1.LineJoin.Round,
            DashStyle = Vortice.Direct2D1.DashStyle.Solid,
            MiterLimit = 10f,
            DashOffset = 0f,
        });
    }

    private void BuildThemeBrushes()
    {
        _bg?.Dispose();
        _dot?.Dispose();
        _border?.Dispose();
        _borderSel?.Dispose();
        _glow?.Dispose();
        _tint?.Dispose();

        _bg = _d2d.CreateSolidColorBrush(Theme.ToColor4(_theme.Background));
        _dot = _d2d.CreateSolidColorBrush(Theme.ToColor4(_theme.Dot, 0.55f));
        _border = _d2d.CreateSolidColorBrush(Theme.ToColor4(_theme.Border));
        _borderSel = _d2d.CreateSolidColorBrush(Theme.ToColor4(_theme.Accent));
        _glow = _d2d.CreateSolidColorBrush(Theme.ToColor4(_theme.Accent, 0.18f));
        _tint = _d2d.CreateSolidColorBrush(Theme.ToColor4(_theme.TintColor));
    }

    public void ApplyTheme(Theme theme)
    {
        _theme = theme;
        BuildThemeBrushes();
    }

    public void Resize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == _width && height == _height) return;
        _width = width;
        _height = height;

        _d2d.Target = null;
        _target?.Dispose();
        _target = null;
        _swapChain.ResizeBuffers(2, (uint)_width, (uint)_height, Format.B8G8R8A8_UNorm, SwapChainFlags.None);
        CreateSizeResources();
    }

    public Func<IntPtr, float>? ContentFadeProvider;

    public static System.Drawing.RectangleF CloseButtonRect(float sx, float sy, float sw, float sh)
    {
        const float sz = 22f;
        return new System.Drawing.RectangleF(sx + sw - sz, sy, sz, sz);
    }

    public System.Drawing.RectangleF LiftedTileScreenRect(Camera cam, IReadOnlyList<WindowItem> items, int i)
    {
        WindowItem it = items[i];
        float lift = i < _lifts.Length ? _lifts[i] : 1f;
        float gx = it.WorldSize.X * (lift - 1f) * 0.5f;
        float gy = it.WorldSize.Y * (lift - 1f) * 0.5f;
        Vector2 tl = cam.WorldToScreen(it.WorldPos - new Vector2(gx, gy));
        Vector2 br = cam.WorldToScreen(it.WorldPos + it.WorldSize + new Vector2(gx, gy));
        return new System.Drawing.RectangleF(tl.X, tl.Y, br.X - tl.X, br.Y - tl.Y);
    }

    public Func<WindowItem, ID2D1Bitmap?>? IconProvider;

    public Func<WindowItem, ID2D1Bitmap?>? SnapshotProvider;

    public void Render(Camera camera, IReadOnlyList<WindowItem> items, int selectedIndex,
        Func<IntPtr, ID2D1Bitmap?> bitmapProvider, float commitFade = 0f, float globalOpacity = 1f,
        ID2D1Bitmap? wallpaper = null, int hoverIndex = -1, bool hoverClose = false, float dt = 0f,
        IReadOnlyList<Pin>? pins = null, int hoverPinIndex = -1, bool hoverPinClose = false,
        string? editingPinId = null, string? resizePinId = null, DrawState? draw = null,
        int snapTargetIndex = -1)
    {
        StepHoverSprings(items, hoverIndex, commitFade, dt);
        if (draw != null) StepButtonSprings(draw, dt);

        _d2d.BeginDraw();
        _d2d.Transform = Matrix3x2.Identity;
        _d2d.Clear(Theme.ToColor4(_theme.Background));

        if (_theme.UseWallpaper && wallpaper != null)
            DrawWallpaper(wallpaper);

        if (_theme.ShowDots)
            DrawDotGrid(camera, (1f - commitFade * 0.9f) * globalOpacity);

        _d2d.Transform = camera.WorldMatrix;
        float otherOpacity = (1f - commitFade) * globalOpacity;

        if (draw?.Strokes != null && (draw.Strokes.Count > 0 || draw.InProgress != null))
            DrawStrokesWorld(draw.Strokes, draw.InProgress, camera.Zoom, otherOpacity);

        int highlightIndex = hoverIndex >= 0 ? hoverIndex : selectedIndex;
        for (int i = 0; i < items.Count; i++)
            if (i != selectedIndex)
                DrawTile(items[i], i == highlightIndex, camera.Zoom, bitmapProvider(items[i].Hwnd), otherOpacity, Fade(items[i].Hwnd), _lifts[i]);
        if (selectedIndex >= 0 && selectedIndex < items.Count)
        {

            float ct = Math.Clamp(commitFade / 0.55f, 0f, 1f);
            float highlightFade = 1f - (ct * ct * (3f - 2f * ct));
            float cc = Math.Clamp(commitFade / 0.9f, 0f, 1f);
            float cornerScale = 1f - (cc * cc * (3f - 2f * cc));
            DrawTile(items[selectedIndex], selectedIndex == highlightIndex, camera.Zoom, bitmapProvider(items[selectedIndex].Hwnd), globalOpacity, Fade(items[selectedIndex].Hwnd), _lifts[selectedIndex], highlightFade, cornerScale);
        }

        if (snapTargetIndex >= 0 && snapTargetIndex < items.Count)
            DrawSnapTarget(items[snapTargetIndex], camera.Zoom, otherOpacity);

        if (pins != null && pins.Count > 0)
            DrawPinsWorld(camera, pins, editingPinId, resizePinId, CaretOn(), otherOpacity);

        _d2d.Transform = Matrix3x2.Identity;
        DrawTileChrome(camera, items, hoverIndex, hoverClose, (1f - commitFade) * globalOpacity);
        if (pins != null && pins.Count > 0)
            DrawPinChrome(camera, pins, hoverPinIndex, hoverPinClose, (1f - commitFade) * globalOpacity);
        DrawOverlayText(items, selectedIndex, (1f - commitFade) * globalOpacity);
        DrawArrow((1f - commitFade) * globalOpacity);
        if (draw != null) DrawToolbar(draw, (1f - commitFade) * globalOpacity);

        _d2d.EndDraw();
        if (_pendingShot != null) { TrySaveScreenshot(_pendingShot); _pendingShot = null; }
        _swapChain.Present(1, PresentFlags.None);
    }

    private string? _pendingShot;

    public void RequestScreenshot(string path) => _pendingShot = path;

    private void TrySaveScreenshot(string path)
    {
        try
        {
            using var backbuf = _swapChain.GetBuffer<ID3D11Texture2D>(0);
            var d = backbuf.Description;
            d.BindFlags = BindFlags.None;
            d.CPUAccessFlags = CpuAccessFlags.Read;
            d.Usage = ResourceUsage.Staging;
            d.MiscFlags = ResourceOptionFlags.None;
            using var staging = _d3dDevice.CreateTexture2D(d);
            _d3dContext.CopyResource(staging, backbuf);

            var map = _d3dContext.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                int w = (int)d.Width, h = (int)d.Height;

                using var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
                var rect = new System.Drawing.Rectangle(0, 0, w, h);
                var bd = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly,
                    System.Drawing.Imaging.PixelFormat.Format32bppRgb);
                try
                {
                    var row = new byte[w * 4];
                    for (int y = 0; y < h; y++)
                    {
                        IntPtr src = map.DataPointer + y * (int)map.RowPitch;
                        System.Runtime.InteropServices.Marshal.Copy(src, row, 0, w * 4);
                        System.Runtime.InteropServices.Marshal.Copy(row, 0, bd.Scan0 + y * bd.Stride, w * 4);
                    }
                }
                finally { bmp.UnlockBits(bd); }

                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                try { System.Windows.Forms.Clipboard.SetImage(bmp); } catch { }
            }
            finally { _d3dContext.Unmap(staging, 0); }
        }
        catch (Exception ex) { Core.Log.Ex("Screenshot", ex); }
    }

    private void DrawWallpaper(ID2D1Bitmap wallpaper)
    {
        var size = wallpaper.Size;
        if (size.Width < 1 || size.Height < 1) return;

        float scale = MathF.Max(_width / size.Width, _height / size.Height);
        float ox = (_width - size.Width * scale) * 0.5f;
        float oy = (_height - size.Height * scale) * 0.5f;

        _blur ??= new Vortice.Direct2D1.Effects.GaussianBlur(_d2d);
        _blur.SetInput(0, wallpaper, true);
        _blur.StandardDeviation = _theme.BlurAmount;

        _d2d.Transform = Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation(ox, oy);
        _d2d.DrawImage(_blur);
        _d2d.Transform = Matrix3x2.Identity;

        if (_theme.TintStrength > 0)
        {
            _tint.Opacity = _theme.TintStrength / 100f;
            _d2d.FillRectangle(new Rect(0, 0, _width, _height), _tint);
            _tint.Opacity = 1f;
        }
    }

    private void DrawTileChrome(Camera cam, IReadOnlyList<WindowItem> items, int hoverIndex, bool hoverClose, float opacity)
    {
        if (opacity <= 0.01f) return;

        for (int i = 0; i < items.Count; i++)
        {
            WindowItem it = items[i];
            float lift = i < _lifts.Length ? _lifts[i] : 1f;
            float gx = it.WorldSize.X * (lift - 1f) * 0.5f;
            float gy = it.WorldSize.Y * (lift - 1f) * 0.5f;
            Vector2 tl = cam.WorldToScreen(it.WorldPos - new Vector2(gx, gy));
            Vector2 br = cam.WorldToScreen(it.WorldPos + it.WorldSize + new Vector2(gx, gy));
            float sw = br.X - tl.X, sh = br.Y - tl.Y;

            if (sw < 110f || sh < 72f) continue;

            ID2D1Bitmap? icon = IconProvider?.Invoke(it);
            if (icon != null)
            {
                float isz = 24f;
                var irect = new Rect(tl.X + 9f, tl.Y + 9f, isz, isz);
                var isize = icon.Size;
                ((ID2D1RenderTarget)_d2d).DrawBitmap(icon, irect, opacity,
                    BitmapInterpolationMode.Linear, new Rect(0f, 0f, isize.Width, isize.Height));

                if (it.StackCount > 1)
                {
                    string n = it.StackCount.ToString();
                    float bsz = 17f;
                    var bc = new Vector2(irect.X + isz - 3f, irect.Y + isz - 3f);
                    var brect = new Rect(bc.X - bsz * 0.5f, bc.Y - bsz * 0.5f, bsz, bsz);
                    _borderSel.Opacity = opacity;
                    _d2d.FillEllipse(new Ellipse(bc, bsz * 0.5f + 1f, bsz * 0.5f + 1f), _badgeBg);
                    _d2d.FillEllipse(new Ellipse(bc, bsz * 0.5f, bsz * 0.5f), _borderSel);
                    _text.Opacity = opacity;
                    _d2d.DrawText(n, _badgeFormat, brect, _text, DrawTextOptions.None);
                    _borderSel.Opacity = 1f; _text.Opacity = 1f;
                }
            }
            else if (it.StackCount > 1)
            {
                string n = it.StackCount.ToString();
                float bw = MathF.Max(24f, 14f + n.Length * 9f), bh = 24f;
                var brect = new Rect(tl.X + 8f, tl.Y + 8f, bw, bh);
                var rr = new RoundedRectangle { Rect = brect, RadiusX = bh * 0.5f, RadiusY = bh * 0.5f };
                _badgeBg.Opacity = opacity; _d2d.FillRoundedRectangle(rr, _badgeBg);
                _borderSel.Opacity = opacity; _d2d.DrawRoundedRectangle(rr, _borderSel, 1.4f);
                _text.Opacity = opacity; _d2d.DrawText(n, _badgeFormat, brect, _text, DrawTextOptions.None);
                _badgeBg.Opacity = 1f; _borderSel.Opacity = 1f; _text.Opacity = 1f;
            }

            if (i == hoverIndex)
            {
                var cr = CloseButtonRect(tl.X, tl.Y, sw, sh);
                var box = new RoundedRectangle { Rect = new Rect(cr.X, cr.Y, cr.Width, cr.Height), RadiusX = 4f, RadiusY = 4f };
                if (hoverClose) { _red.Opacity = opacity; _d2d.FillRoundedRectangle(box, _red); _red.Opacity = 1f; }
                else { _badgeBg.Opacity = opacity * 0.6f; _d2d.FillRoundedRectangle(box, _badgeBg); _badgeBg.Opacity = 1f; }
                float cx = cr.X + cr.Width * 0.5f, cy = cr.Y + cr.Height * 0.5f, d = 4f;
                _text.Opacity = opacity;
                _d2d.DrawLine(new Vector2(cx - d, cy - d), new Vector2(cx + d, cy + d), _text, 1.7f, _roundStroke);
                _d2d.DrawLine(new Vector2(cx - d, cy + d), new Vector2(cx + d, cy - d), _text, 1.7f, _roundStroke);
                _text.Opacity = 1f;
            }
        }
    }

    private void DrawArrow(float opacity)
    {
        if (opacity <= 0.01f) return;
        var r = ArrowRect;
        float cx = r.X + r.Width * 0.5f;
        float cy = r.Y + r.Height * 0.5f;

        var p1 = new System.Numerics.Vector2(cx + 3f, cy - 6f);
        var p2 = new System.Numerics.Vector2(cx - 3f, cy);
        var p3 = new System.Numerics.Vector2(cx + 3f, cy + 6f);

        _text.Opacity = opacity * 0.10f;
        _d2d.DrawLine(p1, p2, _text, 8f, _roundStroke);
        _d2d.DrawLine(p2, p3, _text, 8f, _roundStroke);
        _text.Opacity = opacity * 0.18f;
        _d2d.DrawLine(p1, p2, _text, 4.5f, _roundStroke);
        _d2d.DrawLine(p2, p3, _text, 4.5f, _roundStroke);

        _text.Opacity = opacity * 0.92f;
        _d2d.DrawLine(p1, p2, _text, 2f, _roundStroke);
        _d2d.DrawLine(p2, p3, _text, 2f, _roundStroke);
        _text.Opacity = 1f;
    }

    private void DrawDotGrid(Camera camera, float opacity)
    {
        _dot.Opacity = opacity;

        float baseSpacing = Math.Clamp(_theme.DotSpacing, 40, 200);
        float spacing = baseSpacing;
        while (spacing * camera.Zoom < 26f) spacing *= 2f;
        while (spacing * camera.Zoom > 90f) spacing *= 0.5f;

        Vector2 topLeft = camera.ScreenToWorld(Vector2.Zero);
        Vector2 bottomRight = camera.ScreenToWorld(new Vector2(_width, _height));

        float startX = MathF.Floor(topLeft.X / spacing) * spacing;
        float startY = MathF.Floor(topLeft.Y / spacing) * spacing;
        float radius = Math.Clamp(_theme.DotSize, 6, 50) / 10f;

        for (float wx = startX; wx <= bottomRight.X; wx += spacing)
        {
            for (float wy = startY; wy <= bottomRight.Y; wy += spacing)
            {
                Vector2 s = camera.WorldToScreen(new Vector2(wx, wy));
                _d2d.FillEllipse(new Ellipse(new Vector2(s.X, s.Y), radius, radius), _dot);
            }
        }
    }

    private float Fade(IntPtr hwnd) => ContentFadeProvider?.Invoke(hwnd) ?? 1f;

    private void StepHoverSprings(IReadOnlyList<WindowItem> items, int hoverIndex, float commitFade, float dt)
    {
        if (_lifts.Length != items.Count) _lifts = new float[items.Count];
        dt = Math.Clamp(dt, 0f, 0.05f);
        float hoverScale = 1f + _theme.HoverLift / 1000f;
        const float stiffness = 320f, damping = 17f;

        var live = new HashSet<string>();
        for (int i = 0; i < items.Count; i++)
        {
            string key = items[i].AppKey;
            live.Add(key);
            float target = (i == hoverIndex && commitFade <= 0f) ? 1f : 0f;
            _hoverSprings.TryGetValue(key, out Vector2 s);
            s.Y += ((target - s.X) * stiffness - s.Y * damping) * dt;
            s.X += s.Y * dt;
            _hoverSprings[key] = s;
            _lifts[i] = 1f + (hoverScale - 1f) * s.X;
        }

        if (_hoverSprings.Count > items.Count + 16)
            foreach (var k in _hoverSprings.Keys.Where(k => !live.Contains(k)).ToList())
                _hoverSprings.Remove(k);
    }

    private readonly System.Diagnostics.Stopwatch _caretClock = System.Diagnostics.Stopwatch.StartNew();
    private bool CaretOn() => (_caretClock.ElapsedMilliseconds / 530) % 2 == 0;

    public float MeasureNoteTextHeight(string text, float contentWidth)
    {
        if (string.IsNullOrEmpty(text)) text = " ";
        using var layout = _dwrite.CreateTextLayout(text, _noteFormat, MathF.Max(10f, contentWidth), 100000f);
        return layout.Metrics.Height;
    }

    private ID2D1Bitmap? GetPinImage(string file)
    {
        if (_pinImages.TryGetValue(file, out var cached)) return cached;
        try
        {
            string path = System.IO.Path.Combine(Core.PinStore.ImagesDir, file);
            if (!System.IO.File.Exists(path)) return null;
            using var bmp = new System.Drawing.Bitmap(path);
            var rect = new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            try
            {
                var props = new BitmapProperties1(
                    new PixelFormat(Format.B8G8R8A8_UNorm, DcAlphaMode.Premultiplied), 96f, 96f, BitmapOptions.None);
                var d2dbmp = _d2d.CreateBitmap(new SizeI(bmp.Width, bmp.Height), data.Scan0, (uint)data.Stride, props);
                _pinImages[file] = d2dbmp;
                return d2dbmp;
            }
            finally { bmp.UnlockBits(data); }
        }
        catch (Exception ex) { Core.Log.Ex("GetPinImage", ex); return null; }
    }

    private void DrawPinsWorld(Camera cam, IReadOnlyList<Pin> pins, string? editingId, string? resizeId,
    bool caretOn, float opacity)
    {
        if (opacity <= 0.003f) return;
        float z = MathF.Max(cam.Zoom, 0.001f);

        foreach (var pin in pins)
        {
            var rect = new Rect(pin.X, pin.Y, pin.W, pin.H);
            float rad = pin.Kind == PinKind.Note ? 12f : 8f;

            if (_theme.WindowShadows)
            {
                float sh = 14f;
                var sr = new RoundedRectangle
                {
                    Rect = new Rect(rect.X - sh * 0.3f, rect.Y + sh * 0.4f, rect.Width + sh * 0.6f, rect.Height + sh * 0.6f),
                    RadiusX = rad + 6f,
                    RadiusY = rad + 6f,
                };
                _shadow.Opacity = opacity * 0.8f;
                _d2d.FillRoundedRectangle(sr, _shadow);
                _shadow.Opacity = 1f;
            }

            var rr = new RoundedRectangle { Rect = rect, RadiusX = rad, RadiusY = rad };

            if (pin.Kind == PinKind.Image && pin.ImageFile != null)
            {
                var img = GetPinImage(pin.ImageFile);
                if (img != null)
                {
                    using var geo = _d2dFactory.CreateRoundedRectangleGeometry(rr);
                    _d2d.PushLayer(new LayerParameters1
                    {
                        ContentBounds = rect,
                        GeometricMask = geo,
                        Opacity = 1f,
                        MaskAntialiasMode = AntialiasMode.PerPrimitive,
                        MaskTransform = Matrix3x2.Identity,
                        LayerOptions = LayerOptions1.None,
                    }, null!);
                    var sz = img.Size;
                    ((ID2D1RenderTarget)_d2d).DrawBitmap(img, rect, opacity, BitmapInterpolationMode.Linear,
                        new Rect(0f, 0f, sz.Width, sz.Height));
                    _d2d.PopLayer();
                }
                else { _placeholder.Opacity = opacity; _d2d.FillRoundedRectangle(rr, _placeholder); _placeholder.Opacity = 1f; }
                _noteEdge.Opacity = opacity; _d2d.DrawRoundedRectangle(rr, _noteEdge, 1.2f / z); _noteEdge.Opacity = 1f;
            }
            else
            {
                _noteBg.Opacity = opacity; _d2d.FillRoundedRectangle(rr, _noteBg); _noteBg.Opacity = 1f;
                _noteEdge.Opacity = opacity; _d2d.DrawRoundedRectangle(rr, _noteEdge, 1.2f / z); _noteEdge.Opacity = 1f;

                float pad = 12f;
                var textRect = new Rect(rect.X + pad, rect.Y + pad, rect.Width - 2f * pad, rect.Height - 2f * pad);
                bool editing = pin.Id == editingId;
                if (pin.Text.Length > 0 || editing)
                {
                    string shown = editing && caretOn ? pin.Text + "|" : pin.Text;
                    _text.Opacity = opacity;
                    _d2d.DrawText(shown, _noteFormat, textRect, _text, DrawTextOptions.Clip);
                    _text.Opacity = 1f;
                }
                else
                {
                    _textDim.Opacity = opacity * 0.55f;
                    _d2d.DrawText("Note", _noteFormat, textRect, _textDim, DrawTextOptions.Clip);
                    _textDim.Opacity = 1f;
                }
            }

            if (pin.Locked)
            {
                float dr = 4f / z;
                _borderSel.Opacity = opacity;
                _d2d.FillEllipse(new Ellipse(new Vector2(rect.X + 10f / z, rect.Y + 10f / z), dr, dr), _borderSel);
                _borderSel.Opacity = 1f;
            }

            if (pin.Id == resizeId)
            {
                _borderSel.Opacity = opacity;
                _d2d.DrawRoundedRectangle(rr, _borderSel, 2f / z);
                float hs = 14f / z;
                var handle = new Rect(rect.X + rect.Width - hs, rect.Y + rect.Height - hs, hs, hs);
                _d2d.FillRectangle(handle, _borderSel);
                _borderSel.Opacity = 1f;
            }
        }
    }

    private float ToolbarY => _height - _safeBottom - BtnSize - 50f;

    public System.Drawing.RectangleF DrawToggleRect => new(18f, _height - _safeBottom - BtnSize - 12f, BtnSize, BtnSize);

    public System.Drawing.RectangleF ToolButtonRect(int i)
    {
        float totalW = ToolButtonCount * BtnSize + (ToolButtonCount - 1) * BtnGap;
        float x0 = (_width - totalW) * 0.5f;
        return new System.Drawing.RectangleF(x0 + i * (BtnSize + BtnGap), ToolbarY, BtnSize, BtnSize);
    }

    private static Vector2 SpringTo(Vector2 s, float target, float dt)
    {

        float a = (target - s.X) * 320f - s.Y * 26f;
        s.Y += a * dt;
        s.X += s.Y * dt;
        return s;
    }

    private void StepButtonSprings(DrawState d, float dt)
    {
        dt = MathF.Min(dt, 0.05f);
        for (int i = 0; i < ToolButtonCount; i++)
            _btnSpring[i] = SpringTo(_btnSpring[i], d.Active && d.HoverButton == i ? 1f : 0f, dt);
        _btnSpring[ToolButtonCount] = SpringTo(_btnSpring[ToolButtonCount], d.HoverToggle ? 1f : 0f, dt);
    }

    private void DrawStrokesWorld(IReadOnlyList<Stroke> strokes, Stroke? inProgress, float zoom, float opacity)
    {
        if (opacity <= 0.003f) return;
        float z = MathF.Max(zoom, 0.001f);
        foreach (var s in strokes) DrawOneStroke(s, z, opacity);
        if (inProgress != null) DrawOneStroke(inProgress, z, opacity);
    }

    private void DrawOneStroke(Stroke s, float z, float opacity)
    {
        if (s.Count == 0) return;
        SetDyn(s.Color, opacity);
        float w = MathF.Max(0.5f, s.Thickness);

        switch (s.Kind)
        {
            case StrokeKind.Free:
                for (int i = 1; i < s.Count; i++)
                    _d2d.DrawLine(s.At(i - 1), s.At(i), _dyn, w, _roundStroke);
                if (s.Count == 1) _d2d.FillEllipse(new Ellipse(s.At(0), w * 0.5f, w * 0.5f), _dyn);
                break;
            case StrokeKind.Line:
                _d2d.DrawLine(s.At(0), s.At(s.Count - 1), _dyn, w, _roundStroke);
                break;
            case StrokeKind.Rect:
                {
                    var (x, y, bw, bh) = s.Bounds();
                    var rect = new Rect(x, y, bw, bh);
                    if (s.Fill >= 0) { SetDyn(s.Fill, opacity); _d2d.FillRectangle(rect, _dyn); SetDyn(s.Color, opacity); }
                    _d2d.DrawRectangle(rect, _dyn, w);
                    break;
                }
            case StrokeKind.Ellipse:
                {
                    var (x, y, bw, bh) = s.Bounds();
                    var el = new Ellipse(new Vector2(x + bw * 0.5f, y + bh * 0.5f), bw * 0.5f, bh * 0.5f);
                    if (s.Fill >= 0) { SetDyn(s.Fill, opacity); _d2d.FillEllipse(el, _dyn); SetDyn(s.Color, opacity); }
                    _d2d.DrawEllipse(el, _dyn, w);
                    break;
                }
        }
    }

    private void SetDyn(int rgb, float alpha) => _dyn.Color = Theme.ToColor4(rgb, alpha);

    private void DrawToolbar(DrawState d, float opacity)
    {
        if (opacity <= 0.01f || !_theme.ShowPaintButton) return;

        ID2D1Image? blurred = TryGrabBlur();

        DrawButton(DrawToggleRect, _btnSpring[ToolButtonCount].X, d.Active, opacity, blurred,
            (cx, cy, r) => DrawToolIcon(-1, cx, cy, r, d));

        if (!d.Active) return;

        for (int i = 0; i < ToolButtonCount; i++)
        {
            bool selected = i < 6 && (int)d.Tool == i;
            DrawButton(ToolButtonRect(i), _btnSpring[i].X, selected, opacity, blurred,
                (cx, cy, r) => DrawToolIcon(i, cx, cy, r, d));
        }
    }

    private ID2D1Image? TryGrabBlur()
    {
        if (_blurBroken || _width <= 0 || _height <= 0) return null;
        try
        {
            if (_toolGrab == null || _toolGrabW != _width || _toolGrabH != _height)
            {
                _toolGrab?.Dispose();
                var props = new BitmapProperties1(
                    new PixelFormat(Format.B8G8R8A8_UNorm, DcAlphaMode.Premultiplied), 96f, 96f, BitmapOptions.None);
                _toolGrab = _d2d.CreateBitmap(new SizeI(_width, _height), IntPtr.Zero, 0, props);
                _toolGrabW = _width; _toolGrabH = _height;
            }
            _toolGrab.CopyFromRenderTarget(new System.Drawing.Point(0, 0), _d2d,
                new System.Drawing.Rectangle(0, 0, _width, _height));
            _blur ??= new Vortice.Direct2D1.Effects.GaussianBlur(_d2d);
            _blur.SetInput(0, _toolGrab, true);
            _blur.StandardDeviation = 16f;
            return _blur.Output;
        }
        catch (Exception ex)
        {
            Core.Log.Ex("ToolbarBlur", ex);
            _blurBroken = true;
            return null;
        }
    }

    private void DrawButton(System.Drawing.RectangleF rect, float lift, bool selected, float opacity,
        ID2D1Image? blurred, Action<float, float, float> icon)
    {
        float grow = 4f * lift;
        var r = new Rect(rect.X - grow, rect.Y - grow, rect.Width + 2f * grow, rect.Height + 2f * grow);
        var rr = new RoundedRectangle { Rect = r, RadiusX = 12f, RadiusY = 12f };

        if (blurred != null)
        {
            using var geo = _d2dFactory.CreateRoundedRectangleGeometry(rr);
            _d2d.PushLayer(new LayerParameters1
            {
                ContentBounds = r,
                GeometricMask = geo,
                Opacity = 1f,
                MaskAntialiasMode = AntialiasMode.PerPrimitive,
                MaskTransform = Matrix3x2.Identity,
                LayerOptions = LayerOptions1.None,
            }, null!);
            _d2d.DrawImage(blurred);
            _d2d.PopLayer();
        }

        _dyn.Color = new Color4(0.06f, 0.07f, 0.10f, 0.34f * opacity);
        _d2d.FillRoundedRectangle(rr, _dyn);
        _noteEdge.Opacity = opacity; _d2d.DrawRoundedRectangle(rr, _noteEdge, 1.2f); _noteEdge.Opacity = 1f;
        if (selected)
        {
            _borderSel.Opacity = opacity; _d2d.DrawRoundedRectangle(rr, _borderSel, 2.2f); _borderSel.Opacity = 1f;
        }
        icon((float)(r.X + r.Width * 0.5f), (float)(r.Y + r.Height * 0.5f), (float)(r.Width * 0.5f));
    }

    private void DrawToolIcon(int i, float cx, float cy, float r, DrawState d)
    {
        float u = r * 0.54f;
        float lw = MathF.Max(2.3f, r * 0.16f);
        int ink = 0xF2F6FB;
        _dyn.Color = Theme.ToColor4(ink, 1f);

        switch (i)
        {
            case -1:
            case 0:
                {
                    var tip = new Vector2(cx - u, cy + u);
                    var nape = new Vector2(cx + u * 0.45f, cy - u * 0.45f);
                    var cap = new Vector2(cx + u, cy - u);
                    _d2d.DrawLine(new Vector2(tip.X + lw * 0.6f, tip.Y - lw * 0.6f), nape, _dyn, lw * 1.25f, _roundStroke);
                    _dyn.Color = Theme.ToColor4(0xFF6B6B, 1f);
                    _d2d.DrawLine(nape, cap, _dyn, lw * 1.25f, _roundStroke);
                    _dyn.Color = Theme.ToColor4(0xFFC857, 1f);
                    _d2d.FillEllipse(new Ellipse(tip, lw * 1.05f, lw * 1.05f), _dyn);
                    break;
                }
            case 1:
                {
                    var a = new Vector2(cx - u, cy + u);
                    var b = new Vector2(cx + u, cy - u);
                    _d2d.DrawLine(a, b, _dyn, lw, _roundStroke);
                    _d2d.FillEllipse(new Ellipse(a, lw * 0.9f, lw * 0.9f), _dyn);
                    _d2d.FillEllipse(new Ellipse(b, lw * 0.9f, lw * 0.9f), _dyn);
                    break;
                }
            case 2:
                _d2d.DrawRoundedRectangle(new RoundedRectangle { Rect = new Rect(cx - u, cy - u * 0.76f, u * 2f, u * 1.52f), RadiusX = 4f, RadiusY = 4f }, _dyn, lw);
                break;
            case 3:
                _d2d.DrawEllipse(new Ellipse(new Vector2(cx, cy), u, u * 0.82f), _dyn, lw);
                break;
            case 4:
                {
                    var p1 = new Vector2(cx - u * 0.95f, cy - u * 0.35f);
                    var p2 = new Vector2(cx + u * 0.95f, cy - u * 0.6f);
                    var p3 = new Vector2(cx + u * 0.6f, cy + u);
                    var p4 = new Vector2(cx - u * 0.55f, cy + u * 0.85f);
                    _d2d.DrawLine(p1, p2, _dyn, lw, _roundStroke);
                    _d2d.DrawLine(p2, p3, _dyn, lw, _roundStroke);
                    _d2d.DrawLine(p3, p4, _dyn, lw, _roundStroke);
                    _d2d.DrawLine(p4, p1, _dyn, lw, _roundStroke);

                    _d2d.DrawLine(new Vector2(cx - u * 0.3f, cy - u * 0.5f), new Vector2(cx, cy - u * 1.1f), _dyn, lw * 0.8f, _roundStroke);
                    _d2d.DrawLine(new Vector2(cx, cy - u * 1.1f), new Vector2(cx + u * 0.4f, cy - u * 0.55f), _dyn, lw * 0.8f, _roundStroke);

                    SetDyn(d.Color, 1f);
                    _d2d.FillEllipse(new Ellipse(new Vector2(cx + u * 0.95f, cy + u * 0.35f), lw, lw * 1.35f), _dyn);
                    break;
                }
            case 5:
                {
                    var topF = new[]
                    {
                    new Vector2(cx - u * 0.7f, cy - u * 0.1f),
                    new Vector2(cx + u * 0.4f, cy - u * 0.7f),
                    new Vector2(cx + u, cy - u * 0.25f),
                    new Vector2(cx - u * 0.1f, cy + u * 0.35f),
                };
                    FillQuad(topF, Theme.ToColor4(ink, 1f));
                    var body = new[]
                    {
                    new Vector2(cx - u * 0.7f, cy - u * 0.1f),
                    new Vector2(cx - u * 0.1f, cy + u * 0.35f),
                    new Vector2(cx - u * 0.1f, cy + u),
                    new Vector2(cx - u * 0.7f, cy + u * 0.55f),
                };
                    FillQuad(body, Theme.ToColor4(0xFF8FA3, 1f));
                    var body2 = new[]
                    {
                    new Vector2(cx - u * 0.1f, cy + u * 0.35f),
                    new Vector2(cx + u, cy - u * 0.25f),
                    new Vector2(cx + u, cy + u * 0.4f),
                    new Vector2(cx - u * 0.1f, cy + u),
                };
                    FillQuad(body2, Theme.ToColor4(0xE0708A, 1f));
                    break;
                }
            case 6:
                {
                    var sq = new Rect(cx - u, cy - u, u * 2f, u * 2f);
                    SetDyn(d.Color, 1f);
                    _d2d.FillRoundedRectangle(new RoundedRectangle { Rect = sq, RadiusX = 6f, RadiusY = 6f }, _dyn);
                    _dyn.Color = Theme.ToColor4(0xFFFFFF, 0.6f);
                    _d2d.DrawRoundedRectangle(new RoundedRectangle { Rect = sq, RadiusX = 6f, RadiusY = 6f }, _dyn, 1.5f);
                    break;
                }
            case 7:
                {
                    _dyn.Color = Theme.ToColor4(ink, 1f);
                    _d2d.FillEllipse(new Ellipse(new Vector2(cx - u * 0.6f, cy + u * 0.2f), u * 0.18f, u * 0.18f), _dyn);
                    _d2d.FillEllipse(new Ellipse(new Vector2(cx, cy), u * 0.32f, u * 0.32f), _dyn);
                    _d2d.FillEllipse(new Ellipse(new Vector2(cx + u * 0.62f, cy - u * 0.25f), u * 0.5f, u * 0.5f), _dyn);
                    break;
                }
        }
    }

    private void FillQuad(Vector2[] q, Color4 color)
    {
        using var geo = _d2dFactory.CreatePathGeometry();
        using var sink = geo.Open();
        sink.BeginFigure(q[0], FigureBegin.Filled);
        sink.AddLines(new[] { q[1], q[2], q[3] });
        sink.EndFigure(FigureEnd.Closed);
        sink.Close();
        _dyn.Color = color;
        _d2d.FillGeometry(geo, _dyn);
    }

    private void DrawPinChrome(Camera cam, IReadOnlyList<Pin> pins, int hoverIndex, bool hoverClose, float opacity)
    {
        if (opacity <= 0.01f || hoverIndex < 0 || hoverIndex >= pins.Count) return;
        var pin = pins[hoverIndex];
        Vector2 tl = cam.WorldToScreen(pin.Pos);
        Vector2 br = cam.WorldToScreen(pin.Pos + pin.SizeV);
        float sw = br.X - tl.X, sh = br.Y - tl.Y;
        if (sw < 40f || sh < 30f) return;

        var cr = CloseButtonRect(tl.X, tl.Y, sw, sh);
        var box = new RoundedRectangle { Rect = new Rect(cr.X, cr.Y, cr.Width, cr.Height), RadiusX = 4f, RadiusY = 4f };
        if (hoverClose) { _red.Opacity = opacity; _d2d.FillRoundedRectangle(box, _red); _red.Opacity = 1f; }
        else { _badgeBg.Opacity = opacity * 0.6f; _d2d.FillRoundedRectangle(box, _badgeBg); _badgeBg.Opacity = 1f; }
        float cx = cr.X + cr.Width * 0.5f, cy = cr.Y + cr.Height * 0.5f, d = 4f;
        _text.Opacity = opacity;
        _d2d.DrawLine(new Vector2(cx - d, cy - d), new Vector2(cx + d, cy + d), _text, 1.7f, _roundStroke);
        _d2d.DrawLine(new Vector2(cx - d, cy + d), new Vector2(cx + d, cy - d), _text, 1.7f, _roundStroke);
        _text.Opacity = 1f;
    }

    private void DrawSnapTarget(WindowItem it, float zoom, float opacity)
    {
        var rect = new Rect(it.WorldPos.X, it.WorldPos.Y, it.WorldSize.X, it.WorldSize.Y);
        float r = _theme.CornerRadius;
        var rr = new RoundedRectangle { Rect = rect, RadiusX = r, RadiusY = r };

        _glow.Opacity = opacity * 0.6f;
        _d2d.FillRoundedRectangle(rr, _glow);
        _borderSel.Opacity = opacity;
        _d2d.DrawRoundedRectangle(rr, _borderSel, 3.5f / MathF.Max(zoom, 0.001f));

        float midX = it.WorldPos.X + it.WorldSize.X * 0.5f;
        _d2d.DrawLine(new Vector2(midX, it.WorldPos.Y + 4f), new Vector2(midX, it.WorldPos.Y + it.WorldSize.Y - 4f),
            _borderSel, 2f / MathF.Max(zoom, 0.001f));

        _glow.Opacity = 1f;
        _borderSel.Opacity = 1f;
    }

    private static bool AspectClose(ID2D1Bitmap bmp, Rect rect)
    {
        var size = bmp.Size;
        if (size.Width < 1 || size.Height < 1) return false;
        float ta = rect.Width / MathF.Max(1f, rect.Height);
        float ba = size.Width / MathF.Max(1f, size.Height);
        float ratio = ba / MathF.Max(0.001f, ta);
        return ratio > 0.75f && ratio < 1.34f;
    }

    private void DrawTileBitmap(ID2D1Bitmap bmp, Rect rect, RoundedRectangle rounded, float alpha)
    {

        var size = bmp.Size;
        float ta = rect.Width / MathF.Max(1f, rect.Height);
        float ba = size.Width / MathF.Max(1f, size.Height);
        Rect src;
        if (ba > ta) { float w = size.Height * ta; src = new Rect((size.Width - w) * 0.5f, 0f, w, size.Height); }
        else { float h = size.Width / ta; src = new Rect(0f, (size.Height - h) * 0.5f, size.Width, h); }

        if (rounded.RadiusX <= 0.5f)
        {
            _d2d.PushAxisAlignedClip(rect, AntialiasMode.PerPrimitive);
            ((ID2D1RenderTarget)_d2d).DrawBitmap(bmp, rect, alpha, BitmapInterpolationMode.Linear, src);
            _d2d.PopAxisAlignedClip();
            return;
        }

        using var geo = _d2dFactory.CreateRoundedRectangleGeometry(rounded);
        _d2d.PushLayer(new LayerParameters1
        {
            ContentBounds = rect,
            GeometricMask = geo,
            Opacity = 1f,
            MaskAntialiasMode = AntialiasMode.PerPrimitive,
            MaskTransform = Matrix3x2.Identity,
            LayerOptions = LayerOptions1.None,
        }, null!);

        ((ID2D1RenderTarget)_d2d).DrawBitmap(bmp, rect, alpha, BitmapInterpolationMode.Linear, src);
        _d2d.PopLayer();
    }

    private void DrawTile(WindowItem it, bool highlight, float zoom, ID2D1Bitmap? content,
        float opacity, float contentFade, float lift = 1f, float highlightFade = 1f, float cornerScale = 1f)
    {
        if (opacity <= 0.003f) return;

        _shadow.Opacity = opacity;
        _placeholder.Opacity = opacity;
        _border.Opacity = opacity;
        _borderSel.Opacity = opacity;
        _glow.Opacity = opacity;

        float gx = it.WorldSize.X * (lift - 1f) * 0.5f;
        float gy = it.WorldSize.Y * (lift - 1f) * 0.5f;
        var rect = new Rect(it.WorldPos.X - gx, it.WorldPos.Y - gy, it.WorldSize.X * lift, it.WorldSize.Y * lift);

        float r = _theme.CornerRadius * cornerScale;

        if (_theme.WindowShadows)
        {
            float sh = 14f;
            var shadowRect = new RoundedRectangle
            {
                Rect = new Rect(rect.X - sh * 0.3f, rect.Y + sh * 0.4f, rect.Width + sh * 0.6f, rect.Height + sh * 0.6f),
                RadiusX = r + 6f,
                RadiusY = r + 6f,
            };
            _d2d.FillRoundedRectangle(shadowRect, _shadow);
        }

        var rounded = new RoundedRectangle { Rect = rect, RadiusX = r, RadiusY = r };

        _d2d.FillRoundedRectangle(rounded, _placeholder);

        var snap = SnapshotProvider?.Invoke(it);
        bool liveReady = content != null && AspectClose(content, rect);
        bool useLive = content != null && (liveReady || snap == null);

        if (snap != null && (!useLive || contentFade < 0.999f))
            DrawTileBitmap(snap, rect, rounded, opacity);

        if (useLive && contentFade > 0.003f)
            DrawTileBitmap(content!, rect, rounded, opacity * contentFade);

        if (highlight && _theme.GlowIntensity > 0 && highlightFade > 0.001f)
        {

            float z = MathF.Max(zoom, 0.001f);
            float step = 6f / z;
            float maxA = opacity * (_theme.GlowIntensity / 100f) * 0.6f * highlightFade;
            for (int k = 0; k < 5; k++)
            {
                float c = (k + 0.5f) * step;
                var gr = new RoundedRectangle
                {
                    Rect = new Rect(rect.X - c, rect.Y - c, rect.Width + 2f * c, rect.Height + 2f * c),
                    RadiusX = r + c,
                    RadiusY = r + c,
                };
                _borderSel.Opacity = maxA * (1f - k / 5f);
                _d2d.DrawRoundedRectangle(gr, _borderSel, step);
            }
            _borderSel.Opacity = opacity;
        }

        if ((highlight || _theme.ShowTileOutline) && highlightFade > 0.001f)
        {
            float baseW = Math.Clamp(_theme.BorderThickness, 4, 40) / 10f;
            float bw = (highlight ? baseW * 2f : baseW) / MathF.Max(zoom, 0.001f);
            var brush = highlight ? _borderSel : _border;

            brush.Opacity = opacity * highlightFade;
            _d2d.DrawRoundedRectangle(rounded, brush, bw);
        }

        _shadow.Opacity = 1f;
        _placeholder.Opacity = 1f;
        _border.Opacity = 1f;
        _borderSel.Opacity = 1f;
        _glow.Opacity = 1f;
    }

    private void DrawOverlayText(IReadOnlyList<WindowItem> items, int selectedIndex, float opacity)
    {
        if (opacity <= 0.003f) return;
        _text.Opacity = opacity;
        _textDim.Opacity = opacity;

        if (items.Count == 0)
        {
            float midY = _safeTop + (_height - _safeTop - _safeBottom) * 0.5f - 16f;
            _d2d.DrawText("No windows found", _captionFormat,
                new Rect(0, midY, _width, 32f), _textDim, DrawTextOptions.Clip);
        }
        else
        {
            if (_theme.ShowTitles && selectedIndex >= 0 && selectedIndex < items.Count)
                _d2d.DrawText(items[selectedIndex].Title, _captionFormat,
                    new Rect(0, _safeTop + 22f, _width, 30f), _text, DrawTextOptions.Clip);

            if (_theme.ShowHints)
                _d2d.DrawText("drag pan · scroll zoom · W overview · click/Enter switch · right-click for notes & images · drag pins to move · X to remove · Esc",
                    _hintFormat, new Rect(0, _height - _safeBottom - 34f, _width, 24f), _textDim, DrawTextOptions.Clip);
        }

        _text.Opacity = 1f;
        _textDim.Opacity = 1f;
    }

    public void Dispose()
    {
        _captionFormat?.Dispose();
        _hintFormat?.Dispose();
        _badgeFormat?.Dispose();
        _titleFormat?.Dispose();
        _dwrite?.Dispose();
        _bg?.Dispose();
        _dot?.Dispose();
        _shadow?.Dispose();
        _placeholder?.Dispose();
        _border?.Dispose();
        _borderSel?.Dispose();
        _glow?.Dispose();
        _tint?.Dispose();
        _blur?.Dispose();
        _roundStroke?.Dispose();
        _text?.Dispose();
        _textDim?.Dispose();
        _red?.Dispose();
        _badgeBg?.Dispose();
        _noteBg?.Dispose();
        _noteEdge?.Dispose();
        _dyn?.Dispose();
        _toolGrab?.Dispose();
        _noteFormat?.Dispose();
        foreach (var b in _pinImages.Values) b.Dispose();
        _pinImages.Clear();
        _target?.Dispose();
        _d2d?.Dispose();
        _d2dDevice?.Dispose();
        _d2dFactory?.Dispose();
        _swapChain?.Dispose();
        _d3dContext?.Dispose();
        _d3dDevice?.Dispose();
    }
}
