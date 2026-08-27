# CubeAvalonia

Cross-platform 3D coordinate viewer for cryo-ET, built with
[Avalonia UI](https://avaloniaui.net/). Avalonia port of the Warp Cube
application. Runs on **Linux** and **Windows**.

## Quick Start

```bash
# Build
cd warp
dotnet build CubeAvalonia/CubeAvalonia.csproj

# Run (empty, load tomogram via file picker)
dotnet run --project CubeAvalonia/CubeAvalonia.csproj

# Run with synthetic demo data
dotnet run --project CubeAvalonia/CubeAvalonia.csproj -- --demo
```

## Features

- **Tri-planar volume viewer** — simultaneous XY, ZY, XZ orthogonal slices
- **Particle overlay** — circles at particle coordinates on all 3 planes,
  filtered by FOM score range
- **Slice navigation** — scroll wheel changes slice, click-drag repositions
  crosshairs across all planes
- **Particle management** — arrow keys navigate between particles, Delete key
  removes selected particle
- **Intensity control** — sigma-based intensity windowing, slice averaging
- **STAR file I/O** — import/export particle coordinates
  (`rlnCoordinateX/Y/Z`, `rlnAutopickFigureOfMerit`)
- **Particle detail** — 2x2 sub-volume views centered on selected particle

## Interaction

| Action | Effect |
|--------|--------|
| Scroll wheel on XY | Change Z slice |
| Scroll wheel on ZY | Change X slice |
| Scroll wheel on XZ | Change Y slice |
| Click-drag on any plane | Reposition crosshairs |
| Left/Right arrow keys | Navigate between particles |
| Delete key | Remove selected particle |

## Project Structure

```
CubeAvalonia/
├── CubeAvalonia.csproj     (Avalonia 11.3.12, net8.0, references WarpLib)
├── Program.cs              (entry point, --demo flag)
├── App.axaml / .cs         (Fluent theme)
├── MainWindow.axaml / .cs  (tri-planar viewer, particle management)
├── CoordsImportWindow.axaml / .cs  (import dialog)
└── Controls/
    └── ValueSlider.axaml / .cs     (compact numeric input)
```

**9 source files, ~564 lines.**

## Requirements

- .NET 8.0 SDK
- Linux (X11 or Wayland) or Windows
- For loading real MRC tomograms: WarpLib `Image.FromFile()` requires
  accessible MRC file (no GPU needed for viewing)

## Porting Notes

Ported from `warp/Cube/` (WPF, 1,887 lines). Key WPF → Avalonia changes:
- `MetroWindow` → `Window` with Fluent theme
- `Canvas` + `Image` + `Line` → same Avalonia controls
- `MouseWheel/Down/Move` → `PointerWheelChanged/Pressed/Moved`
- `BitmapSource.Create(Indexed8)` → `WriteableBitmap` + BGRA32
- `FileDialog` (WinForms) → `StorageProvider` API
- `KeyDown` → `KeyDown` (same in Avalonia)
