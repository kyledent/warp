# MAvalonia

Cross-platform GUI for **M** (multi-particle refinement), built with
[Avalonia UI](https://avaloniaui.net/). Avalonia port of the Warp M
application. Runs on **Linux** and **Windows** using WarpLib's refinement
pipeline.

## Quick Start

```bash
# Build
cd warp
dotnet build MAvalonia/MAvalonia.csproj

# Run
dotnet run --project MAvalonia/MAvalonia.csproj
```

## Features

### Population Management
- **Create new population** — select folder, creates `population.xml`
- **Load existing population** — open `.xml` population file
- **Data sources** — add/remove Warp `.settings` data sources

### Species Management
- **Species cards** — grid display with name, symmetry, resolution
- **Create new species** — 4-tab wizard (Parameters, Half-maps, Mask, Particles)
- **Add existing species** — import from local folder
- **Species operations**:
  - Shift (translate in X/Y/Z)
  - Rotate (Euler angles)
  - Symmetry expansion
  - Particle set management (distance analysis, union/intersection)

### Refinement
- **Settings dialog** — full refinement parameter panel:
  - Iterations, resolution, minimum particles
  - Geometry: image warp, particle poses, magnification
  - CTF: defocus, phase shift, Cs, Zernike polynomials
  - Frame series: doming
  - Tilt series: stage angles, volume warp, tilt movies
- **Multi-GPU refinement** — dispatches across available GPUs via WarpLib
- **Progress tracking** — resolution and FSC monitoring

## Dialogs (10 total)

| Dialog | Purpose |
|--------|---------|
| DialogCreatePopulation | Name + folder for new population |
| DialogDataSources | Add/remove `.settings` data sources |
| DialogCreateSourceFromSettings | Import source from Warp settings |
| DialogAddSpecies | Choose creation method (scratch vs import) |
| DialogCreateNewSpecies | 4-tab wizard with half-maps, mask, particles |
| DialogRefinementSettings | Full refinement parameter configuration |
| DialogSpeciesShift | Translate species in X/Y/Z |
| DialogSpeciesRotate | Rotate species by Euler angles |
| DialogSpeciesSymExpand | Symmetry expansion with target symmetry |
| DialogSpeciesParticleSets | Distance analysis, set union/intersection |

## Project Structure

```
MAvalonia/
├── MAvalonia.csproj        (Avalonia 11.3.12, net8.0, references WarpLib)
├── Program.cs              (entry point)
├── App.axaml / .cs         (Fluent theme)
├── MainWindow.axaml / .cs  (population management, species grid, refinement)
└── Dialogs/
    ├── DialogCreatePopulation.axaml / .cs
    ├── DialogDataSources.axaml / .cs
    ├── DialogCreateSourceFromSettings.axaml / .cs
    ├── DialogAddSpecies.axaml / .cs
    ├── DialogCreateNewSpecies.axaml / .cs
    ├── DialogRefinementSettings.axaml / .cs
    ├── DialogSpeciesShift.axaml / .cs
    ├── DialogSpeciesRotate.axaml / .cs
    ├── DialogSpeciesSymExpand.axaml / .cs
    └── DialogSpeciesParticleSets.axaml / .cs
```

**25 source files, ~751 lines.**

## Requirements

- .NET 8.0 SDK
- Linux (X11 or Wayland) or Windows
- For refinement: NVIDIA GPU with CUDA + `NativeAcceleration.so`

## Key WarpLib Classes

| Class | Purpose |
|-------|---------|
| `Population` | Container for species + data sources |
| `Species` | Particle set with map, symmetry, resolution |
| `DataSource` | Reference to Warp processing directory |
| `ProcessingOptionsMPARefine` | Refinement parameters |
| `WorkerWrapper` | GPU process for refinement |

## Porting Notes

Ported from `warp/M/` (WPF, ~5,000 lines across 28 files). Key changes:
- `MetroWindow` + `CustomDialog` → `Window` + `ShowDialog<T>()`
- `MahApps.Metro` theming → Avalonia Fluent theme
- `LiveCharts` (used in DialogCreateSourceFromSettings) → removed (not critical)
- `WarpControls` VolumeRenderer/MultiSliceRenderer → deferred to production
  (requires GPU for real-time rendering)
- `ProgressRing` → `ProgressBar IsIndeterminate=True`
