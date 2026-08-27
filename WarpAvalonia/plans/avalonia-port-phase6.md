# Avalonia Port — Phase 6: M and Cube Applications

## Context

Phases 1-5 + 7 delivered a production-ready WarpAvalonia with file discovery,
real data loading, processing pipeline, and all 13 task dialogs. Phase 6 ports
the two companion GUI applications: M (multi-particle refinement, ~5,000 lines)
and Cube (3D coordinate viewer, ~2,100 lines).

Both share WarpLib and the WarpControls patterns already ported to Avalonia
(ValueSlider, etc). They become separate Avalonia projects referencing the
same WarpLib and the shared Avalonia controls.

## Inventory

**M Application (~5,000 lines):**
- MainWindow (67 XAML + 1,004 CS) — population management, species display, refinement
- 12 dialog controls (~3,250 CS total):
  - DialogRefinementSettings, DialogDataSources, DialogCreateSourceFromSettings
  - DialogAddSpecies, DialogCreateNewSpecies, DialogCreatePopulation
  - DialogSpeciesShift, DialogSpeciesRotate, DialogSpeciesSymExpand
  - DialogSpeciesParticleSets, DialogTomoParticleExport
  - SpeciesView (species card display)
- App.xaml (121 lines theming)

**Cube Application (~2,100 lines):**
- MainWindow (172 XAML + 1,715 CS) — tri-planar volume viewer + particle management
- CoordsImportWindow (75 XAML + 35 CS)
- CoordsExportWindow (75 XAML + 36 CS)

## Approach

Create two new Avalonia projects alongside WarpAvalonia:
- `MAvalonia/` — M companion app
- `CubeAvalonia/` — Cube coordinate viewer

Both reference WarpLib and can reuse controls from `WarpAvalonia/Controls/`.

## Implementation Steps

### Step 1: Cube (simpler app, ~2,100 lines)

Port Cube first as it's self-contained and simpler.

**CubeAvalonia/MainWindow:**
- Tri-planar volume viewer (XY, YZ, XZ slices) — reuse SliceRenderer pattern
- Particle overlay on each plane (Canvas + Ellipse, same as MicrographDisplay)
- Particle detail viewer (4-view sub-volume extraction)
- Intensity range + zoom controls (ValueSlider — already ported)
- Keyboard navigation (arrow keys, delete)
- Mouse interaction (click to position crosshairs, drag to pan)

**CubeAvalonia/CoordsImportWindow + CoordsExportWindow:**
- Simple modal dialogs with 3 ValueSliders (dimensions) + 3 checkboxes (axis inversion)
- Trivial port (~110 lines each)

### Step 2: M MainWindow

**MAvalonia/MainWindow:**
- Population management (create new, load existing)
- Species display grid (WrapPanel with SpeciesView cards)
- Data source management
- GPU stats display
- Refinement launch button
- Dark/light theme toggle

Key WarpLib classes: `Population`, `Species`, `Source`, `ProcessingOptionsMPARefine`

### Step 3: M Dialogs (12 total)

Port in dependency order:
1. DialogCreatePopulation — simple name/path input
2. DialogDataSources — source list with add/remove
3. DialogCreateSourceFromSettings — load from .settings file
4. DialogAddSpecies — species selection
5. DialogCreateNewSpecies — new species with STAR + map inputs
6. SpeciesView — species card with stats + action buttons
7. DialogRefinementSettings — complex settings panel (binds to ProcessingOptionsMPARefine)
8. DialogSpeciesShift/Rotate/SymExpand — geometry operations
9. DialogSpeciesParticleSets — particle set management
10. DialogTomoParticleExport — reuse from WarpAvalonia dialogs

### Step 4: M Refinement Pipeline

The core of M — multi-particle refinement:
- Worker process spawning across GPUs
- Refinement iteration loop with progress
- Species-level refinement with half-set processing
- FSC computation and resolution tracking

This calls `ProcessingOptionsMPARefine` and WarpLib's refinement methods.
The processing logic is framework-agnostic (WarpLib).

## Files to Create

```
MAvalonia/
+-- MAvalonia.csproj
+-- Program.cs
+-- App.axaml / .cs
+-- MainWindow.axaml / .cs
+-- Controls/
|   +-- SpeciesView.axaml / .cs
+-- Dialogs/
    +-- DialogCreatePopulation.axaml / .cs
    +-- DialogDataSources.axaml / .cs
    +-- DialogCreateSourceFromSettings.axaml / .cs
    +-- DialogAddSpecies.axaml / .cs
    +-- DialogCreateNewSpecies.axaml / .cs
    +-- DialogRefinementSettings.axaml / .cs
    +-- DialogSpeciesShift.axaml / .cs
    +-- DialogSpeciesRotate.axaml / .cs
    +-- DialogSpeciesSymExpand.axaml / .cs
    +-- DialogSpeciesParticleSets.axaml / .cs

CubeAvalonia/
+-- CubeAvalonia.csproj
+-- Program.cs
+-- App.axaml / .cs
+-- MainWindow.axaml / .cs
+-- CoordsImportWindow.axaml / .cs
+-- CoordsExportWindow.axaml / .cs
```

## Verification

**Cube:**
- Load a reconstructed tomogram MRC file
- Three orthogonal slice views render correctly
- Import particle coordinates from STAR file
- Particles displayed as circles on each plane
- Arrow keys navigate between particles
- Export coordinates to STAR

**M:**
- Create new population
- Add data source from Warp .settings file
- Add species with map + STAR
- Species cards display in grid
- Refinement settings dialog shows all parameters
- Start refinement (requires GPU + real data)
