using Vortice.Mathematics;

namespace PrinceWM.Core;

internal sealed class Theme
{
    public int Background { get; set; } = Rgb(17, 18, 22);
    public int Accent { get; set; } = Rgb(102, 199, 255);
    public int Dot { get; set; } = Rgb(77, 84, 102);
    public int Border { get; set; } = Rgb(72, 77, 92);
    public bool ShowDots { get; set; } = true;
    public bool ShowHints { get; set; } = true;
    public bool ShowTitles { get; set; } = true;
    public bool ShowTileOutline { get; set; } = true;
    public bool WindowShadows { get; set; } = true;
    public bool ShowPaintButton { get; set; } = false;
    public bool RememberZoom { get; set; } = false;
    public bool DragToTile { get; set; } = false;
    public int GlowIntensity { get; set; } = 12;

    public int CornerRadius { get; set; } = 8;
    public int TileGap { get; set; } = 56;
    public int HoverLift { get; set; } = 30;
    public int AnimSpeed { get; set; } = 100;
    public int SettleSpeed { get; set; } = 100;
    public int BorderThickness { get; set; } = 12;
    public int DotSize { get; set; } = 15;
    public int DotSpacing { get; set; } = 80;

    public bool UseWallpaper { get; set; } = false;
    public bool ShowDesktopIcons { get; set; } = false;
    public int BlurAmount { get; set; } = 8;
    public int TintColor { get; set; } = 0;
    public int TintStrength { get; set; } = 35;

    public static int Rgb(int r, int g, int b) => (r << 16) | (g << 8) | b;

    public static Color4 ToColor4(int rgb, float alpha = 1f)
    {
        float r = ((rgb >> 16) & 0xFF) / 255f;
        float g = ((rgb >> 8) & 0xFF) / 255f;
        float b = (rgb & 0xFF) / 255f;
        return new Color4(r, g, b, alpha);
    }

    public Theme Clone() => new()
    {
        Background = Background,
        Accent = Accent,
        Dot = Dot,
        Border = Border,
        ShowDots = ShowDots,
        ShowHints = ShowHints,
        ShowTitles = ShowTitles,
        ShowTileOutline = ShowTileOutline,
        WindowShadows = WindowShadows,
        ShowPaintButton = ShowPaintButton,
        RememberZoom = RememberZoom,
        DragToTile = DragToTile,
        GlowIntensity = GlowIntensity,
        CornerRadius = CornerRadius,
        TileGap = TileGap,
        HoverLift = HoverLift,
        AnimSpeed = AnimSpeed,
        SettleSpeed = SettleSpeed,
        BorderThickness = BorderThickness,
        DotSize = DotSize,
        DotSpacing = DotSpacing,
        UseWallpaper = UseWallpaper,
        ShowDesktopIcons = ShowDesktopIcons,
        BlurAmount = BlurAmount,
        TintColor = TintColor,
        TintStrength = TintStrength,
    };
}
