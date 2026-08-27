# Avalonia Port — Phase 3: Right-Panel Visualizations

## Context

Phase 2 delivered the complete settings panel (left column) with all sections
matching WPF Warp 1:1, native file dialogs, and a processing loop stub.

Phase 3 ports the right-panel visualization controls that display processing
results -- scatter plots, CTF display, status bar, and the overview tab
statistics. The MicrographDisplay (2,608 lines, most complex control) is
deferred to Phase 4.

## Status: COMPLETE (2026-03-28)

All 6 visualization controls ported and rendering. Zero external charting
dependencies -- all rendering via Avalonia's native DrawingContext, Canvas,
and WriteableBitmap.

## Implementation Summary

### Step 1: GridMiniature — DONE
**File:** `WarpAvalonia/Controls/GridMiniature.axaml/.cs` (~160 lines)
**Source:** `Warp/Controls/MainUI/GridMiniature.xaml.cs`

Isometric 3D grid visualization for CTF and Motion model grids. Pure Canvas +
Line/Ellipse drawing. Wired into the Models section of the settings panel,
bound to `Options.Grids.CTFX/Y/Z` and `Options.Grids.MovementX/Y/Z`.

**WPF → Avalonia changes:**
- `DependencyProperty` → `StyledProperty<T>`
- `Line.X1/Y1/X2/Y2` → `Line.StartPoint/EndPoint`
- `Brush.Clone()` + `.Opacity` → `new SolidColorBrush(color, opacity)`
- `SizeChanged` → `BoundsProperty.Changed`
- `ActualWidth` → `Bounds.Width`

### Step 2: SingleAxisScatter — DONE
**File:** `WarpAvalonia/Controls/SingleAxisScatter.axaml/.cs` (~230 lines)
**Source:** `Warp/Controls/MainUI/SingleAxisScatter.xaml.cs` (465 lines)

1D scatter plot with histogram sidebar, range highlight bar, axis labels, and
point interaction. Used 4× in the Overview tab (defocus, resolution, motion,
particles). Includes `GenerateTestData()` for PoC validation.

**WPF → Avalonia changes:**
- `DrawingGroup` / `DrawingContext.DrawEllipse()` → identical API in Avalonia
- `DrawingImage` + `.Freeze()` → `DrawingImage` (no Freeze needed)
- `PushClip(new RectangleGeometry(...))` → `PushClip(new RoundedRect(...))`
- `Polygon` with `PointCollection` → `Polygon` with `Points` (Avalonia.Points)
- `MouseMove/Down/Leave` → `PointerMoved/Pressed/Exited`
- `Dispatcher.Invoke()` → not needed (already on UI thread in property handlers)
- WPF `Movie` context reference removed (replaced with `int ColorID`)

### Step 3: DualAxisScatter — DONE
**File:** `WarpAvalonia/Controls/DualAxisScatter.axaml/.cs` (~130 lines)
**Source:** `Warp/Controls/MainUI/DualAxisScatter.xaml.cs` (308 lines)

2D scatter plot centered on origin, used for astigmatism display. Same
DrawingContext pattern as SingleAxisScatter. Displayed in the Overview tab
inside a circular `Border` with `ClipToBounds="True"` and `CornerRadius="110"`.

**WPF → Avalonia changes:** Same as SingleAxisScatter.

### Step 4: DonutChart (custom, replaces LiveCharts) — DONE
**File:** `WarpAvalonia/Controls/DonutChart.axaml/.cs` (~120 lines)
**Replaces:** `LiveCharts.Wpf.PieChart` (WPF-only dependency)

Custom donut/pie chart rendered via `DrawingContext` with `StreamGeometry`
arc segments. Matches the WPF Warp processing status chart (5 wedges:
processed/green, outdated/yellow, unprocessed/red, filtered/cyan,
deselected/gray). Center text overlay for benchmark display.

**Decision:** Option A (custom Canvas drawing) chosen over ScottPlot or
LiveCharts2 to avoid adding a heavy charting dependency for one simple chart.
~120 lines including arc math, labels, and donut hole.

**Key implementation:** `StreamGeometry` with `ArcTo()` for each wedge,
`FormattedText` for labels inside the arc segments.

### Step 5: CTFDisplay — DONE
**File:** `WarpAvalonia/Controls/CTFDisplay.axaml/.cs` (~200 lines)
**Source:** `Warp/Controls/MainUI/CTFDisplay.xaml.cs` (628 lines)

CTF visualization panel with three components:
1. **2D simulated CTF** — Thon rings rendered to `WriteableBitmap` via
   BGRA32 byte array + `Marshal.Copy` (same pipeline as SliceRenderer PoC)
2. **2D power spectrum** — same bitmap rendering with added Gaussian noise
3. **1D CTF plot** — custom `Canvas` with `Polyline` elements (pink=fitted,
   cyan=experimental, gray=quality), replacing LiveCharts `CartesianChart`

For the PoC, generates synthetic CTF data using the relativistic electron
wavelength formula. In production, this will be driven by `Movie`/`TiltSeries`
CTF data from WarpLib (the data pipeline already exists in WarpLib, only the
UI hookup is needed).

**WPF → Avalonia changes:**
- `BitmapSource.Create(Indexed8, Gray256 palette)` → `WriteableBitmap` + BGRA32
- LiveCharts `CartesianChart` + `LineSeries` → `Canvas` + `Polyline`
- CTF parameter overlay as simple `TextBlock` stack (same in both frameworks)
- Tilt dial (circular Canvas arrangement) deferred to production integration

### Step 6: ProcessingStatusBar — DONE
**File:** `WarpAvalonia/Controls/ProcessingStatusBar.axaml/.cs` (~130 lines)
**Source:** `Warp/Controls/MainUI/StatusBar.xaml.cs` (448 lines)

Color-coded item grid showing processing status of all movies/tilt series.
Each item is a small colored rectangle rendered on Canvas. Includes filter
checkboxes (5 status categories), search text box, and processed count
display. Active item highlighted with white border.

**WPF → Avalonia changes:**
- `StatusBarRow` child control → simplified to direct Canvas rectangle rendering
- `DependencyProperty` → `StyledProperty<T>`
- `Visibility.Hidden` → `IsVisible = false`
- `WarpRuntime.MainWindow.Options` coupling → removed (self-contained)
- `Movie` references → replaced with `StatusItem` struct

---

## Key WPF → Avalonia Patterns Validated

| WPF API | Avalonia Equivalent | Notes |
|---------|-------------------|-------|
| `DrawingGroup` / `DrawingContext` | Same classes | Core of scatter plot rendering |
| `DrawingImage` + `.Freeze()` | `DrawingImage` (no Freeze) | Immutability handled differently |
| `dc.PushClip(RectangleGeometry)` | `dc.PushClip(RoundedRect)` | Different clip type |
| `BitmapSource.Create(Indexed8)` | `WriteableBitmap` + BGRA32 | No indexed palette in Avalonia |
| `Polygon` + `PointCollection` | `Polygon` + `Avalonia.Points` | Different collection type |
| `StreamGeometry` + `ArcTo()` | Same classes | Used for donut chart wedges |
| `Polyline` | Same class | Used for 1D CTF line plot |
| `Canvas` + `Line`/`Ellipse`/`Rectangle` | Same controls | `Line` uses `StartPoint`/`EndPoint` |
| `FormattedText` | Same class | Used for chart labels |
| `Mouse*` events | `Pointer*` events | Different event names and args |
| `Dispatcher.Invoke()` | `Dispatcher.UIThread.InvokeAsync()` | Async version preferred |
| LiveCharts `PieChart` | Custom `DonutChart` | Zero dependencies |
| LiveCharts `CartesianChart` | Custom `Canvas` + `Polyline` | Zero dependencies |

---

## Files Created (Phase 3)

| File | Lines | Purpose |
|------|-------|---------|
| `Controls/GridMiniature.axaml` | 5 | Canvas-only XAML |
| `Controls/GridMiniature.axaml.cs` | 155 | Isometric grid drawing |
| `Controls/SingleAxisScatter.axaml` | 52 | 1D scatter layout |
| `Controls/SingleAxisScatter.axaml.cs` | 225 | Scatter + histogram rendering |
| `Controls/DualAxisScatter.axaml` | 12 | 2D scatter layout |
| `Controls/DualAxisScatter.axaml.cs` | 125 | 2D scatter rendering |
| `Controls/DonutChart.axaml` | 9 | Chart + center text layout |
| `Controls/DonutChart.axaml.cs` | 115 | Arc segment rendering |
| `Controls/CTFDisplay.axaml` | 42 | 2×2 grid layout |
| `Controls/CTFDisplay.axaml.cs` | 200 | CTF bitmap + 1D plot |
| `Controls/ProcessingStatusBar.axaml` | 33 | Filter controls + canvas |
| `Controls/ProcessingStatusBar.axaml.cs` | 125 | Color-coded item grid |
| **Total** | **~1,100** | |

## Files Modified (Phase 3)

- `MainWindow.axaml` — GridMiniature in Models section, DonutChart + DualAxisScatter + 4× SingleAxisScatter in Overview tab, CTFDisplay in Fourier Space tab, ProcessingStatusBar at bottom
- `MainWindow.axaml.cs` — test data generation for all visualization controls, grid defaults

---

## Complete Project Inventory (Phases 1-3)

```
warp/WarpAvalonia/
├── WarpAvalonia.csproj          (Avalonia 11.3.12, net8.0, refs WarpLib)
├── App.axaml / .cs              (Fluent theme, dark mode default)
├── MainWindow.axaml             (full 3-column layout, all sections)
├── MainWindow.axaml.cs          (OptionsWarp binding, file dialogs, processing stub)
├── Program.cs                   (entry point)
├── Controls/
│   ├── ValueSlider.axaml / .cs            (Phase 1 — inline text editor)
│   ├── SliceRenderer.axaml / .cs          (Phase 1 — BGRA32 bitmap rendering)
│   ├── DecimalDoubleConverter.cs          (Phase 1 — WarpLib decimal ↔ double)
│   ├── GridMiniature.axaml / .cs          (Phase 3 — isometric grid)
│   ├── SingleAxisScatter.axaml / .cs      (Phase 3 — 1D scatter + histogram)
│   ├── DualAxisScatter.axaml / .cs        (Phase 3 — 2D scatter)
│   ├── DonutChart.axaml / .cs             (Phase 3 — replaces LiveCharts pie)
│   ├── CTFDisplay.axaml / .cs             (Phase 3 — power spectrum + 1D CTF)
│   └── ProcessingStatusBar.axaml / .cs    (Phase 3 — item grid + filters)
└── plans/
    ├── avalonia-port-phase2.md
    └── avalonia-port-phase3.md
```

**10 custom controls, ~2,500 lines total, zero external UI dependencies
beyond Avalonia core.**

---

## Not in scope (Phase 4+)

- MicrographDisplay (2,608 lines — Phase 4, most complex control)
- Task dialogs (2D/Tomo export/import — Phase 5)
- M and Cube applications (Phase 6)
- Full processing pipeline integration with WarpLib GPU (Phase 7)
