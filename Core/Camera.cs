using System.Numerics;

namespace PrinceWM.Core;

internal sealed class Camera
{
    public Vector2 Viewport;
    public float Zoom = 1f;
    public Vector2 Center;

    private float _targetZoom = 1f;
    private Vector2 _targetCenter;
    private Vector2 _panVel;

    private bool _tweening;
    private bool _tweenEaseOut;
    private float _tweenT, _tweenDur;
    private Vector2 _tweenStartC, _tweenEndC;
    private float _tweenStartZ, _tweenEndZ;

    public const float MinZoom = 0.12f;
    public const float MaxZoom = 8.0f;

    public static float DurationScale = 1f;

    public Vector2 TargetCenter => _targetCenter;
    public float TargetZoom => _targetZoom;

    public bool IsAnimating => _tweening;

    public void SnapTo(Vector2 center, float zoom)
    {
        _tweening = false;
        Center = _targetCenter = center;
        Zoom = _targetZoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        _panVel = Vector2.Zero;
    }

    private void SetTarget(Vector2 center, float zoom)
    {
        _targetCenter = center;
        _targetZoom = Math.Clamp(zoom, MinZoom, MaxZoom);
    }

    public Vector2 WorldToScreen(Vector2 world) => (world - Center) * Zoom + Viewport * 0.5f;
    public Vector2 ScreenToWorld(Vector2 screen) => (screen - Viewport * 0.5f) / Zoom + Center;

    private Vector2 ScreenToWorldTarget(Vector2 screen) =>
        (screen - Viewport * 0.5f) / _targetZoom + _targetCenter;

    private void CancelTween()
    {
        if (!_tweening) return;
        _tweening = false;
        _targetCenter = Center;
        _targetZoom = Zoom;
    }

    public void PanByScreen(Vector2 screenDelta)
    {
        CancelTween();
        _targetCenter -= screenDelta / _targetZoom;
        _panVel = Vector2.Zero;
    }

    public void Flick(Vector2 screenVelocity)
    {
        CancelTween();
        _panVel = -screenVelocity / _targetZoom;
    }

    public void ZoomAt(Vector2 screenAnchor, float factor)
    {
        CancelTween();
        Vector2 before = ScreenToWorldTarget(screenAnchor);
        _targetZoom = Math.Clamp(_targetZoom * factor, MinZoom, MaxZoom);
        Vector2 after = ScreenToWorldTarget(screenAnchor);
        _targetCenter += before - after;
    }

    public void ZoomStep(float factor) => ZoomAt(Viewport * 0.5f, factor);

    public void TweenTo(Vector2 center, float zoom, float duration, bool easeOut = false)
    {
        _tweenStartC = Center;
        _tweenStartZ = Zoom;
        _tweenEndC = center;
        _tweenEndZ = Math.Clamp(zoom, MinZoom, MaxZoom);
        _tweenT = 0f;
        _tweenDur = MathF.Max(0.0001f, duration * DurationScale);
        _tweenEaseOut = easeOut;
        _tweening = true;
        _panVel = Vector2.Zero;
        _targetCenter = _tweenEndC;
        _targetZoom = _tweenEndZ;
    }

    public void CenterOn(Vector2 worldCenter, float duration = 0.22f) =>
        TweenTo(worldCenter, _targetZoom, duration);

    public void PanTargetTo(Vector2 worldCenter)
    {
        CancelTween();
        _targetCenter = worldCenter;
        _panVel = Vector2.Zero;
    }

    public void ResetZoom() => TweenTo(_targetCenter, 1.0f, 0.24f);

    public void FocusOn(WindowItem item)
    {
        float pad = 1.4f;
        float zoom = MathF.Min(Viewport.X / (item.WorldSize.X * pad), Viewport.Y / (item.WorldSize.Y * pad));
        TweenTo(item.WorldCenter, zoom, 0.26f);
    }

    public void FocusFill(WindowItem item, float duration)
    {
        float zoom = MathF.Max(Viewport.X / item.WorldSize.X, Viewport.Y / item.WorldSize.Y);
        TweenTo(item.WorldCenter, zoom, duration, easeOut: true);
    }

    public void FitAll(IReadOnlyList<WindowItem> items, float marginFraction = 0.12f, bool animate = false)
    {
        if (items.Count == 0)
        {
            if (animate) TweenTo(Vector2.Zero, 1f, 0.4f); else SnapTo(Vector2.Zero, 1f);
            return;
        }

        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var it in items)
        {
            minX = MathF.Min(minX, it.WorldPos.X);
            minY = MathF.Min(minY, it.WorldPos.Y);
            maxX = MathF.Max(maxX, it.WorldPos.X + it.WorldSize.X);
            maxY = MathF.Max(maxY, it.WorldPos.Y + it.WorldSize.Y);
        }

        var center = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
        float spanX = MathF.Max(1f, maxX - minX);
        float spanY = MathF.Max(1f, maxY - minY);
        float pad = 1f + marginFraction * 2f;
        float zoom = MathF.Min(Viewport.X / (spanX * pad), Viewport.Y / (spanY * pad));
        zoom = Math.Clamp(zoom, MinZoom, MaxZoom);

        if (animate) TweenTo(center, zoom, 0.38f); else SnapTo(center, zoom);
    }

    public float FocusZoom(WindowItem item, float pad = 1.6f) =>
    Math.Clamp(MathF.Min(Viewport.X / (item.WorldSize.X * pad), Viewport.Y / (item.WorldSize.Y * pad)),
        MinZoom, MaxZoom);

    public void Step(float dt)
    {
        if (_tweening)
        {
            _tweenT += dt;
            float u = Math.Clamp(_tweenT / _tweenDur, 0f, 1f);
            float e = _tweenEaseOut ? EaseOut(u) : Smoother(u);
            Center = Vector2.Lerp(_tweenStartC, _tweenEndC, e);

            Zoom = _tweenStartZ * MathF.Pow(_tweenEndZ / _tweenStartZ, e);
            if (u >= 1f)
            {
                _tweening = false;
                Center = _tweenEndC;
                Zoom = _tweenEndZ;
            }
            return;
        }

        if (_panVel.LengthSquared() > 0.01f)
        {
            _targetCenter += _panVel * dt;
            _panVel *= MathF.Exp(-dt * 3.2f);
            if (_panVel.LengthSquared() < 0.01f) _panVel = Vector2.Zero;
        }

        float t = 1f - MathF.Exp(-dt * 16f);
        Center = Vector2.Lerp(Center, _targetCenter, t);

        Zoom *= MathF.Pow(_targetZoom / MathF.Max(Zoom, 1e-4f), t);
    }

    private static float Smoother(float u) => u * u * u * (u * (u * 6f - 15f) + 10f);

    private static float EaseOut(float u) { float p = 1f - u; return 1f - p * p * p * p * p; }

    public Matrix3x2 WorldMatrix =>
        Matrix3x2.CreateTranslation(-Center) *
        Matrix3x2.CreateScale(Zoom) *
        Matrix3x2.CreateTranslation(Viewport * 0.5f);
}
