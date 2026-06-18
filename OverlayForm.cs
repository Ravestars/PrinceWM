using System.Diagnostics;
using System.Numerics;
using System.Windows.Forms;
using PrinceWM.Capture;
using PrinceWM.Core;
using PrinceWM.Native;
using PrinceWM.Render;

namespace PrinceWM;

internal sealed class OverlayForm : Form
{
    private readonly AltTabHook _hook;
    private readonly Camera _camera = new();
    private Renderer? _renderer;
    private CaptureManager? _captures;
    private readonly NotifyIcon _tray;

    private static readonly Theme AppTheme = ThemeStore.Load();
    private SettingsForm? _settings;

    private List<WindowItem> _items = new();
    private int _selected = -1;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _lastTicks;

    private bool _mouseDown;
    private bool _dragged;
    private Point _lastMouse;
    private Point _downMouse;
    private Vector2 _dragVel;
    private long _lastMoveTicks;
    private const int DragThreshold = 6;

    private bool _open;

    private bool _committing;
    private float _commitElapsed;
    private const float CommitDuration = 0.2f;

    private bool _opening;
    private float _openElapsed;
    private const float OpenDuration = 0.16f;

    private int _tileDrag = -1;
    private HashSet<WindowItem>? _dragCluster;
    private Point _tileDownMouse;
    private int _drillOnClick = -1;
    private int _snapTarget = -1;

    private int _hoverIndex = -1;
    private bool _hoverClose;

    private string? _drillApp;
    private bool Drilled => _drillApp != null;

    private readonly HashSet<IntPtr> _promoted = new();

    private int _quickIndex = -1;

    private float _savedZoom;

    private readonly List<Stroke> _strokes = DrawStore.Load();
    private readonly DrawState _draw = new();
    private Stroke? _drawing;
    private bool _erasing;
    private bool _drawPanning;
    private int _drawSizeIdx = 1;
    private static readonly float[] DrawSizes = { 2f, 4f, 8f, 16f, 28f };

    private readonly List<Pin> _pins = PinStore.Load();
    private int _hoverPin = -1;
    private bool _hoverPinClose;
    private int _pinDrag = -1;
    private bool _pinDragged;
    private Vector2 _pinDragOffset;
    private Point _pinDownMouse;
    private Pin? _editingPin;

    private static readonly Dictionary<string, Vector2> SavedPositions = LayoutStore.Load();
    private static readonly Dictionary<string, Vector2> SavedSizes = SizeStore.Load();

    private static readonly List<IntPtr> Mru = new();
    private readonly NativeMethods.WinEventDelegate _foreProc;
    private IntPtr _foreHook;

    private void OnForegroundChanged(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild,
    uint thread, uint time)
    {
        if (idObject != NativeMethods.OBJID_WINDOW || hwnd == IntPtr.Zero) return;
        if (_open) return;
        IntPtr root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        if (root != IntPtr.Zero) BumpMru(root);
    }

    private const float NudgeStep = 20f;

    public OverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = System.Drawing.Color.Black;
        DoubleBuffered = false;

        TopMost = true;
        Text = "PrinceWM";
        KeyPreview = true;

        var appIcon = LoadAppIcon();
        if (appIcon != null) Icon = appIcon;

        _draw.Strokes = _strokes;
        _draw.Size = DrawSizes[_drawSizeIdx];

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque | ControlStyles.UserPaint, true);
        UpdateStyles();

        Application.Idle += OnIdle;

        _hook = new AltTabHook();

        _hook.AltTabPressed += shift => PostToUi(() => OnAltTab(shift));
        _hook.NavPressed += (key, shift) => PostToUi(() => OnNav(key, shift));
        _hook.ScreenshotKey += () => PostToUi(TakeScreenshot);

        _foreProc = OnForegroundChanged;
        _foreHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _foreProc, 0, 0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

        var menu = new ContextMenuStrip();
        menu.Items.Add("PrinceWM").Enabled = false;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Application.Exit());
        _tray = new NotifyIcon
        {
            Icon = appIcon ?? System.Drawing.SystemIcons.Application,
            Text = "PrinceWM - Alt+Tab canvas",
            Visible = true,
            ContextMenuStrip = menu,
        };

        Visible = false;

        var updateTimer = new System.Windows.Forms.Timer { Interval = 4000 };
        updateTimer.Tick += async (_, _) =>
        {
            updateTimer.Stop();
            updateTimer.Dispose();
            await CheckForUpdatesAsync();
        };
        updateTimer.Start();
    }

    private async Task CheckForUpdatesAsync()
    {
        string? tag = await UpdateChecker.GetNewerTagAsync();
        if (tag == null) return;

        var page = new TaskDialogPage
        {
            Caption = "PrinceWM",
            Heading = $"Update available: {tag}",
            Text = "A newer version of PrinceWM is available on GitHub.",
            Icon = TaskDialogIcon.Information,
            Verification = new TaskDialogVerificationCheckBox("Don't notify me about updates again"),
        };
        var download = new TaskDialogButton("Download");
        page.Buttons.Add(download);
        page.Buttons.Add(new TaskDialogButton("Later"));

        TaskDialogButton result;
        try { result = TaskDialog.ShowDialog(page); }
        catch (Exception ex) { Core.Log.Ex("UpdateChecker.ShowDialog", ex); return; }

        if (page.Verification.Checked) UpdateChecker.DisableForever();
        if (result == download)
        {
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(UpdateChecker.ReleasesUrl) { UseShellExecute = true });
            }
            catch (Exception ex) { Core.Log.Ex("UpdateChecker.OpenUrl", ex); }
        }
    }

    private static System.Drawing.Icon? LoadAppIcon()
    {
        try
        {
            string? path = Environment.ProcessPath;
            return path != null ? System.Drawing.Icon.ExtractAssociatedIcon(path) : null;
        }
        catch { return null; }
    }

    protected override CreateParams CreateParams
    {
        get
        {

            const int WS_EX_TOOLWINDOW = 0x00000080;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {

    }

    private Func<IntPtr, Vortice.Direct2D1.ID2D1Bitmap?>? _bitmapProvider;

    private void EnsureRenderer()
    {
        if (_renderer == null)
        {
            _renderer = new Renderer(Handle, ClientSize.Width, ClientSize.Height, AppTheme);
            _renderer.ContentFadeProvider = h => _captures?.GetContentFade(h) ?? 1f;
            _renderer.IconProvider = it => _captures?.GetIcon(it);
            _renderer.SnapshotProvider = it => _captures?.GetSnapshot(it.AppKey);
        }
        _bitmapProvider ??= h => _captures?.GetBitmap(h);
    }

    private readonly HashSet<IntPtr> _closed = new();

    private float _rescanT;
    private float _captureT;
    private float _warmup;
    private HashSet<IntPtr> _lastScanHwnds = new();

    private List<WindowItem> _allWindows = new();

    private static int MruRank(IntPtr h)
    {
        int i = Mru.IndexOf(h);
        return i < 0 ? int.MaxValue : i;
    }

    private void BuildItems()
    {
        var raw = WindowScanner.Scan(Handle, sizeCache: SavedSizes);
        if (_closed.Count > 0) raw.RemoveAll(it => _closed.Contains(it.Hwnd));
        _lastScanHwnds = raw.Select(r => r.Hwnd).ToHashSet();
        _allWindows = new List<WindowItem>(raw);

        if (_drillApp != null)
        {
            var sub = raw.FindAll(it => it.AppKey == _drillApp);

            if (sub.Count >= 1)
            {
                sub.Sort((a, b) => MruRank(a.Hwnd).CompareTo(MruRank(b.Hwnd)));
                ArrangeGrid(sub, _drillCenter);
                _items = sub;
                return;
            }
            _drillApp = null;
        }

        _items = Grouper.Stack(raw, Mru, _promoted);
        LayoutManager.Arrange(_items, SavedPositions, PinBoxes(null));

        var scanRank = new Dictionary<IntPtr, int>(raw.Count);
        for (int i = 0; i < raw.Count; i++) scanRank[raw[i].Hwnd] = i;
        int Rank(WindowItem it)
        {
            int mru = int.MaxValue, scan = int.MaxValue;
            foreach (var h in it.StackHwnds)
            {
                int m = Mru.IndexOf(h);
                if (m >= 0 && m < mru) mru = m;
                int s = scanRank.GetValueOrDefault(h, int.MaxValue);
                if (s < scan) scan = s;
            }
            return mru != int.MaxValue ? mru : Mru.Count + scan;
        }
        _items = _items.OrderBy(Rank).ToList();
    }

    private List<(Vector2 pos, Vector2 size)> Obstacles(object? exclude)
    {
        var list = new List<(Vector2, Vector2)>();
        foreach (var it in _items) if (!ReferenceEquals(it, exclude)) list.Add((it.WorldPos, it.WorldSize));
        foreach (var p in _pins) if (!ReferenceEquals(p, exclude)) list.Add((p.Pos, p.SizeV));
        return list;
    }

    private List<(Vector2 pos, Vector2 size)> PinBoxes(object? exclude)
    {
        var list = new List<(Vector2, Vector2)>();
        foreach (var p in _pins) if (!ReferenceEquals(p, exclude)) list.Add((p.Pos, p.SizeV));
        return list;
    }

    private static void ArrangeGrid(List<WindowItem> items, Vector2 center)
    {
        int n = items.Count;
        if (n == 0) return;

        float cw = 0, ch = 0;
        foreach (var it in items) { cw = MathF.Max(cw, it.WorldSize.X); ch = MathF.Max(ch, it.WorldSize.Y); }
        float gap = LayoutManager.Gap;
        int cols = (int)MathF.Ceiling(MathF.Sqrt(n));
        int rows = (int)MathF.Ceiling(n / (float)cols);
        float totalH = rows * ch + (rows - 1) * gap;
        float y0 = center.Y - totalH * 0.5f;

        int idx = 0;
        for (int r = 0; r < rows && idx < n; r++)
        {
            int inRow = Math.Min(cols, n - idx);
            float totalW = inRow * cw + (inRow - 1) * gap;
            float x0 = center.X - totalW * 0.5f;
            float cellTop = y0 + r * (ch + gap);
            for (int c = 0; c < inRow; c++)
            {
                var it = items[idx++];
                float cellCx = x0 + c * (cw + gap) + cw * 0.5f;
                float cellCy = cellTop + ch * 0.5f;
                it.WorldPos = new Vector2(cellCx - it.WorldSize.X * 0.5f, cellCy - it.WorldSize.Y * 0.5f);
            }
        }
    }

    private Vector2 _drillReturnCenter;
    private float _drillReturnZoom;
    private Vector2 _drillCenter;

    private void EnterDrill(string appKey)
    {

        var stack = _items.FirstOrDefault(it => it.AppKey == appKey);
        _drillReturnCenter = _camera.TargetCenter;
        _drillReturnZoom = _camera.TargetZoom;
        _drillCenter = stack?.WorldCenter ?? _camera.TargetCenter;

        _drillApp = appKey;
        _tileDrag = -1;
        _dragCluster = null;
        _hoverIndex = -1;
        _hoverClose = false;
        BuildItems();
        _captures?.Sync(_allWindows);
        _selected = _items.Count > 0 ? 0 : -1;
        if (_items.Count > 0)
        {

            _camera.FitAll(_items, animate: false);
            Vector2 fitCenter = _camera.Center;
            float fitZoom = _camera.Zoom;
            _camera.SnapTo(_drillReturnCenter, _drillReturnZoom);
            _camera.TweenTo(fitCenter, fitZoom, 0.44f, easeOut: true);
            _opening = true;
            _openElapsed = 0f;
        }
    }

    private void PromoteToCanvas(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        _promoted.Add(hwnd);
        ExitDrill();
    }

    private void RearrangeAll()
    {
        _drillApp = null;

        foreach (var it in _items) SavedPositions.Remove(it.AppKey);
        BuildItems();
        _captures?.Sync(_allWindows);
        _selected = _items.Count > 0 ? 0 : -1;
        _camera.FitAll(_items, animate: true);
    }

    private Pin? _resizing;
    private bool _pinResizeDrag;

    private void PasteAt(Point screen)
    {
        try
        {
            if (!Clipboard.ContainsImage()) return;
            using var img = Clipboard.GetImage();
            if (img == null) return;
            string? file = PinStore.SaveImage(img);
            if (file == null) return;

            float w = img.Width, h = img.Height;
            float scale = MathF.Min(1f, 520f / MathF.Max(w, h));
            w *= scale; h *= scale;
            Vector2 wp = _camera.ScreenToWorld(new Vector2(screen.X, screen.Y));
            var p0 = Collision.Resolve(new Vector2(wp.X - w * 0.5f, wp.Y - h * 0.5f), new Vector2(w, h), Obstacles(null), 10f);
            _pins.Add(new Pin { Kind = PinKind.Image, ImageFile = file, X = p0.X, Y = p0.Y, W = w, H = h });
            PinStore.Save(_pins);
        }
        catch (Exception ex) { Core.Log.Ex("PasteAt", ex); }
    }

    private void NewNoteAt(Point screen)
    {
        float w = 240f, h = 180f;
        Vector2 wp = _camera.ScreenToWorld(new Vector2(screen.X, screen.Y));
        var p0 = Collision.Resolve(new Vector2(wp.X - w * 0.5f, wp.Y - h * 0.5f), new Vector2(w, h), Obstacles(null), 10f);
        var note = new Pin { Kind = PinKind.Note, X = p0.X, Y = p0.Y, W = w, H = h };
        _pins.Add(note);
        _editingPin = note;
        _resizing = null;
        PinStore.Save(_pins);
    }

    private void ToggleDraw()
    {
        _draw.Active = !_draw.Active;
        if (!_draw.Active) { _drawing = null; _erasing = false; _drawPanning = false; }
        _editingPin = null;
    }

    private void UpdateButtonHover(Point p)
    {
        if (!AppTheme.ShowPaintButton) { _draw.HoverToggle = false; _draw.HoverButton = -1; return; }
        _draw.HoverToggle = _renderer != null && _renderer.DrawToggleRect.Contains(p);
        _draw.HoverButton = -1;
        if (_draw.Active && _renderer != null)
            for (int i = 0; i < Render.Renderer.ToolButtonCount; i++)
                if (_renderer.ToolButtonRect(i).Contains(p)) { _draw.HoverButton = i; break; }
    }

    private void ToolbarClick(int i)
    {
        if (i < 6) { _draw.Tool = (DrawTool)i; return; }
        if (i == 6)
        {
            using var dlg = new ColorDialog { Color = FromRgb(_draw.Color), FullOpen = true };
            if (dlg.ShowDialog(this) == DialogResult.OK) _draw.Color = ToRgb(dlg.Color);
            return;
        }

        _drawSizeIdx = (_drawSizeIdx + 1) % DrawSizes.Length;
        _draw.Size = DrawSizes[_drawSizeIdx];
    }

    private Stroke NewStroke()
    {
        var kind = _draw.Tool switch
        {
            DrawTool.Line => StrokeKind.Line,
            DrawTool.Rect => StrokeKind.Rect,
            DrawTool.Ellipse => StrokeKind.Ellipse,
            _ => StrokeKind.Free,
        };
        return new Stroke { Kind = kind, Color = _draw.Color, Thickness = _draw.Size };
    }

    private void HandleDrawDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && _renderer != null)
            for (int i = 0; i < Render.Renderer.ToolButtonCount; i++)
                if (_renderer.ToolButtonRect(i).Contains(e.Location)) { ToolbarClick(i); return; }

        if (e.Button == MouseButtons.Right) { _drawPanning = true; _lastMouse = e.Location; return; }
        if (e.Button != MouseButtons.Left) return;

        Vector2 wp = _camera.ScreenToWorld(new Vector2(e.X, e.Y));
        switch (_draw.Tool)
        {
            case DrawTool.Fill: FillAt(wp); break;
            case DrawTool.Eraser: _erasing = true; EraseAt(wp); break;
            default:
                _drawing = NewStroke();
                _drawing.Add(wp);
                if (_draw.Tool != DrawTool.Pen) _drawing.Add(wp);
                break;
        }
        _lastMouse = e.Location;
    }

    private void HandleDrawMove(MouseEventArgs e)
    {
        if (_drawPanning)
        {
            _camera.PanByScreen(new Vector2(e.X - _lastMouse.X, e.Y - _lastMouse.Y));
            _lastMouse = e.Location;
            return;
        }
        Vector2 wp = _camera.ScreenToWorld(new Vector2(e.X, e.Y));
        if (_drawing != null)
        {
            if (_drawing.Kind == StrokeKind.Free) _drawing.Add(wp);
            else { _drawing.Pts[2] = wp.X; _drawing.Pts[3] = wp.Y; }
        }
        else if (_erasing) EraseAt(wp);
        _lastMouse = e.Location;
    }

    private void HandleDrawUp()
    {
        if (_drawPanning) { _drawPanning = false; return; }
        if (_erasing) { _erasing = false; DrawStore.Save(_strokes); return; }
        if (_drawing == null) return;

        Stroke s = _drawing; _drawing = null;
        var (_, _, w, h) = s.Bounds();
        bool keep = s.Kind == StrokeKind.Free ? s.Count >= 2 : (w > 2f || h > 2f);
        if (keep) { _strokes.Add(s); DrawStore.Save(_strokes); }
    }

    private void FillAt(Vector2 wp)
    {

        for (int i = _strokes.Count - 1; i >= 0; i--)
            if (_strokes[i].EnclosesPoint(wp)) { _strokes[i].Fill = _draw.Color; DrawStore.Save(_strokes); return; }
    }

    private void EraseAt(Vector2 wp)
    {
        float r = MathF.Max(10f, _draw.Size * 2.5f) / MathF.Max(_camera.Zoom, 0.001f);
        _strokes.RemoveAll(s => StrokeNear(s, wp, r));
    }

    private static bool StrokeNear(Stroke s, Vector2 p, float r)
    {
        if (s.Kind is StrokeKind.Free or StrokeKind.Line)
        {
            for (int i = 0; i < s.Count; i++)
                if (Vector2.Distance(s.At(i), p) < r) return true;
            return false;
        }
        var (x, y, w, h) = s.Bounds();
        return p.X >= x - r && p.X <= x + w + r && p.Y >= y - r && p.Y <= y + h + r;
    }

    private static System.Drawing.Color FromRgb(int rgb) =>
        System.Drawing.Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
    private static int ToRgb(System.Drawing.Color c) => (c.R << 16) | (c.G << 8) | c.B;

    private void GrowNoteIfNeeded(Pin note)
    {
        if (_renderer == null || note.Kind != PinKind.Note) return;
        const float pad = 12f;
        string measured = note == _editingPin ? note.Text + "|" : note.Text;
        float textH = _renderer.MeasureNoteTextHeight(measured, note.W - 2f * pad);
        float needed = textH + 2f * pad;
        if (needed > note.H) note.H = needed;
    }

    private void ShowPinMenu(Point p, Pin pin)
    {
        var menu = new ContextMenuStrip
        {
            BackColor = System.Drawing.Color.FromArgb(28, 30, 36),
            ForeColor = System.Drawing.Color.White,
            ShowImageMargin = false,
            Font = new System.Drawing.Font("Segoe UI", 9f),
        };
        if (pin.Kind == PinKind.Note)
            menu.Items.Add("Edit note", null, (_, _) => { _editingPin = pin; _resizing = null; });
        menu.Items.Add(pin.Locked ? "Unpin (allow moving)" : "Pin (lock in place)", null,
            (_, _) => { pin.Locked = !pin.Locked; if (pin.Locked) _resizing = null; PinStore.Save(_pins); });
        if (!pin.Locked)
            menu.Items.Add(_resizing == pin ? "Done resizing" : "Resize", null,
                (_, _) => { _resizing = _resizing == pin ? null : pin; _editingPin = null; });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Remove", null, (_, _) => RemovePin(pin));
        menu.Show(this, p);
    }

    private void ShowCanvasMenu(Point p)
    {
        var menu = new ContextMenuStrip
        {
            BackColor = System.Drawing.Color.FromArgb(28, 30, 36),
            ForeColor = System.Drawing.Color.White,
            ShowImageMargin = false,
            Font = new System.Drawing.Font("Segoe UI", 9f),
        };
        menu.Items.Add("Add note here", null, (_, _) => NewNoteAt(p));
        var paste = (ToolStripMenuItem)menu.Items.Add("Paste image here", null, (_, _) => PasteAt(p));
        bool hasImg = false;
        try { hasImg = Clipboard.ContainsImage(); } catch { }
        paste.Enabled = hasImg;
        menu.Show(this, p);
    }

    private void RemovePin(Pin pin)
    {
        if (pin.Kind == PinKind.Image && pin.OlderThan(TimeSpan.FromHours(12)))
        {
            var r = MessageBox.Show(this,
                "This image has been pinned for over 12 hours. Remove it?",
                "Remove pinned image", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) return;
        }
        _pins.Remove(pin);
        if (pin.Kind == PinKind.Image) PinStore.DeleteImage(pin.ImageFile);
        if (_editingPin == pin) _editingPin = null;
        _hoverPin = -1; _hoverPinClose = false;
        PinStore.Save(_pins);
    }

    private void UpdatePinHover(Point p)
    {
        _hoverPin = -1;
        _hoverPinClose = false;
        for (int i = _pins.Count - 1; i >= 0; i--)
        {
            Vector2 tl = _camera.WorldToScreen(_pins[i].Pos);
            Vector2 br = _camera.WorldToScreen(_pins[i].Pos + _pins[i].SizeV);
            var s = new System.Drawing.RectangleF(tl.X, tl.Y, br.X - tl.X, br.Y - tl.Y);
            if (s.Width < 30f || s.Height < 24f) continue;
            var cr = Render.Renderer.CloseButtonRect(s.X, s.Y, s.Width, s.Height);
            if (cr.Contains(p.X, p.Y)) { _hoverPin = i; _hoverPinClose = true; return; }
            if (s.Contains(p.X, p.Y)) { _hoverPin = i; _hoverPinClose = false; return; }
        }
    }

    private void ExitDrill()
    {
        string? was = _drillApp;
        _drillApp = null;
        _hoverIndex = -1;
        _hoverClose = false;
        BuildItems();
        _captures?.Sync(_allWindows);
        _selected = was != null ? _items.FindIndex(i => i.AppKey == was) : -1;
        if (_selected < 0) _selected = _items.Count > 0 ? 0 : -1;

        if (_selected >= 0)
            _camera.TweenTo(_items[_selected].WorldCenter, _camera.FocusZoom(_items[_selected]), 0.42f, easeOut: true);
        else
            _camera.FitAll(_items, animate: true);
    }

    private void RescanIfChanged()
    {
        if (_committing || _tileDrag >= 0) return;
        var raw = WindowScanner.Scan(Handle, sizeCache: SavedSizes);
        if (_closed.Count > 0) raw.RemoveAll(it => _closed.Contains(it.Hwnd));
        var now = raw.Select(r => r.Hwnd).ToHashSet();
        if (now.SetEquals(_lastScanHwnds)) return;
        RefreshItems();
    }

    private void RefreshItems()
    {
        IntPtr selH = _selected >= 0 && _selected < _items.Count ? _items[_selected].Hwnd : IntPtr.Zero;
        BuildItems();
        _selected = selH != IntPtr.Zero
            ? _items.FindIndex(i => i.Hwnd == selH || i.StackHwnds.Contains(selH))
            : -1;
        if (_selected < 0) _selected = _items.Count > 0 ? 0 : -1;
        _captures?.Sync(_allWindows);
    }

    private void ShowCanvas()
    {

        var vs = SystemInformation.VirtualScreen;
        Bounds = new Rectangle(vs.X, vs.Y, vs.Width, vs.Height - 1);

        _committing = false;
        _commitElapsed = 0f;
        _tileDrag = -1;
        _dragCluster = null;
        _drillOnClick = -1;
        _rescanT = 0f;

        _camera.Viewport = new Vector2(ClientSize.Width, ClientSize.Height);
        bool zoomedOut = false;
        if (_items.Count > 0)
        {
            WindowItem here = _items[0];

            float targetZoom = AppTheme.RememberZoom && _savedZoom > 0.001f
                ? _savedZoom : _camera.FocusZoom(here);

            if (TryFramePerfect(here, out Vector2 c, out float z))
            {
                _camera.SnapTo(c, z);
                _camera.TweenTo(here.WorldCenter, targetZoom, 0.42f, easeOut: true);
                zoomedOut = true;
            }
            else
            {
                _camera.SnapTo(here.WorldCenter, targetZoom);
            }
            _selected = Math.Clamp(_quickIndex, 0, _items.Count - 1);
        }
        else
        {
            _camera.FitAll(_items, animate: false);
            _selected = -1;
        }

        _opening = !zoomedOut;
        _openElapsed = 0f;

        EnsureRenderer();
        _renderer!.Resize(ClientSize.Width, ClientSize.Height);
        ApplySafeArea();

        if (CaptureManager.IsSupported)
        {
            _captures ??= new CaptureManager(_renderer.D3DDevice, _renderer.D3DContext, _renderer.D2D);
            _captures.Sync(_allWindows);
            _captures.RefreshAll();
            _captures.ResetHeal();
            SyncWallpaper();

            _captures.Update();
            _captures.Update();
        }

        Show();
        WindowState = FormWindowState.Normal;

        NativeMethods.SetForegroundWindow(Handle);
        Activate();
        BringToFront();

        NativeMethods.ClipCursor(IntPtr.Zero);

        _lastTicks = _clock.ElapsedTicks;
        _open = true;
        _warmup = 0.7f;
        _hook.OverlayOpen = true;
    }

    private static void BumpMru(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        Mru.Remove(hwnd);
        Mru.Insert(0, hwnd);
        if (Mru.Count > 64) Mru.RemoveRange(64, Mru.Count - 64);
    }

    private void CloseOverlay(bool activateSelection)
    {
        _savedZoom = _camera.Zoom;
        int idx = activateSelection ? _selected : -1;
        IntPtr hwnd = idx >= 0 && idx < _items.Count ? _items[idx].Hwnd : IntPtr.Zero;

        if (hwnd != IntPtr.Zero) { BumpMru(hwnd); WindowScanner.Activate(hwnd); }
        HideOverlay();
    }

    private void BeginCommit(int index)
    {
        if (index < 0 || index >= _items.Count) return;
        _selected = index;
        _savedZoom = _camera.Zoom;
        _committing = true;
        _commitElapsed = 0f;

        WindowItem it = _items[index];
        if (TryFramePerfect(it, out Vector2 center, out float zoom))
            _camera.TweenTo(center, zoom, CommitDuration, easeOut: true);
        else
            _camera.FocusFill(it, CommitDuration);
    }

    private bool TryFramePerfect(WindowItem it, out Vector2 center, out float zoom)
    {
        center = default;
        zoom = 1f;

        NativeMethods.RECT wr;
        if (NativeMethods.IsIconic(it.Hwnd))
        {

            var wp = new NativeMethods.WINDOWPLACEMENT
            { length = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>() };
            if (!NativeMethods.GetWindowPlacement(it.Hwnd, ref wp)) return false;
            wr = wp.rcNormalPosition;
        }

        else if (!NativeMethods.GetExtendedFrameBounds(it.Hwnd, out wr) &&
                 !NativeMethods.GetWindowRect(it.Hwnd, out wr)) return false;

        if (wr.Width <= 0 || wr.Height <= 0 || wr.Left <= -30000 || wr.Top <= -30000) return false;

        float winW = wr.Width, winH = wr.Height;
        var winCenterClient = new Vector2(
            (wr.Left - Bounds.X) + winW * 0.5f,
            (wr.Top - Bounds.Y) + winH * 0.5f);
        zoom = winW / Math.Max(1f, it.WorldSize.X);
        center = it.WorldCenter - (winCenterClient - _camera.Viewport * 0.5f) / zoom;
        return true;
    }

    private void FinalizeCommit()
    {
        IntPtr hwnd = _selected >= 0 && _selected < _items.Count ? _items[_selected].Hwnd : IntPtr.Zero;

        // Raise the target to the top of the Z-order WITHOUT activating it. Activation does a
        // foreground handoff that can block on a busy app (e.g. a game like Garry's Mod that
        // isn't pumping messages), and doing that here would stall the render thread with the
        // last frame frozen on screen. Raising is cheap and non-blocking, so when we hide the
        // overlay the live target window is revealed instantly - the correct window, no freeze.
        if (hwnd != IntPtr.Zero)
        {
            BumpMru(hwnd);
            if (NativeMethods.IsIconic(hwnd)) NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
            NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOP, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        }

        HideOverlay();

        // Now claim focus. This can block on a busy game, but the overlay is already gone and the
        // game is on screen, so the wait is invisible instead of a frozen final frame.
        if (hwnd != IntPtr.Zero) WindowScanner.Activate(hwnd);
    }

    private static void ApplyTunables()
    {
        LayoutManager.Gap = Math.Clamp(AppTheme.TileGap, 8, 240);
        Camera.DurationScale = 100f / Math.Clamp(AppTheme.AnimSpeed, 30, 300);
    }

    private void ApplySafeArea()
    {
        if (_renderer == null) return;
        var screen = Screen.PrimaryScreen;
        if (screen == null) { _renderer.SetSafeArea(0, 0); return; }
        float top = Math.Max(0, screen.WorkingArea.Top - Bounds.Top);
        float bottom = Math.Max(0, Bounds.Bottom - screen.WorkingArea.Bottom);
        _renderer.SetSafeArea(top, bottom);
    }

    private static void ResumeWallpaperEngine(IntPtr weWindow)
    {
        try
        {
            NativeMethods.GetWindowThreadProcessId(weWindow, out uint pid);
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            string? exe = p.MainModule?.FileName;
            if (string.IsNullOrEmpty(exe)) return;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, "-control play")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch { }
    }

    private bool _iconsHidden;

    private void SetDesktopIconsHidden(bool hide)
    {
        if (hide == _iconsHidden) return;
        IntPtr host = NativeMethods.GetDesktopIconHost();
        if (host == IntPtr.Zero) return;
        NativeMethods.ShowWindow(host, hide ? NativeMethods.SW_HIDE : NativeMethods.SW_SHOW);
        _iconsHidden = hide;
    }

    private void SyncWallpaper()
    {
        if (_captures == null) return;
        if (!AppTheme.UseWallpaper)
        {
            _captures.ClearWallpaper();
            _captures.ClearStaticWallpaper();
            return;
        }

        _captures.SetStaticWallpaper(NativeMethods.GetWallpaperPath());

        IntPtr animated = NativeMethods.FindAnimatedWallpaperWindow();
        bool win11 = NativeMethods.IsWindows11;

        if (animated != IntPtr.Zero)
        {

            IntPtr target = win11 ? NativeMethods.GetAncestor(animated, NativeMethods.GA_ROOT) : animated;
            SetDesktopIconsHidden(win11 && !AppTheme.ShowDesktopIcons);
            ResumeWallpaperEngine(animated);
            if (target != IntPtr.Zero) _captures.SetWallpaper(target);
            else _captures.ClearWallpaper();
            return;
        }

        if (AppTheme.ShowDesktopIcons)
        {

            SetDesktopIconsHidden(false);
            IntPtr target = win11 ? NativeMethods.FindWindow("Progman", null) : IntPtr.Zero;
            if (target != IntPtr.Zero) _captures.SetWallpaper(target);
            else _captures.ClearWallpaper();
        }
        else
        {

            _captures.ClearWallpaper();
        }
    }

    private void ToggleSettings()
    {
        if (_settings != null && !_settings.IsDisposed)
        {
            _settings.Close();
            _settings = null;
            return;
        }

        _settings = new SettingsForm(AppTheme);
        _settings.Changed += () =>
        {
            _renderer?.ApplyTheme(AppTheme); ApplyTunables(); SyncWallpaper();
            if (!AppTheme.ShowPaintButton) { _draw.Active = false; _drawing = null; }
        };
        _settings.RearrangeRequested += RearrangeAll;
        _settings.FormClosed += (_, _) => _settings = null;
        _settings.Show(this);
    }

    private void HideOverlay()
    {
        if (_settings != null && !_settings.IsDisposed) { _settings.Close(); _settings = null; }
        _open = false;
        _committing = false;
        _hook.OverlayOpen = false;
        _closed.Clear();
        _drillApp = null;
        _hoverIndex = -1;
        _hoverClose = false;
        _snapTarget = -1;
        _editingPin = null;
        _pinDrag = -1;
        _hoverPin = -1;
        _resizing = null;
        _pinResizeDrag = false;
        _draw.Active = false;
        _drawing = null;
        _erasing = false;
        _drawPanning = false;
        SetDesktopIconsHidden(false);

        Hide();
        _captures?.SaveSnapshots();
        _captures?.PauseAll();
        LayoutStore.Save(SavedPositions);
        SizeStore.Save(SavedSizes);
        PinStore.Save(_pins);
        DrawStore.Save(_strokes);
    }

    private void PostToUi(Action action)
    {
        if (!IsHandleCreated) return;
        try { BeginInvoke(action); } catch { }
    }

    private void OnAltTab(bool shift)
    {

        if (_open) { CycleSelection(shift); return; }

        try
        {
            ApplyTunables();
            BumpMru(NativeMethods.GetForegroundWindow());
            _drillApp = null;
            BuildItems();

            _quickIndex = (shift && _items.Count > 1) ? _items.Count - 1 : 0;
            ShowCanvas();
        }
        catch (Exception ex)
        {

            Core.Log.Write($"EX OpenCanvas: {ex}");
            CloseOverlay(false);
            _tray.BalloonTipTitle = "PrinceWM error";
            _tray.BalloonTipText = ex.Message;
            _tray.ShowBalloonTip(4000);
        }
    }

    private void CycleSelection(bool shift)
    {
        if (_items.Count == 0 || _committing) return;
        _selected = (_selected + (shift ? -1 : 1) + _items.Count) % _items.Count;
        _camera.PanTargetTo(_items[_selected].WorldCenter);
    }

    private void OnNav(NavKey key, bool shift)
    {
        if (!_open) return;

        if (_editingPin != null)
        {
            if (key == NavKey.Enter && shift) { _editingPin.Text += "\n"; GrowNoteIfNeeded(_editingPin); }
            else if (key == NavKey.Enter || key == NavKey.Escape) { _editingPin = null; PinStore.Save(_pins); }
            return;
        }

        if (_resizing != null && key == NavKey.Escape) { _resizing = null; return; }

        if (key == NavKey.Escape)
        {
            if (Drilled) ExitDrill();
            else CloseOverlay(false);
            return;
        }
        if (_committing) return;
        switch (key)
        {
            case NavKey.Enter:
                BeginCommit(_selected);
                break;
            case NavKey.Left:
            case NavKey.Up:
            case NavKey.Right:
            case NavKey.Down:
                if (shift) NudgeWindow(key);
                else MoveSelection(key);
                break;
        }
    }

    private void NudgeWindow(NavKey dir)
    {
        if (Drilled) return;
        if (_selected < 0 || _selected >= _items.Count) return;
        Vector2 d = dir switch
        {
            NavKey.Left => new Vector2(-NudgeStep, 0),
            NavKey.Right => new Vector2(NudgeStep, 0),
            NavKey.Up => new Vector2(0, -NudgeStep),
            NavKey.Down => new Vector2(0, NudgeStep),
            _ => Vector2.Zero,
        };
        _items[_selected].WorldPos += d;
        Remember(_items[_selected]);
    }

    private static void Remember(WindowItem it) =>
        SavedPositions[it.AppKey] = it.Sliding ? it.SlideTarget : it.WorldPos;

    private void UpdateSnapTarget()
    {
        _snapTarget = -1;
        if (!AppTheme.DragToTile) return;
        if (_tileDrag < 0 || _tileDrag >= _items.Count) return;
        var a = _items[_tileDrag];
        float aArea = MathF.Max(1f, a.WorldSize.X * a.WorldSize.Y);
        float best = 0f;
        for (int i = 0; i < _items.Count; i++)
        {
            if (i == _tileDrag) continue;
            var b = _items[i];
            float ox = MathF.Max(0f, MathF.Min(a.WorldPos.X + a.WorldSize.X, b.WorldPos.X + b.WorldSize.X) - MathF.Max(a.WorldPos.X, b.WorldPos.X));
            float oy = MathF.Max(0f, MathF.Min(a.WorldPos.Y + a.WorldSize.Y, b.WorldPos.Y + b.WorldSize.Y) - MathF.Max(a.WorldPos.Y, b.WorldPos.Y));
            float overlap = ox * oy;
            float frac = overlap / MathF.Min(aArea, MathF.Max(1f, b.WorldSize.X * b.WorldSize.Y));
            if (frac > 0.4f && overlap > best) { best = overlap; _snapTarget = i; }
        }
    }

    private void StepTileSlides(float dt)
    {
        float rate = 13f * (Math.Clamp(AppTheme.SettleSpeed, 40, 200) / 100f);
        float k = 1f - MathF.Exp(-dt * rate);
        foreach (var it in _items)
        {
            if (!it.Sliding) continue;
            it.WorldPos = Vector2.Lerp(it.WorldPos, it.SlideTarget, k);
            if ((it.SlideTarget - it.WorldPos).LengthSquared() < 0.25f)
            {
                it.WorldPos = it.SlideTarget;
                it.Sliding = false;
            }
        }
    }

    private void TakeScreenshot()
    {
        if (_renderer == null) return;
        string dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "PrinceWM");
        string path = System.IO.Path.Combine(dir, $"PrinceWM_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        _renderer.RequestScreenshot(path);
        _tray.BalloonTipTitle = "Screenshot copied";
        _tray.BalloonTipText = "On the clipboard, and saved to " + path;
        _tray.ShowBalloonTip(2500);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!_open || _committing) { base.OnKeyDown(e); return; }

        if (_editingPin != null) { base.OnKeyDown(e); return; }

        bool handled = true;
        switch (e.KeyCode)
        {
            case Keys.W:
                _camera.FitAll(_items, animate: true);
                break;
            case Keys.M:
            case Keys.F:
                if (_selected >= 0) _camera.FocusFill(_items[_selected], 0.28f);
                break;
            case Keys.C:
                if (_selected >= 0) _camera.CenterOn(_items[_selected].WorldCenter);
                break;
            case Keys.D0:
            case Keys.NumPad0:
            case Keys.Z:
                _camera.ResetZoom();
                break;
            case Keys.Oemplus:
            case Keys.Add:
                _camera.ZoomStep(1.18f);
                break;
            case Keys.OemMinus:
            case Keys.Subtract:
                _camera.ZoomStep(1f / 1.18f);
                break;
            case Keys.R:
                RearrangeAll();
                break;
            case Keys.F12:
                TakeScreenshot();
                break;
            default:
                handled = false;
                break;
        }

        if (handled) { e.Handled = true; e.SuppressKeyPress = true; }
        else base.OnKeyDown(e);
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (_open && _editingPin != null)
        {
            char c = e.KeyChar;
            if (c == '\b') { if (_editingPin.Text.Length > 0) _editingPin.Text = _editingPin.Text[..^1]; }
            else if (!char.IsControl(c)) _editingPin.Text += c;
            GrowNoteIfNeeded(_editingPin);
            e.Handled = true;
            return;
        }
        base.OnKeyPress(e);
    }

    private void MoveSelection(NavKey dir)
    {
        if (_items.Count == 0) return;
        if (_selected < 0) { _selected = 0; _camera.CenterOn(_items[0].WorldCenter); return; }

        Vector2 from = _items[_selected].WorldCenter;
        int best = -1;
        float bestScore = float.MaxValue;

        for (int i = 0; i < _items.Count; i++)
        {
            if (i == _selected) continue;
            Vector2 d = _items[i].WorldCenter - from;
            bool ok = dir switch
            {
                NavKey.Left => d.X < -1,
                NavKey.Right => d.X > 1,
                NavKey.Up => d.Y < -1,
                NavKey.Down => d.Y > 1,
                _ => false,
            };
            if (!ok) continue;

            float along = dir is NavKey.Left or NavKey.Right ? MathF.Abs(d.X) : MathF.Abs(d.Y);
            float perp = dir is NavKey.Left or NavKey.Right ? MathF.Abs(d.Y) : MathF.Abs(d.X);
            float score = along + perp * 2f;
            if (score < bestScore) { bestScore = score; best = i; }
        }

        if (best >= 0)
        {
            _selected = best;
            _camera.PanTargetTo(_items[_selected].WorldCenter);
        }
    }

    private void OnIdle(object? sender, EventArgs e)
    {

        while (_open && !MessagePending())
            Frame();
    }

    private static bool MessagePending() =>
        NativeMethods.PeekMessage(out _, IntPtr.Zero, 0, 0, NativeMethods.PM_NOREMOVE);

    private System.Drawing.RectangleF TileScreenRect(WindowItem it)
    {
        Vector2 tl = _camera.WorldToScreen(it.WorldPos);
        Vector2 br = _camera.WorldToScreen(it.WorldPos + it.WorldSize);
        return new System.Drawing.RectangleF(tl.X, tl.Y, br.X - tl.X, br.Y - tl.Y);
    }

    private void UpdateHover(Point p)
    {
        _hoverIndex = -1;
        _hoverClose = false;
        if (_renderer == null) return;

        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var s = _renderer.LiftedTileScreenRect(_camera, _items, i);
            if (s.Width < 60f || s.Height < 40f) continue;
            var cr = Render.Renderer.CloseButtonRect(s.X, s.Y, s.Width, s.Height);
            if (cr.Contains(p.X, p.Y)) { _hoverIndex = i; _hoverClose = true; return; }
            if (s.Contains(p.X, p.Y)) { _hoverIndex = i; _hoverClose = false; return; }
        }
    }

    private void CloseTile(WindowItem it)
    {
        IntPtr h = it.Hwnd;
        _closed.Add(h);
        NativeMethods.PostMessage(h, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        _hoverIndex = -1;
        _hoverClose = false;
        RefreshItems();
    }

    private void CloseStack(WindowItem it)
    {
        foreach (IntPtr h in it.StackHwnds)
        {
            _closed.Add(h);
            NativeMethods.PostMessage(h, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }
        _hoverIndex = -1;
        _hoverClose = false;
        RefreshItems();
    }

    private void ShowTileMenu(Point p)
    {
        if (_committing) return;
        int hit = HitTest(p);
        if (hit < 0) return;
        WindowItem it = _items[hit];

        var menu = new ContextMenuStrip
        {
            BackColor = System.Drawing.Color.FromArgb(28, 30, 36),
            ForeColor = System.Drawing.Color.White,
            ShowImageMargin = false,
            Font = new System.Drawing.Font("Segoe UI", 9f),
        };
        menu.Items.Add("Switch to", null, (_, _) => BeginCommit(hit));
        menu.Items.Add("Minimize", null, (_, _) => NativeMethods.ShowWindow(it.Hwnd, NativeMethods.SW_MINIMIZE));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(it.StackCount > 1 ? "Close newest" : "Close", null, (_, _) => CloseTile(it));
        if (it.StackCount > 1)
            menu.Items.Add($"Close all ({it.StackCount})", null, (_, _) => CloseStack(it));
        menu.Show(this, p);
    }

    private void Frame()
    {
        if (!_open || _renderer == null) return;

        long now = _clock.ElapsedTicks;
        float dt = (float)((now - _lastTicks) / (double)Stopwatch.Frequency);
        _lastTicks = now;
        dt = Math.Clamp(dt, 0f, 0.05f);

        if (_committing)
        {
            _commitElapsed += dt;
            if (_commitElapsed >= CommitDuration) { FinalizeCommit(); return; }
        }

        if (_opening)
        {
            _openElapsed += dt;
            if (_openElapsed >= OpenDuration) _opening = false;
        }

        _rescanT += dt;
        if (_rescanT >= 0.35f)
        {
            _rescanT = 0f;
            try { RescanIfChanged(); } catch (Exception ex) { Core.Log.Ex("Rescan", ex); }
        }

        try
        {

            if (_warmup > 0f) _warmup -= dt;
            _captureT += dt;
            if (_committing)
            {

            }
            else if (_camera.IsAnimating)
            {
                if (_warmup > 0f) _captures?.UpdateStaggered(3);
            }
            else if (_captureT >= 1f / 60f)
            {
                _captureT = 0f;
                _captures?.Update();
            }
            _camera.Step(dt);
            StepTileSlides(dt);
            float commitFade = _committing ? Math.Clamp(_commitElapsed / CommitDuration, 0f, 1f) : 0f;
            float openFade = 1f;
            if (_opening)
            {
                float u = Math.Clamp(_openElapsed / OpenDuration, 0f, 1f);
                openFade = 1f - MathF.Pow(1f - u, 3f);
            }

            bool drilled = Drilled;
            _draw.InProgress = _drawing;
            _draw.Strokes = drilled ? null : _strokes;
            _renderer.Render(_camera, _items, _selected, _bitmapProvider!, commitFade, openFade,
                _captures?.WallpaperBitmap, _hoverIndex, _hoverClose, dt,
                drilled ? null : _pins, _hoverPin, _hoverPinClose, _editingPin?.Id, _resizing?.Id, _draw,
                drilled ? -1 : _snapTarget);
        }
        catch (Exception ex)
        {

            Core.Log.Ex("Frame", ex);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (_committing) return;

        if (AppTheme.ShowPaintButton && e.Button == MouseButtons.Left &&
            _renderer != null && _renderer.DrawToggleRect.Contains(e.Location))
        {
            ToggleDraw();
            return;
        }

        if (_renderer != null && _renderer.ArrowRect.Contains(e.Location))
        {
            ToggleSettings();
            return;
        }

        if (_draw.Active) { HandleDrawDown(e); return; }

        if (e.Button == MouseButtons.Right)
        {
            UpdatePinHover(e.Location);
            if (_hoverPin >= 0) { ShowPinMenu(e.Location, _pins[_hoverPin]); return; }
            if (HitTest(e.Location) >= 0) { ShowTileMenu(e.Location); return; }
            ShowCanvasMenu(e.Location);
            return;
        }

        if (e.Button == MouseButtons.Left)
        {
            UpdatePinHover(e.Location);
            if (_hoverPin >= 0)
            {
                Pin pin = _pins[_hoverPin];
                if (_hoverPinClose) { RemovePin(pin); return; }
                if (_resizing == pin) { _pinResizeDrag = true; _lastMouse = e.Location; return; }
                _resizing = null;
                if (pin.Locked) return;
                _pinDrag = _hoverPin;
                _pinDragged = false;
                _pinDownMouse = e.Location;
                _lastMouse = e.Location;
                _pinDragOffset = _camera.ScreenToWorld(new Vector2(e.X, e.Y)) - pin.Pos;
                return;
            }

            if (_editingPin != null) { _editingPin = null; PinStore.Save(_pins); }
            _resizing = null;
        }

        bool alt = (ModifierKeys & Keys.Alt) != 0;
        bool shift = (ModifierKeys & Keys.Shift) != 0;

        if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && alt))
        {
            int hit = HitTest(e.Location);
            if (hit >= 0)
            {

                if (e.Button == MouseButtons.Middle && Drilled)
                {
                    PromoteToCanvas(_items[hit].Hwnd);
                    return;
                }
                if (!Drilled)
                {
                    _tileDrag = hit;
                    _items[hit].Sliding = false;
                    _selected = hit;
                    _lastMouse = e.Location;
                    _tileDownMouse = e.Location;

                    _drillOnClick = (e.Button == MouseButtons.Middle && _items[hit].StackCount > 1) ? hit : -1;

                    _dragCluster = shift ? Snapping.GetCluster(_items[hit], _items) : null;
                }
            }
            return;
        }

        if (e.Button != MouseButtons.Left) return;
        _mouseDown = true;
        _dragged = false;
        _downMouse = e.Location;
        _lastMouse = e.Location;
        _dragVel = Vector2.Zero;
        _lastMoveTicks = _clock.ElapsedTicks;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_committing) return;

        UpdateButtonHover(e.Location);
        if (_draw.Active) { HandleDrawMove(e); return; }

        if (_pinResizeDrag && _resizing != null)
        {
            Vector2 w = _camera.ScreenToWorld(new Vector2(e.X, e.Y));
            _resizing.W = MathF.Max(60f, w.X - _resizing.X);
            _resizing.H = MathF.Max(40f, w.Y - _resizing.Y);
            _lastMouse = e.Location;
            return;
        }

        if (_pinDrag >= 0)
        {
            if (Math.Abs(e.X - _pinDownMouse.X) + Math.Abs(e.Y - _pinDownMouse.Y) > DragThreshold)
                _pinDragged = true;
            if (_pinDragged && _pinDrag < _pins.Count)
            {
                Vector2 w = _camera.ScreenToWorld(new Vector2(e.X, e.Y)) - _pinDragOffset;
                _pins[_pinDrag].X = w.X;
                _pins[_pinDrag].Y = w.Y;
            }
            _lastMouse = e.Location;
            return;
        }

        if (_tileDrag >= 0)
        {

            if (Math.Abs(e.X - _tileDownMouse.X) + Math.Abs(e.Y - _tileDownMouse.Y) <= DragThreshold)
            {
                _lastMouse = e.Location;
                return;
            }
            _drillOnClick = -1;
            var worldDelta = new Vector2(e.X - _lastMouse.X, e.Y - _lastMouse.Y) / _camera.Zoom;
            if (_dragCluster != null)
            {
                foreach (var m in _dragCluster) { m.WorldPos += worldDelta; Remember(m); }
            }
            else
            {
                WindowItem moved = _items[_tileDrag];
                moved.WorldPos += worldDelta;
                Remember(moved);
                UpdateSnapTarget();
            }
            _lastMouse = e.Location;
            return;
        }

        if (!_mouseDown) { UpdateHover(e.Location); UpdatePinHover(e.Location); return; }

        if (Math.Abs(e.X - _downMouse.X) + Math.Abs(e.Y - _downMouse.Y) > DragThreshold)
            _dragged = true;

        if (_dragged)
        {
            var delta = new Vector2(e.X - _lastMouse.X, e.Y - _lastMouse.Y);
            _camera.PanByScreen(delta);

            long now = _clock.ElapsedTicks;
            float dt = (float)((now - _lastMoveTicks) / (double)System.Diagnostics.Stopwatch.Frequency);
            _lastMoveTicks = now;
            if (dt > 0.0001f)
            {
                Vector2 inst = delta / dt;
                _dragVel = Vector2.Lerp(_dragVel, inst, 0.4f);
            }
        }
        _lastMouse = e.Location;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_draw.Active) { HandleDrawUp(); return; }

        if (_pinResizeDrag)
        {
            _pinResizeDrag = false;
            PinStore.Save(_pins);
            return;
        }

        if (_pinDrag >= 0)
        {
            if (_pinDragged && _pinDrag < _pins.Count)
            {
                var pin = _pins[_pinDrag];
                var np = Collision.Resolve(pin.Pos, pin.SizeV, Obstacles(pin), 10f);
                pin.X = np.X; pin.Y = np.Y;
            }
            _pinDrag = -1;
            _pinDragged = false;
            PinStore.Save(_pins);
            return;
        }

        if (_renderer != null && _renderer.ArrowRect.Contains(e.Location) && _tileDrag < 0 && !_dragged)
            return;

        if (_tileDrag >= 0)
        {

            if (_drillOnClick >= 0 && _drillOnClick < _items.Count && _items[_drillOnClick].StackCount > 1)
            {
                string appKey = _items[_drillOnClick].AppKey;
                _tileDrag = -1; _dragCluster = null; _drillOnClick = -1;
                EnterDrill(appKey);
                return;
            }

            if (_dragCluster == null && _snapTarget >= 0 && _snapTarget < _items.Count &&
                _tileDrag < _items.Count && _snapTarget != _tileDrag)
            {
                WindowItem a = _items[_tileDrag];
                WindowItem b = _items[_snapTarget];
                bool dragLeft = a.WorldCenter.X <= b.WorldCenter.X;
                IntPtr left = (dragLeft ? a : b).Hwnd;
                IntPtr right = (dragLeft ? b : a).Hwnd;
                BumpMru(a.Hwnd); BumpMru(b.Hwnd);
                _snapTarget = -1; _tileDrag = -1; _dragCluster = null; _drillOnClick = -1;
                _savedZoom = _camera.Zoom;
                WindowTiling.SnapSideBySide(left, right, Handle);
                HideOverlay();
                return;
            }

            if (_dragCluster == null && _tileDrag < _items.Count)
            {
                var moved = _items[_tileDrag];
                Vector2 resolved = Collision.Resolve(moved.WorldPos, moved.WorldSize, Obstacles(moved), 12f);
                if ((resolved - moved.WorldPos).LengthSquared() > 0.5f)
                {
                    moved.SlideTarget = resolved;
                    moved.Sliding = true;
                }
            }
            foreach (var it in _items) Remember(it);
            _tileDrag = -1;
            _dragCluster = null;
            _snapTarget = -1;
            _drillOnClick = -1;
            return;
        }
        if (e.Button == MouseButtons.Middle) return;
        if (_committing) return;

        bool wasDrag = _dragged;
        _mouseDown = false;
        _dragged = false;
        if (wasDrag)
        {

            if (_dragVel.LengthSquared() > 400f) _camera.Flick(_dragVel);
            return;
        }
        if (e.Button != MouseButtons.Left) return;

        UpdateHover(e.Location);
        if (_hoverClose && _hoverIndex >= 0 && _hoverIndex < _items.Count)
        {
            CloseTile(_items[_hoverIndex]);
            return;
        }

        int hit = HitTest(e.Location);
        if (hit >= 0)
            BeginCommit(hit);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (_committing) return;
        float factor = MathF.Pow(1.2f, e.Delta / 120f);
        _camera.ZoomAt(new Vector2(e.X, e.Y), factor);
    }

    private int HitTest(Point screen)
    {
        Vector2 world = _camera.ScreenToWorld(new Vector2(screen.X, screen.Y));

        for (int i = _items.Count - 1; i >= 0; i--)
            if (_items[i].ContainsWorldPoint(world)) return i;
        return -1;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_open && _renderer != null && ClientSize.Width > 0 && ClientSize.Height > 0)
        {
            _camera.Viewport = new Vector2(ClientSize.Width, ClientSize.Height);
            _renderer.Resize(ClientSize.Width, ClientSize.Height);
            ApplySafeArea();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SetDesktopIconsHidden(false);
            LayoutStore.Save(SavedPositions);
            SizeStore.Save(SavedSizes);
            PinStore.Save(_pins);
            DrawStore.Save(_strokes);
            Application.Idle -= OnIdle;
            if (_foreHook != IntPtr.Zero) { NativeMethods.UnhookWinEvent(_foreHook); _foreHook = IntPtr.Zero; }
            _hook?.Dispose();
            _captures?.Dispose();
            _renderer?.Dispose();
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        }
        base.Dispose(disposing);
    }
}
