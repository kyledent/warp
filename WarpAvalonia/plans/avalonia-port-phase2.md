# Avalonia Port — Phase 2: Complete Settings Panel + Processing Foundation

## Context

The PoC (Phase 1) validated that Avalonia can bind to WarpLib's `OptionsWarp`,
render BGRA32 bitmaps via `WriteableBitmap`, and port WPF custom controls
(`ValueSlider`, `SliceRenderer`). The app compiles and runs on Linux.

Phase 2 builds the complete settings panel (matching WPF Warp 1:1) and adds
the file discovery + processing orchestration that makes Warp actually useful.

## Status: COMPLETE (2026-03-28)

All 10 steps implemented. Full settings panel with all sections, native file
dialogs, and processing loop stub.

## Scope

Port the **left panel** of the WPF Warp MainWindow completely, plus the
processing infrastructure. Skip the right-panel visualizations (CTFDisplay,
MicrographDisplay, charts) for Phase 3.

## Files to Modify/Create

### Existing files to modify
- `WarpAvalonia/MainWindow.axaml` — expand settings panel to full WPF parity
- `WarpAvalonia/MainWindow.axaml.cs` — add processing orchestration

### New files to create

**Controls (ported from WarpControls):**
- `WarpAvalonia/Controls/DropMenu.axaml/.cs` — dropdown menu (from `WarpControls/DropMenu.xaml`)
- `WarpAvalonia/Controls/GridMiniature.axaml/.cs` — grid visualization (from `Warp/Controls/MainUI/GridMiniature.xaml`)
- `WarpAvalonia/Controls/StatusBar.axaml/.cs` — processing status (from `Warp/Controls/MainUI/StatusBar.xaml`)

**Processing infrastructure:**
- `WarpAvalonia/Processing/FileDiscoverer.cs` — port from `Warp/FileDiscoverer.cs` (replace BackgroundWorker with async/await)
- `WarpAvalonia/Processing/ProcessingOrchestrator.cs` — extract from MainWindow.xaml.cs lines 1108-1977

## Implementation Steps

### Step 1: Complete Input section
Expand the Input section to match WPF exactly:
- Data folder browser with extension popup (MRC/MRCS/EM/TIF/TIFF/EER/TomoSTAR/DAT)
- Recursive search toggle
- Processing folder path selector
- EER frame grouping controls
- Tomo dimensions (X/Y/Z) — already in PoC, refine layout

**Source reference:** `MainWindow.xaml` lines 75-155, binds to `Options.Import.*`

### Step 2: Complete Preprocessing section
- Gain reference file selector + correction toggle
- Defect map file selector + correction toggle
- Flip X, Flip Y, Transpose checkboxes

**Source reference:** `MainWindow.xaml` lines ~160-200, binds to `Options.Import.*`

### Step 3: Complete CTF section
- Enable/disable toggle (`ProcessCTF`)
- Window size, fitting range (Å min/max)
- Voltage, Cs, phase shift enable
- Amplitude, defocus range
- Use movie sum checkbox

**Source reference:** `MainWindow.xaml` lines ~210-280, binds to `Options.CTF.*`

### Step 4: Complete Motion section
- Enable/disable toggle (`ProcessMovement`)
- Range consideration with B-factor
- Grid dimensions (X/Y/Z sliders)

**Source reference:** `MainWindow.xaml` lines ~285-340, binds to `Options.Movement.*`

### Step 5: Models section with GridMiniature
- Port `GridMiniature` control (visual grid distribution display)
- CTF and Movement grid parameter sliders
- This is the first non-trivial custom control beyond the PoC set

**Source reference:** `MainWindow.xaml` lines ~345-400, `GridMiniature.xaml.cs` (~200 lines)

### Step 6: Pick Particles section
- Enable/disable toggle (`ProcessPicking`)
- BoxNet model selector (file browser)
- Particle diameter, data style, minimum score
- Distance from mask
- Extract toggle with box size and pixel size

**Source reference:** `MainWindow.xaml` lines ~405-470, binds to `Options.Picking.*`

### Step 7: Output section
- Skip first/last N frames
- Average, odd/even toggles

**Source reference:** `MainWindow.xaml` lines ~475-510

### Step 8: Start Processing button + orchestration
- Port the large processing button (gradient styling)
- Extract processing pipeline from `MainWindow.xaml.cs` lines 1108-1977
- Create `ProcessingOrchestrator` class that manages:
  - GPU device setup
  - Gain reference loading
  - Worker thread spawning
  - Per-item processing loop (CTF → motion → picking → export)
- Replace `Dispatcher.Invoke` with Avalonia `Dispatcher.UIThread.InvokeAsync`

### Step 9: File discovery
- Port `FileDiscoverer` from BackgroundWorker to async/await
- File system watching for new data
- Incubation period for partial writes

### Step 10: Avalonia file/folder dialogs
- Replace WPF `FolderBrowserDialog` with Avalonia `StorageProvider.OpenFolderPickerAsync`
- Replace `SaveFileDialog` with `StorageProvider.SaveFilePickerAsync`
- Wire up all browse buttons

## Key WPF → Avalonia Patterns

| WPF Pattern | Avalonia Equivalent |
|-------------|-------------------|
| `Dispatcher.Invoke(() => ...)` | `Dispatcher.UIThread.InvokeAsync(() => ...)` |
| `FolderBrowserDialog` | `StorageProvider.OpenFolderPickerAsync()` |
| `Visibility.Collapsed` | `IsVisible = false` |
| `RadioButton` in popup | Same, or `ToggleButton` group |
| `MahApps ToggleSwitch` | Avalonia `ToggleSwitch` (built-in) |
| `BackgroundWorker` | `Task.Run` + `CancellationToken` |

## Verification

After each step, run:
```bash
export PATH=$HOME/.dotnet:$PATH
cd warp
dotnet build WarpAvalonia/WarpAvalonia.csproj
dotnet run --project WarpAvalonia/WarpAvalonia.csproj
```

Visual checks:
1. Settings panel scrolls and all sections are visible
2. Changing sliders updates the status bar (PropertyChanged events)
3. File browser dialogs open and return paths
4. Start Processing button is visible at bottom of left panel
5. GridMiniature renders (if test data provided)

## Not in scope (Phase 3)
- CTFDisplay (charting library migration)
- MicrographDisplay (complex bitmap + overlays)
- Overview tab statistics/filtering plots
- Task dialogs (2D/Tomo export/import)
- M and Cube applications
- REST API integration
