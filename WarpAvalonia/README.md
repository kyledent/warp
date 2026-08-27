# WarpAvalonia

Cross-platform GUI for [Warp](https://github.com/warpem/warp) cryo-EM/ET
processing, built with [Avalonia UI](https://avaloniaui.net/). Runs on
**Linux** and **Windows** using the same WarpLib processing backend as the
original Windows-only WPF desktop application.

## Quick Start

```bash
# Prerequisites: .NET 8.0 SDK
# Install: https://dot.net/v1/dotnet-install.sh --channel 8.0

# Build
cd warp
dotnet build WarpAvalonia/WarpAvalonia.csproj

# Run (empty, ready for data)
dotnet run --project WarpAvalonia/WarpAvalonia.csproj

# Run with synthetic demo data
dotnet run --project WarpAvalonia/WarpAvalonia.csproj -- --demo
```

## What This Is

WarpAvalonia is a port of the Warp desktop GUI from WPF (Windows-only) to
Avalonia UI (cross-platform). It provides the same interface for cryo-EM
preprocessing: settings configuration, CTF estimation, motion correction,
particle picking, template matching, tomogram reconstruction, and particle
export.

The port references **WarpLib** directly -- the same core library used by the
WPF Warp app and the WarpTools CLI. All processing logic (GPU acceleration,
image I/O, CTF fitting, reconstruction) comes from WarpLib unchanged.

## Architecture

```
WarpLib (net8.0, cross-platform)
    |
    +-- WarpTools      (CLI, net8.0)
    +-- WarpWorker     (REST server, net8.0)
    +-- WarpAvalonia   (GUI, Avalonia 11.3, net8.0)  <-- this project
    |
    +-- Warp           (WPF GUI, net8.0-windows)     <-- original, Windows-only
```

WarpAvalonia replaces the WPF GUI layer while sharing the entire processing
backend. Settings, options, and data models are the same `OptionsWarp`,
`Movie`, `TiltSeries`, `Star`, and `Image` classes from WarpLib.

## Features

### Settings Panel (left column)
All settings from the WPF Warp GUI, bound to WarpLib's `OptionsWarp`:
- **Input** -- data folder, extension, pixel size, binning, exposure, EER grouping, tomo dimensions
- **Preprocessing** -- gain reference, defect map, flip/transpose
- **CTF** -- window, fitting range, voltage, Cs, phase shift, amplitude, defocus range
- **Motion** -- frequency range, B-factor weighting
- **Models** -- CTF and motion grid parameters with isometric miniature visualization
- **Pick Particles** -- BoxNet model, diameter, style, score threshold, extraction
- **Output** -- frame skipping, averaging, odd/even denoising
- **Start Processing** -- launches the processing loop with progress display

Native file/folder dialogs via Avalonia `StorageProvider` API.

### Visualization Controls (right column)

| Control | Purpose | Rendering |
|---------|---------|-----------|
| **SingleAxisScatter** | 1D scatter + histogram (defocus, resolution, motion, particles) | DrawingContext |
| **DualAxisScatter** | 2D scatter (astigmatism) | DrawingContext |
| **DonutChart** | Processing status pie chart (replaces LiveCharts) | StreamGeometry arcs |
| **CTFDisplay** | 2D power spectrum + simulated CTF + 1D CTF plot | WriteableBitmap + Polyline |
| **MicrographDisplay** | Interactive micrograph viewer with 6 overlay layers | WriteableBitmap + Canvas |
| **GridMiniature** | Isometric 3D grid visualization | Canvas + Line/Ellipse |
| **ProcessingStatusBar** | Color-coded item grid with filtering | Canvas + Rectangle |
| **ValueSlider** | Compact numeric input | TextBox |
| **SliceRenderer** | 3D volume slice viewer | WriteableBitmap |

All rendering uses Avalonia's native `DrawingContext`, `Canvas`, and
`WriteableBitmap` -- zero external charting dependencies.

### MicrographDisplay Layers
The micrograph viewer supports 6 interactive overlay layers:
1. **Core image** -- zoom (scroll wheel), pan (drag), intensity windowing
2. **Elevation** -- defocus gradient heatmap (pink-to-blue)
3. **Mask** -- binary mask with brush painting (left-drag paint, right-drag erase)
4. **Motion tracks** -- polyline grid showing per-pixel motion vectors
5. **Cursor track** -- dynamic motion track under mouse
6. **Particles** -- circles/dots at particle positions with threshold filtering

### Task Dialogs (13 total)

**2D operations:**
- Export micrograph list (RELION STAR with CTF + polishing metadata)
- Adjust particle defocus (interpolate local CTF at coordinates)
- Export particles (GPU-accelerated extraction via WorkerWrapper)
- Import particle coordinates (STAR scaling + per-micrograph output)
- Template matching (multi-GPU via `Helper.ForEachGPU`)

**Tomography operations:**
- Import tilt series from IMOD (mdoc/xf parsing, TomoSTAR generation)
- Export tomogram list (STAR with CTF metadata)
- Reconstruct full tomograms (multi-GPU via `TiltSeries.ReconstructFull`)
- Export sub-tomograms (coordinate extraction + `ReconstructSubtomos`)
- Template matching 3D (multi-GPU via `TiltSeries.MatchFull`)

**BoxNet AI:**
- Select model (file browser)
- Export training examples (micrograph + label + mask TIFF stacks)
- Retrain model (training loop with loss curve visualization)

## Project Structure

```
WarpAvalonia/
+-- WarpAvalonia.csproj        Avalonia 11.3.12, net8.0, references WarpLib
+-- Program.cs                 Entry point, --demo flag handling
+-- App.axaml / .cs            Avalonia app with Fluent theme
+-- MainWindow.axaml / .cs     3-column layout, settings, processing loop
|
+-- Controls/
|   +-- ValueSlider            Compact numeric input control
|   +-- SliceRenderer           3D volume slice bitmap renderer
|   +-- DecimalDoubleConverter  WarpLib decimal <-> Avalonia double bridge
|   +-- GridMiniature           Isometric 3D grid (Models section)
|   +-- SingleAxisScatter       1D scatter plot + histogram (Overview)
|   +-- DualAxisScatter         2D scatter plot (astigmatism)
|   +-- DonutChart              Processing status pie (replaces LiveCharts)
|   +-- CTFDisplay              Power spectrum + 1D CTF (Fourier Space)
|   +-- MicrographDisplay       Interactive micrograph viewer (Real Space)
|   +-- ProcessingStatusBar     Color-coded item grid + filters
|
+-- Dialogs/
|   +-- Dialog2DList            Export micrograph STAR
|   +-- Dialog2DDefocusUpdate   Adjust particle defocus
|   +-- Dialog2DParticleExport  Extract 2D particles
|   +-- Dialog2DParticleImport  Import coordinates
|   +-- Dialog2DMatch           2D template matching
|   +-- DialogTomoList          Export tomogram STAR
|   +-- DialogTomoReconstruction Full tomogram reconstruction
|   +-- DialogTomoParticleExport Sub-tomogram extraction
|   +-- DialogTomoMatch         3D template matching
|   +-- DialogTomoImportImod    IMOD tilt series import
|   +-- BoxNetSelect            Model selection
|   +-- BoxNetExport            Training data export
|   +-- BoxNetTrain             Model retraining + loss curves
|
+-- Processing/
|   +-- FileDiscoverer.cs       File system watching + incubation + Movie creation
|   +-- ImageHelper.cs          Gain/defect loading (extracted from WPF MainWindow)
|
+-- plans/                      Implementation plans for each phase
    +-- avalonia-port-phase2.md
    +-- avalonia-port-phase3.md
    +-- avalonia-port-phase4.md
    +-- avalonia-port-phase5.md
```

**55 source files, ~5,500 lines of code, zero external UI dependencies
beyond Avalonia core.**

## WPF to Avalonia Porting Patterns

Key API translations used throughout:

| WPF | Avalonia |
|-----|----------|
| `DependencyProperty.Register()` | `AvaloniaProperty.Register<T>()` / `StyledProperty<T>` |
| `BitmapSource.Create(Indexed8)` | `WriteableBitmap` + `Lock()` + `Marshal.Copy` (BGRA32) |
| `DrawingGroup` / `DrawingContext` | Same classes (identical API) |
| `DrawingImage` + `.Freeze()` | `DrawingImage` (no Freeze needed) |
| `Canvas.Children.Add(new Line { X1, Y1, X2, Y2 })` | `new Line { StartPoint, EndPoint }` |
| `dc.PushClip(RectangleGeometry)` | `dc.PushClip(RoundedRect)` |
| `Polygon` + `PointCollection` | `Polygon` + `Avalonia.Points` |
| `MouseMove/Down/Leave` | `PointerMoved/Pressed/Exited` |
| `Dispatcher.Invoke()` | `Dispatcher.UIThread.InvokeAsync()` |
| `Visibility.Collapsed` | `IsVisible = false` |
| `FolderBrowserDialog` (WinForms) | `StorageProvider.OpenFolderPickerAsync()` |
| `MahApps.Metro MetroWindow` | `Avalonia.Controls.Window` + Fluent theme |
| `LiveCharts PieChart` | Custom `DonutChart` (StreamGeometry arcs) |
| `LiveCharts CartesianChart` | Custom Canvas + `Polyline` |

## Requirements

- .NET 8.0 SDK
- Linux (X11 or Wayland) or Windows
- For GPU processing: NVIDIA GPU with CUDA drivers + `NativeAcceleration.so` (built separately, see `warp/BUILD_LOG_2026-02-13.md`)

## Production Integration

The app includes a full production data pipeline:

**File Discovery** (`Processing/FileDiscoverer.cs`):
- Watches data folder via `FileSystemWatcher` (cross-platform on .NET 8)
- Incubates new files for 1 second until stable (size unchanged + readable)
- Creates `Movie` or `TiltSeries` objects from `.xml` metadata
- Max 15 concurrent creation tasks, sorted alphabetically
- Events: `FilesChanged`, `IncubationStarted`, `IncubationEnded`

**Processing Pipeline** (MainWindow `StartProcessing`):
- Detects unprocessed/outdated items from discovered files
- Multi-GPU dispatch via `Helper.ForEachGPU`
- Per-item: CTF estimation → motion correction → particle picking → export
- Live statistics updates after each item completes
- Cancellation via CancellationToken

**Statistics** (MainWindow `UpdateStatsAll`):
- Determines processing status per Movie (Processed/Outdated/Unprocessed/FilteredOut/Deselected)
- Updates scatter plots with real CTF defocus, resolution, motion, particle values
- Updates donut chart with status counts
- Color-codes status bar items

**Item Selection**:
- Click status bar item → loads CTF display + micrograph viewer with real data
- CTF display shows real defocus, Cs, voltage from Movie.CTF
- Micrograph display loads actual average image from Movie.AveragePath

**Task Dialogs**:
- All 13 dialogs receive real `CurrentItems` (filtered by Movie vs TiltSeries)
- File pickers for STAR/MRC inputs before launching processing dialogs

### Usage with real data

```bash
# Launch empty (production mode)
dotnet run --project WarpAvalonia/WarpAvalonia.csproj

# Then: click "Select folder..." in Input section
# FileDiscoverer will scan for data files and populate the UI
# Click items in the status bar to view CTF and micrographs
# Click START PROCESSING to begin CTF/motion/picking pipeline
```

## Implementation Status

| Phase | Scope | Status |
|-------|-------|--------|
| 1 -- PoC | Avalonia + WarpLib compiles, bitmap rendering, data binding | Done |
| 2 -- Settings | Complete left panel matching WPF 1:1 | Done |
| 3 -- Visualizations | Scatter plots, donut chart, CTF display, status bar | Done |
| 4 -- MicrographDisplay | 6-layer interactive viewer with zoom/pan/paint | Done |
| 5 -- Task Dialogs | All 13 processing dialogs with full processing logic | Done |
| 7 -- Production | File discovery, real data loading, GPU pipeline, statistics | Done |
| 6 -- M + Cube apps | Multi-particle refinement GUI, coordinate viewer | Not started |

### What requires GPU runtime (`NativeAcceleration.so`)

The following operations need the CUDA native library loaded at runtime:
- CTF estimation (`Movie.ProcessCTF`)
- Motion correction (`Movie.ProcessShift`)
- Template matching (`Movie.MatchFull`, `TiltSeries.MatchFull`)
- Tomogram reconstruction (`TiltSeries.ReconstructFull`, `ReconstructSubtomos`)
- BoxNet inference and training
- Image deconvolution

Everything else (file discovery, settings, visualization, STAR I/O, metadata
loading) works without GPU.

## Related Documentation

- [WARP_AVALONIA_PORT_ANALYSIS.md](../WARP_AVALONIA_PORT_ANALYSIS.md) -- feasibility analysis and effort estimates
- [plans/](plans/) -- per-phase implementation plans with WPF-to-Avalonia pattern references

## License

Same license as the upstream Warp repository.
