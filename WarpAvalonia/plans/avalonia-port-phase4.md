# Avalonia Port — Phase 4: MicrographDisplay

## Context

Phase 3 completed all right-panel visualization controls (scatter plots, donut
chart, CTF display, status bar). The MicrographDisplay is the most complex
control in the WPF Warp GUI at 2,608 lines with 31 DependencyProperties,
6 rendering layers, GPU integration, and interactive mouse tools.

This phase ports MicrographDisplay incrementally -- image display first, then
overlays one at a time. Each step produces a working control so we can test
continuously.

## Source Files
- `Warp/Controls/MainUI/MicrographDisplay.xaml` (185 lines)
- `Warp/Controls/MainUI/MicrographDisplay.xaml.cs` (2,608 lines)

## Architecture: 6 Rendering Layers (bottom to top)

1. **ImageDisplay** — core micrograph bitmap (zoom, deconv, intensity scaling)
2. **ImageMask** — binary mask overlay (orange alpha)
3. **ImageElevation** — defocus gradient heatmap
4. **CanvasTrack** — motion tracks (grid of polylines)
5. **CanvasTrackMouse** — dynamic track under cursor
6. **CanvasParticles** — particle circles/dots with FOM labels

## Implementation Steps

### Step 1: Core image display (BGRA32 bitmap + zoom/pan)
Create `MicrographDisplay.axaml/.cs` with:
- Image control for core micrograph display
- Zoom (mouse wheel) and pan (drag) interaction
- Intensity range normalization (sigma-based windowing)
- `UpdateImage()` method: loads test data → normalizes → BGRA32 → WriteableBitmap
- For PoC: generate synthetic micrograph (Gaussian blobs on noisy background)
- No GPU dependency yet — pure CPU rendering

**WPF APIs to port:**
- `BitmapSource.Create(Indexed8, Gray256)` → `WriteableBitmap` + BGRA32
- `SemaphoreSlim` for update serialization → same in .NET 8
- Mouse wheel zoom + drag pan → Avalonia `PointerWheelChanged` + `PointerPressed/Moved/Released`

**Properties:** Zoom, IntensityRange

### Step 2: Elevation overlay (defocus heatmap)
- Semi-transparent colored overlay from defocus grid
- LinearGradientBrush mapping (pink → blue)
- 16×16 interpolated grid → BGRA32 bitmap
- Toggle: `ElevationShow` property

### Step 3: Motion tracks (Canvas polylines)
- Grid of polylines showing per-pixel motion vectors
- `Path` + `PathGeometry` + `PolyLineSegment` → Avalonia `Polyline`
- Track under mouse cursor with dot markers
- Scale control
- `DropShadowEffect` → Avalonia `DropShadowEffect`

**Properties:** TrackShow, TrackScale, TrackGridX, TrackGridY

### Step 4: Particle overlay (circles/dots)
- Ellipse shapes at particle coordinates
- Color customization (replace Xceed ColorCanvas with Avalonia ColorPicker or simple preset palette)
- FOM threshold filtering
- Blinky timer animation (DispatcherTimer → Avalonia DispatcherTimer)
- Click to select/create, right-click to delete

**Properties:** ParticlesShow, ParticlesDiameter, ParticleCircleBrush,
ParticleCircleStyle, ParticlesThreshold, ParticlesShowFOM, ParticlesBlinky

### Step 5: Mask painting
- Binary mask overlay (orange alpha on BGRA32 bitmap)
- Brush cursor (Ellipse following mouse)
- Left-drag paint, right-drag erase
- PaintMaskLine algorithm (circle-line intersection, framework-agnostic math)
- Save/load mask (TIFF via BitMiracle.LibTiff.NET, already in WarpLib)

**Properties:** MaskShow, BrushActive, BrushDiameter

### Step 6: Settings panel sidebar
Port the control panel from MicrographDisplay.xaml lines 1-185:
- Zoom/intensity sliders (ValueSlider — already ported)
- Deconv controls (enable, mode dropdown, strength, falloff, highpass)
- Track controls (show, scale, grid X/Y)
- Elevation toggle
- Particle controls (show, suffix dropdown, diameter, color, style, threshold, FOM)
- Mask/brush controls
- Wire to StyledProperties

### Step 7: Wire into Real Space tab
Replace SliceRenderer PoC with MicrographDisplay in MainWindow.

## Files to Create
- `WarpAvalonia/Controls/MicrographDisplay.axaml` (~200 lines)
- `WarpAvalonia/Controls/MicrographDisplay.axaml.cs` (~1,200-1,500 lines)

## Files to Modify
- `WarpAvalonia/MainWindow.axaml` — Real Space tab content

## Verification
After each step: `dotnet build && dotnet run`
- Step 1: Synthetic micrograph with zoom/pan
- Step 2: Toggle elevation → colored overlay
- Step 3: Toggle tracks → polyline grid
- Step 4: Toggle particles → colored circles
- Step 5: Brush tool → paint/erase on mask layer
- Step 6: All sidebar controls functional
- Step 7: Integrated in Real Space tab
