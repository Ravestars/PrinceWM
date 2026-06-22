# PrinceWM

Infinite canvas tab switcher that replaces native Alt+Tab in Windows. Press Alt+Tab to
spread your open windows across the canvas as live tiled previews, then switch, stack,
arrange, annotate, and snap them however you want.

## Features

- **Infinite canvas.** Drag, pinch, and flick through live previews of every window.
- **Real MRU ordering.** Alt+Tab cycles through open windows in real most-recently-used
  order, just as in native tab switchers.
- **Seamless switching.** The chosen preview fades away smoothly and turns into the real
  window
- **App stacks and drill-down.** All windows belonging to the same application form a
  tile. Click-mouse on the tile to unfold its windows in an individual page.
- **Pinboard mode.** Paste any image or stickies from the clipboard onto the canvas.
- **Paint mode.** Draw freely on the canvas
- **Live editing.** Live edit colors, grid, tile style, glow, animations, background,
  wallpaper, etc.
- **Drag to tile (beta).** Drop one window's tile onto another, snapping real windows
  side-by-side.

## Compiling

Requires the .NET 9 SDK on Windows 10/11.

```
dotnet build PrinceWM.csproj -c Release
```

Oh also people who area reading this the app was vibe coded by a bit mostly multiscreen support because i dont own two screens i had to use claude code for that part. Please understand <33

The application will need to run as an administrator because it hooks globally.

## Technical details

C#, .NET 9, WinForms host. Vortice.Windows for DirectX 11 + DXGI + Direct2D.
Windows.Graphics.Capture is used for capturing live window textures.

## License

[MIT license](LICENSE)
