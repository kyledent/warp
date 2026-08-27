# Avalonia Port — Phase 7: Production Integration

## Context

Phases 1-5 delivered a complete GUI shell: settings panel, 11 visualization
controls, MicrographDisplay with 6 overlay layers, and 13 task dialogs with
full processing logic. All controls render synthetic demo data (`--demo`).

Phase 7 makes the app production-ready by connecting the GUI to live data:
file discovery, real Movie/TiltSeries loading, processing pipeline, statistics
updates, and item selection. This is the final integration step.

## Source References (WPF implementation to port)
- `Warp/FileDiscoverer.cs` — file system watching + incubation
- `Warp/MainWindow.xaml.cs` lines 745-774 — FileDiscoverer wiring
- `Warp/MainWindow.xaml.cs` lines 1108-1900 — processing pipeline
- `Warp/MainWindow.xaml.cs` lines 2057-2200 — statistics updates
- `WarpLib/Movie/Movie.cs` constructor — Movie loading from XML

## Implementation Steps

### Step 1: Port FileDiscoverer to async/await
Port `Warp/FileDiscoverer.cs` to Avalonia-compatible async code.
- Replace `BackgroundWorker` with `Task.Run` + `CancellationToken`
- Replace `FileSystemWatcher` (works on Linux via .NET 8)
- Keep incubation logic (1000ms stability check before processing)
- Keep thread-safe collections with `lock`
- Fire `FilesChanged` event on timer (throttled ~500ms)
- Create Movie/TiltSeries objects from discovered files

**Output:** `WarpAvalonia/Processing/FileDiscoverer.cs`

### Step 2: Wire FileDiscoverer to MainWindow
- Start discovery when data folder is set (via Input section browse button)
- `FilesChanged` event updates:
  - `ProcessingStatusBar.SetItems()` with real Movie items
  - Auto-select first item if none selected
  - Trigger `UpdateStatsAll()`
- Stop discovery when folder changes or app closes

### Step 3: Create ProcessingOrchestrator
Extract from WPF MainWindow.xaml.cs lines 1108-1900 into a standalone class:
- GPU device enumeration and worker creation
- Gain reference loading via `ImageHelper`
- Main processing loop: detect unprocessed items, dispatch to GPUs
- Per-item pipeline: CTF estimation → motion correction → picking → export
- Progress reporting via events (not Dispatcher.Invoke)
- Cancellation support via CancellationToken

**Output:** `WarpAvalonia/Processing/ProcessingOrchestrator.cs`

### Step 4: Implement UpdateStatsAll
Port the statistics update logic from WPF MainWindow lines 2057-2200:
- Determine processing status for each Movie (Processed/Outdated/Unprocessed/FilteredOut/Deselected)
- Assign color IDs (0-4) matching the status palette
- Update scatter plots with real values:
  - Defocus: `Movie.CTF.Defocus`
  - Resolution: `Movie.CTFResolutionEstimate`
  - Motion: `Movie.MeanFrameMovement`
  - Particles: `Movie.ParticleCount`
- Update donut chart with status counts
- Update astigmatism scatter with defocus delta/angle

### Step 5: Item selection → display update
- ProcessingStatusBar click → set `DisplayedMovie`
- `DisplayedMovie` changed → update:
  - CTFDisplay: load `Movie.PS1D`, `Movie.Simulated1D`, power spectrum from file
  - MicrographDisplay: load `Movie.AveragePath` image
- StatusBar highlights active item

### Step 6: Wire Start Processing button
Connect the button to `ProcessingOrchestrator`:
- Start: create orchestrator, begin processing loop
- Stop: cancel token, dispose workers
- Progress: update status bar text + processing status bar items
- On item processed: trigger `UpdateStatsAll()`

### Step 7: Settings persistence testing
- Save/Load `.settings` files (already wired to `Options.Save()`/`Options.Load()`)
- Test with real Warp settings files from existing projects
- Verify all option categories round-trip correctly

### Step 8: Error handling
- Try/catch around all file I/O operations
- GPU memory check before processing (`GPU.GetFreeMemory()`)
- User-facing error dialogs for common failures:
  - No GPU found
  - Settings file corrupt
  - Data folder doesn't exist
  - Gain reference dimensions mismatch

## Files to Create
- `WarpAvalonia/Processing/FileDiscoverer.cs` (~300 lines)
- `WarpAvalonia/Processing/ProcessingOrchestrator.cs` (~400 lines)

## Files to Modify
- `WarpAvalonia/MainWindow.axaml.cs` — wire FileDiscoverer, UpdateStatsAll, item selection, processing button
- `WarpAvalonia/Controls/ProcessingStatusBar.axaml.cs` — accept real Movie items instead of StatusItem structs
- `WarpAvalonia/Controls/CTFDisplay.axaml.cs` — add LoadFromMovie() method
- `WarpAvalonia/Controls/MicrographDisplay.axaml.cs` — add LoadFromMovie() method
- `WarpAvalonia/Controls/SingleAxisScatter.axaml.cs` — ScatterPoint context field for Movie reference

## Key Patterns

**FileDiscoverer event → UI update chain:**
```
FileDiscoverer.FilesChanged
  → StatusBar.SetItems(movies)
  → UpdateStatsAll()
    → scatter plots updated with real CTF/motion/particle values
    → donut chart updated with status counts
    → astigmatism plot updated
```

**Item selection chain:**
```
StatusBar click → DisplayedMovie = movie
  → CTFDisplay.LoadFromMovie(movie) — loads PS1D, simulated CTF, power spectrum
  → MicrographDisplay.LoadFromMovie(movie) — loads average image, particles, mask
```

**Processing loop:**
```
ProcessingOrchestrator.Start(options, movies, devices)
  → foreach unprocessed movie:
    → CTF estimation (if enabled)
    → motion correction (if enabled)
    → particle picking (if enabled)
    → export (if enabled)
    → movie.SaveMeta()
    → UpdateStatsAll()
```

## Verification

1. Set data folder to EMPIAR-11830 (or any processed dataset with .xml files)
2. FileDiscoverer finds all movies and populates status bar
3. Scatter plots show real defocus/resolution/motion values
4. Click a status bar item → CTF display shows real power spectrum + CTF fit
5. Click Real Space tab → micrograph display shows real average image
6. Start Processing on unprocessed items → progress updates live
7. Save/Load settings round-trips correctly
