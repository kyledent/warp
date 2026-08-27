using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Warp;
using Warp.Headers;
using Warp.Tools;
using WarpImage = Warp.Image;

namespace WarpAvalonia.Dialogs;

/// <summary>
/// Avalonia port of Warp.Controls.DialogTomoParticleExport.
/// Exports sub-tomograms from particle coordinates with 3D reconstruction.
/// Uses WorkerWrapper for GPU-accelerated extraction via TiltSeries.ReconstructSubtomos().
/// </summary>
public partial class DialogTomoParticleExport : Window
{
    public TiltSeries[] Series;
    public string ImportPath;
    public string? ExportPath;
    public OptionsWarp Options;
    private string InputSuffix = "";
    private string InputFolder = "";
    private bool IsCanceled = false;
    private List<float> Timings = new();

    public DialogTomoParticleExport(TiltSeries[] series, string importPath, OptionsWarp options)
    {
        InitializeComponent();
        Options = options;
        Series = series;
        ImportPath = importPath;
        DataContext = Options;

        if (series.Any(m => m.OptionsCTF != null))
            Options.Tasks.InputPixelSize = series.First(m => m.OptionsCTF != null).OptionsCTF.BinnedPixelSizeMean;

        // Check per-item input
        string importName = Helper.PathToNameWithExtension(importPath);
        foreach (var item in Series)
        {
            if (importName.Contains(item.RootName + "_"))
            {
                InputSuffix = importName.Substring(item.RootName.Length);
                CheckOneInputPerItem.IsEnabled = true;
                Options.Tasks.InputOnePerItem = true;
                InputFolder = new FileInfo(importPath).DirectoryName ?? "";
                if (InputFolder.Length > 0 && InputFolder.Last() != '/' && InputFolder.Last() != '\\')
                    InputFolder += "/";
                break;
            }
        }
    }

    private void ButtonCancel_OnClick(object? sender, RoutedEventArgs e) => Close();

    private async void ButtonWrite_OnClick(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Sub-Tomogram STAR",
            DefaultExtension = "star",
            FileTypeChoices = new[] { new FilePickerFileType("STAR Files") { Patterns = new[] { "*.star" } } }
        });
        if (file == null) return;
        ExportPath = file.Path.LocalPath;

        bool filter = CheckFilter.IsChecked == true;
        bool manual = CheckManual.IsChecked == true;
        bool doVolumes = RadioVolume.IsChecked == true;
        float3 additionalShift = CheckShiftParticles.IsChecked == true
            ? new float3((float)SliderShiftParticlesX.Value, (float)SliderShiftParticlesY.Value, (float)SliderShiftParticlesZ.Value)
            : new float3(0);

        PanelConfiguration.IsVisible = false;
        PanelProgress.IsVisible = true;
        ProgressWrite.IsIndeterminate = true;

        await Task.Run(async () =>
        {
            List<TiltSeries> validSeries = Series.Where(v =>
            {
                if (!filter && v.UnselectFilter && v.UnselectManual == null) return false;
                if (!manual && v.UnselectManual != null && (bool)v.UnselectManual) return false;
                if (v.OptionsCTF == null) return false;
                return true;
            }).ToList();

            if (validSeries.Count == 0) return;

            // Read input STAR table
            Star tableIn;
            if (Options.Tasks.InputOnePerItem)
            {
                List<Star> tables = new();
                foreach (var item in Series)
                {
                    string starPath = InputFolder + item.RootName + InputSuffix;
                    if (File.Exists(starPath))
                    {
                        Star tableItem = new Star(starPath);
                        if (!tableItem.HasColumn("rlnMicrographName"))
                            tableItem.AddColumn("rlnMicrographName", item.Name);
                        else
                            tableItem.SetColumn("rlnMicrographName", Helper.ArrayOfConstant(item.Name, tableItem.RowCount));
                        tables.Add(tableItem);
                    }
                }
                tableIn = new Star(tables.ToArray());
            }
            else
            {
                tableIn = new Star(ImportPath);
            }

            if (!tableIn.HasColumn("rlnMicrographName") || !tableIn.HasColumn("rlnCoordinateX") ||
                !tableIn.HasColumn("rlnCoordinateY") || !tableIn.HasColumn("rlnCoordinateZ"))
                return;

            // Group by tilt series
            Dictionary<string, List<int>> groups = new();
            string[] colMicNames = tableIn.GetColumn("rlnMicrographName");
            for (int r = 0; r < colMicNames.Length; r++)
            {
                string name = Helper.PathToName(colMicNames[r]);
                if (!groups.ContainsKey(name)) groups[name] = new List<int>();
                groups[name].Add(r);
            }
            List<string> validNames = validSeries.Select(m => m.RootName).ToList();
            groups = groups.Where(g => validNames.Any(n => g.Key == n)).ToDictionary(g => g.Key, g => g.Value);
            validSeries = validSeries.Where(v => groups.Any(n => n.Key == v.RootName)).ToList();

            if (validSeries.Count == 0) return;

            // Ensure columns
            if (!tableIn.HasColumn("rlnMagnification")) tableIn.AddColumn("rlnMagnification", "10000.0");
            if (!tableIn.HasColumn("rlnDetectorPixelSize"))
                tableIn.AddColumn("rlnDetectorPixelSize", Options.Tasks.TomoSubReconstructPixel.ToString("F5", CultureInfo.InvariantCulture));
            if (!tableIn.HasColumn("rlnCtfMaxResolution")) tableIn.AddColumn("rlnCtfMaxResolution", "999.0");
            if (!tableIn.HasColumn("rlnImageName")) tableIn.AddColumn("rlnImageName", "None");
            if (!tableIn.HasColumn("rlnCtfImage")) tableIn.AddColumn("rlnCtfImage", "None");

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ProgressWrite.IsIndeterminate = false;
                ProgressWrite.Maximum = validSeries.Count;
            });

            // GPU processing
            int nDevices = GPU.GetDeviceCount();
            int completed = 0;

            Helper.ForEachGPU(validSeries, (item, gpuID) =>
            {
                if (IsCanceled) return true;
                Stopwatch itemSw = Stopwatch.StartNew();

                ProcessingOptionsTomoSubReconstruction exportOptions = Options.GetProcessingTomoSubReconstruction();

                if (!groups.ContainsKey(item.RootName)) { return false; }
                ProcessingOptionsTomoSubReconstruction exportOpts = Options.GetProcessingTomoSubReconstruction();

                List<int> groupRows = groups[item.RootName];

                // Extract coordinates and angles from STAR table
                float[] posX = tableIn.GetColumn("rlnCoordinateX").Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();
                float[] posY = tableIn.GetColumn("rlnCoordinateY").Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();
                float[] posZ = tableIn.GetColumn("rlnCoordinateZ").Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();
                float[] shiftX = tableIn.HasColumn("rlnOriginX") ? tableIn.GetColumn("rlnOriginX").Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray() : new float[tableIn.RowCount];
                float[] shiftY = tableIn.HasColumn("rlnOriginY") ? tableIn.GetColumn("rlnOriginY").Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray() : new float[tableIn.RowCount];
                float[] shiftZ = tableIn.HasColumn("rlnOriginZ") ? tableIn.GetColumn("rlnOriginZ").Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray() : new float[tableIn.RowCount];

                // Convert coordinates to physical units (Angstroms)
                float inputPix = (float)Options.Tasks.InputPixelSize;
                foreach (var r in groupRows)
                {
                    posX[r] = (posX[r] - shiftX[r]) * inputPix;
                    posY[r] = (posY[r] - shiftY[r]) * inputPix;
                    posZ[r] = (posZ[r] - shiftZ[r]) * inputPix;
                }

                float[] angleRot = tableIn.HasColumn("rlnAngleRot") ? tableIn.GetColumn("rlnAngleRot").Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray() : new float[tableIn.RowCount];
                float[] angleTilt = tableIn.HasColumn("rlnAngleTilt") ? tableIn.GetColumn("rlnAngleTilt").Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray() : new float[tableIn.RowCount];
                float[] anglePsi = tableIn.HasColumn("rlnAnglePsi") ? tableIn.GetColumn("rlnAnglePsi").Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray() : new float[tableIn.RowCount];

                // Apply additional shift if requested
                if (additionalShift.Length() > 0)
                {
                    foreach (var r in groupRows)
                    {
                        Matrix3 R = Matrix3.Euler(angleRot[r] * Helper.ToRad, angleTilt[r] * Helper.ToRad, anglePsi[r] * Helper.ToRad);
                        float3 rotatedShift = R * additionalShift;
                        posX[r] += rotatedShift.X;
                        posY[r] += rotatedShift.Y;
                        posZ[r] += rotatedShift.Z;
                    }
                }

                // Update STAR table with binned coordinates and output paths
                int pi = 0;
                foreach (var r in groupRows)
                {
                    tableIn.SetRowValue(r, "rlnCoordinateX", (posX[r] / (float)exportOpts.BinnedPixelSizeMean).ToString("F3", CultureInfo.InvariantCulture));
                    tableIn.SetRowValue(r, "rlnCoordinateY", (posY[r] / (float)exportOpts.BinnedPixelSizeMean).ToString("F3", CultureInfo.InvariantCulture));
                    tableIn.SetRowValue(r, "rlnCoordinateZ", (posZ[r] / (float)exportOpts.BinnedPixelSizeMean).ToString("F3", CultureInfo.InvariantCulture));
                    tableIn.SetRowValue(r, "rlnCtfMaxResolution", item.CTFResolutionEstimate.ToString("F1", CultureInfo.InvariantCulture));

                    string pathSubtomo = Path.Combine(item.SubtomoDir, $"{item.RootName}{exportOpts.Suffix}_{pi:D7}_{exportOpts.BinnedPixelSizeMean:F2}A.mrc");
                    string pathCTF = Path.Combine(item.SubtomoDir, $"{item.RootName}{exportOpts.Suffix}_{pi:D7}_ctf_{exportOpts.BinnedPixelSizeMean:F2}A.mrc");
                    tableIn.SetRowValue(r, "rlnImageName", pathSubtomo);
                    tableIn.SetRowValue(r, "rlnCtfImage", pathCTF);
                    pi++;
                }

                // Build position and angle arrays for reconstruction
                float3[] tomoPositions = Helper.Combine(groupRows.Select(r => Helper.ArrayOfConstant(new float3(posX[r], posY[r], posZ[r]), item.NTilts)).ToArray());
                float3[] tomoAngles = Helper.Combine(groupRows.Select(r => Helper.ArrayOfConstant(new float3(angleRot[r], angleTilt[r], anglePsi[r]), item.NTilts)).ToArray());

                // Reconstruct sub-tomograms
                if (doVolumes)
                    item.ReconstructSubtomos(exportOpts, tomoPositions, tomoAngles);

                Timings.Add((float)itemSw.Elapsed.TotalSeconds);
                int progress = ++completed;
                float remaining = Timings.Average() * (validSeries.Count - completed);

                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ProgressWrite.Value = progress;
                    TextRemaining.Text = $"{progress}/{validSeries.Count} — ~{TimeSpan.FromSeconds(remaining):mm\\:ss} remaining";
                });

                return false;
            }, 1);

            tableIn.Save(ExportPath);
        });

        Close();
    }

    private void ButtonAbort_OnClick(object? sender, RoutedEventArgs e)
    {
        ButtonAbort.IsEnabled = false;
        IsCanceled = true;
    }
}
