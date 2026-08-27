# Avalonia Port — Phase 5: Task Dialogs

## Context

Phases 1-4 delivered the complete settings panel, all visualization controls,
and MicrographDisplay. Phase 5 ports the 13 task dialogs that perform actual
processing operations (template matching, particle export, reconstruction, etc).

The user has requested a **functionally identical port** — every dialog must
preserve its full functionality, not just the UI layout.

## Inventory: 13 Dialogs, 5,578 Lines

### By priority (processing-critical first):

**Tier 1 — Core processing (port first):**
1. `DialogTomoReconstruction` (240 lines) — full tomogram reconstruction
2. `DialogTomoParticleExport` (763 lines) — sub-tomogram extraction
3. `Dialog2DParticleExport` (658 lines) — 2D particle extraction
4. `Dialog2DMatch` (314 lines) — 2D template matching
5. `DialogTomoMatch` (273 lines) — 3D template matching

**Tier 2 — Import/Export metadata:**
6. `DialogTomoImportImod` (832 lines) — IMOD tilt series import
7. `Dialog2DList` (274 lines) — micrograph list export
8. `DialogTomoList` (152 lines) — tomogram list export
9. `Dialog2DParticleImport` (211 lines) — particle coordinate import
10. `Dialog2DDefocusUpdate` (252 lines) — defocus refinement

**Tier 3 — BoxNet AI:**
11. `BoxNetSelect` (150 lines) — model selection
12. `BoxNetExport` (457 lines) — training data export
13. `BoxNetTrain` (1,002 lines) — model retraining

## Implementation Approach

Each dialog becomes an Avalonia `Window` (not UserControl) shown as a modal
dialog via `window.ShowDialog(owner)`. This replaces WPF's MahApps
`CustomDialog` pattern.

### Key WPF → Avalonia patterns for dialogs:

| WPF | Avalonia |
|-----|----------|
| `CustomDialog` (MahApps inline) | `Window.ShowDialog<T>(owner)` |
| `MetroProgressBar` | Avalonia `ProgressBar` |
| `ProgressRing` (MahApps) | Avalonia `ProgressBar IsIndeterminate=True` |
| `FolderBrowserDialog` (WinForms) | `StorageProvider.OpenFolderPickerAsync()` |
| `SaveFileDialog` (WinForms) | `StorageProvider.SaveFilePickerAsync()` |
| `Dispatcher.Invoke` | `Dispatcher.UIThread.InvokeAsync` |
| `Helper.ForEachGPU` | Same (WarpLib, cross-platform) |
| `WorkerWrapper` | Same (WarpLib, cross-platform) |

### WarpLib processing methods (all cross-platform, no changes needed):

All `Movie.*()` and `TiltSeries.*()` methods, `Star` I/O, `Image` operations,
`GPU.*` calls, and `WorkerWrapper` are in WarpLib (net8.0). They work on Linux
as-is — only the UI shell needs porting.

## Implementation Steps

### Step 1: Dialog base infrastructure
- Create `WarpAvalonia/Dialogs/` directory
- Create `DialogBase` helper with common patterns:
  - Progress bar management
  - Async processing with cancellation
  - File/folder picker wrappers
  - Status text updates
  - Close/cancel handling

### Step 2: Port Tier 2 metadata dialogs (simplest, no GPU)
- `DialogTomoList` (152 lines) — straightforward Star I/O
- `Dialog2DList` (274 lines) — Star I/O + motion data
- `Dialog2DParticleImport` (211 lines) — coordinate scaling
- `Dialog2DDefocusUpdate` (252 lines) — CTF interpolation

### Step 3: Port Tier 1 GPU processing dialogs
- `DialogTomoReconstruction` (240 lines)
- `Dialog2DMatch` / `DialogTomoMatch` (314 + 273 lines)
- `Dialog2DParticleExport` (658 lines)
- `DialogTomoParticleExport` (763 lines)

### Step 4: Port IMOD import (largest metadata dialog)
- `DialogTomoImportImod` (832 lines) — mdoc/xf parsing, matrix math

### Step 5: Port BoxNet dialogs
- `BoxNetSelect` (150 lines)
- `BoxNetExport` (457 lines)
- `BoxNetTrain` (1,002 lines) — training loop, needs LiveCharts replacement
  for loss curves (use custom Canvas Polyline, same as CTFDisplay)

### Step 6: Wire all dialogs to MainWindow task buttons
Connect the Overview tab task buttons to open the corresponding dialogs.

### Step 7: Port custom progress controls
- `FlatGridMiniature` — used in 2D match progress
- `ReconstructionMiniature` — used in tomo reconstruction progress
- `OldschoolBar` / `NyanBar` — progress indicators

## Files to Create

```
WarpAvalonia/Dialogs/
├── DialogTomoList.axaml/.cs
├── DialogTomoReconstruction.axaml/.cs
├── DialogTomoMatch.axaml/.cs
├── DialogTomoParticleExport.axaml/.cs
├── DialogTomoImportImod.axaml/.cs
├── Dialog2DList.axaml/.cs
├── Dialog2DMatch.axaml/.cs
├── Dialog2DParticleExport.axaml/.cs
├── Dialog2DParticleImport.axaml/.cs
├── Dialog2DDefocusUpdate.axaml/.cs
├── BoxNetSelect.axaml/.cs
├── BoxNetExport.axaml/.cs
└── BoxNetTrain.axaml/.cs
```

## Files to Modify
- `WarpAvalonia/MainWindow.axaml` — wire task buttons to dialog launchers
- `WarpAvalonia/MainWindow.axaml.cs` — dialog launch event handlers

## Verification
After each step: `dotnet build && dotnet run`
- Task buttons in Overview tab open corresponding dialogs
- Dialogs show all controls matching WPF layout
- Processing dialogs can execute with real data (requires GPU + test dataset)
- Progress bars update during processing
- Output files are generated correctly
