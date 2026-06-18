# PrinceWM

An infinite-canvas Alt+Tab replacement for Windows. Summon it with Alt+Tab and your
open windows fan out as live tiles on a pannable, zoomable canvas. Switch, arrange,
stack, annotate, and snap windows however you like.

## Features

- **Infinite canvas.** Pan, zoom, and flick through live previews of every window.
- **True MRU cycling.** Alt+Tab steps through windows in real most-recently-used order,
  just like the native switcher.
- **Seamless switch-in.** The selected tile melts straight into the real window (outline
  fades, corners square off, geometry lands frame-perfect).
- **Per-app stacks and drill-in.** Windows of one app group into a tile; middle-click to
  unfold them on their own page.
- **Pinboard.** Paste images and sticky notes onto the canvas.
- **Paint mode.** Draw freehand on the canvas (optional pen tool).
- **Live customization.** Colors, dot grid, tile shape, glow, animation speed, wallpaper
  backdrop, and more, all applied instantly.
- **Drag-to-tile (beta).** Drop one window's tile onto another to snap the real windows
  side by side.

## Building

Requires the .NET 9 SDK on Windows 10/11.

```
dotnet build PrinceWM.csproj -c Release
```

The app runs elevated (it installs a global keyboard hook and uses Windows.Graphics.Capture).

## Tech

C# / .NET 9, WinForms host, Vortice.Windows (Direct3D 11 + Direct2D + DXGI),
Windows.Graphics.Capture for live window textures.

## License

[MIT](LICENSE). Free to use, modify, and redistribute. Open source.
