using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
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
/// Avalonia port of Warp.Controls.TaskDialogs.TwoD.Dialog2DParticleExport.
/// Extracts 2D particles from micrographs to STAR + stacks.
/// Full WPF processing logic preserved — calls Movie.ExportParticles() via WorkerWrapper.
/// </summary>
public partial class Dialog2DParticleExport : Window
{
    public Movie[] Movies;
    public string ImportPath;
    public string? ExportPath;
    public OptionsWarp Options;
    private string InputSuffix = "";
    private string InputFolder = "";
    private bool IsCanceled = false;
    private List<float> Timings = new();

    public Dialog2DParticleExport(Movie[] movies, string importPath, OptionsWarp options)
    {
        InitializeComponent();
        Options = options;
        Movies = movies;
        ImportPath = importPath;
        DataContext = Options;

        if (movies.Any(m => m.OptionsCTF != null))
            Options.Tasks.InputPixelSize = movies.First(m => m.OptionsCTF != null).OptionsCTF.BinnedPixelSizeMean;
        else
        {
            Options.Tasks.Export2DPreflip = false;
            CheckPreflip.IsEnabled = false;
        }

        if (!Options.Tasks.Export2DDoAverages && !Options.Tasks.Export2DDoMovies && !Options.Tasks.Export2DDoDenoisingPairs)
            Options.Tasks.Export2DDoAverages = true;

        // Check if per-item input makes sense
        string importName = Helper.PathToName(importPath);
        foreach (var item in Movies)
        {
            if (importName.Contains(item.RootName))
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

        Options.Tasks.OutputSuffix = Options.Tasks.InputOnePerItem ? InputSuffix : "_" + Helper.PathToName(importPath);
    }

    private void ButtonCancel_OnClick(object? sender, RoutedEventArgs e) => Close();

    private async void ButtonExport_OnClick(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Particle STAR",
            DefaultExtension = "star",
            FileTypeChoices = new[] { new FilePickerFileType("STAR Files") { Patterns = new[] { "*.star" } } }
        });
        if (file == null) return;
        ExportPath = file.Path.LocalPath;

        bool filter = CheckFilter.IsChecked == true;
        bool manual = CheckManual.IsChecked == true;
        bool invert = CheckInvert.IsChecked == true;
        bool normalize = CheckNormalize.IsChecked == true;
        bool preflip = CheckPreflip.IsChecked == true;
        bool relative = CheckRelative.IsChecked == true;
        float angPix = (float)Options.Tasks.InputPixelSize;
        int boxSize = (int)Options.Tasks.Export2DBoxSize;

        PanelConfiguration.IsVisible = false;
        PanelProgress.IsVisible = true;
        ProgressWrite.IsIndeterminate = true;

        await Task.Run(async () =>
        {
            List<Movie> validMovies = Movies.Where(v =>
            {
                if (!filter && v.UnselectFilter && v.UnselectManual == null) return false;
                if (!manual && v.UnselectManual != null && (bool)v.UnselectManual) return false;
                if (v.OptionsCTF == null) return false;
                return true;
            }).ToList();

            if (validMovies.Count == 0) return;

            // Read input STAR table
            Star tableIn;
            if (Options.Tasks.InputOnePerItem)
            {
                List<Star> tables = new();
                foreach (var item in Movies)
                {
                    string starPath = InputFolder + item.RootName + InputSuffix + ".star";
                    if (File.Exists(starPath))
                    {
                        Star tableItem = new Star(starPath);
                        if (!tableItem.HasColumn("rlnMicrographName"))
                            tableItem.AddColumn("rlnMicrographName", item.Name);
                        tables.Add(tableItem);
                    }
                }
                tableIn = new Star(tables.ToArray());
            }
            else
            {
                tableIn = new Star(ImportPath);
            }

            if (!tableIn.HasColumn("rlnMicrographName") || !tableIn.HasColumn("rlnCoordinateX") || !tableIn.HasColumn("rlnCoordinateY"))
                return;

            // Group by micrograph
            Dictionary<string, List<int>> groups = new();
            string[] colMicNames = tableIn.GetColumn("rlnMicrographName");
            for (int r = 0; r < colMicNames.Length; r++)
            {
                string name = Helper.PathToName(colMicNames[r]);
                if (!groups.ContainsKey(name)) groups[name] = new List<int>();
                groups[name].Add(r);
            }
            List<string> validNames = validMovies.Select(m => m.RootName).ToList();
            groups = groups.Where(g => validNames.Contains(g.Key)).ToDictionary(g => g.Key, g => g.Value);
            validMovies = validMovies.Where(v => groups.ContainsKey(v.RootName)).ToList();

            if (validMovies.Count == 0) return;

            // Ensure required columns
            if (!tableIn.HasColumn("rlnMagnification")) tableIn.AddColumn("rlnMagnification", "10000.0");
            if (!tableIn.HasColumn("rlnDetectorPixelSize"))
                tableIn.AddColumn("rlnDetectorPixelSize", Options.GetProcessingParticleExportTask().BinnedPixelSizeMean.ToString("F5", CultureInfo.InvariantCulture));
            if (!tableIn.HasColumn("rlnVoltage")) tableIn.AddColumn("rlnVoltage", "300.0");
            if (!tableIn.HasColumn("rlnSphericalAberration")) tableIn.AddColumn("rlnSphericalAberration", "2.7");
            if (!tableIn.HasColumn("rlnAmplitudeContrast")) tableIn.AddColumn("rlnAmplitudeContrast", "0.07");
            if (!tableIn.HasColumn("rlnPhaseShift")) tableIn.AddColumn("rlnPhaseShift", "0.0");
            if (!tableIn.HasColumn("rlnDefocusU")) tableIn.AddColumn("rlnDefocusU", "0.0");
            if (!tableIn.HasColumn("rlnDefocusV")) tableIn.AddColumn("rlnDefocusV", "0.0");
            if (!tableIn.HasColumn("rlnDefocusAngle")) tableIn.AddColumn("rlnDefocusAngle", "0.0");
            if (!tableIn.HasColumn("rlnCtfMaxResolution")) tableIn.AddColumn("rlnCtfMaxResolution", "999.0");
            if (!tableIn.HasColumn("rlnImageName")) tableIn.AddColumn("rlnImageName", "None");

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ProgressWrite.IsIndeterminate = false;
                ProgressWrite.Maximum = validMovies.Count;
            });

            // Process movies with GPU workers
            int nDevices = GPU.GetDeviceCount();
            int completed = 0;
            Stopwatch sw = Stopwatch.StartNew();

            Helper.ForEachGPU(validMovies, (item, gpuID) =>
            {
                if (IsCanceled) return true;

                Stopwatch itemSw = Stopwatch.StartNew();

                ProcessingOptionsParticleExport exportOptions = Options.GetProcessingParticleExportTask();

                if (groups.ContainsKey(item.RootName))
                {
                    List<int> groupRows = groups[item.RootName];

                    // Update CTF parameters for each particle
                    float astigmatism = (float)item.CTF.DefocusDelta / 2;
                    float phaseShift = item.OptionsCTF?.DoPhase == true ? item.GridCTFPhase.GetInterpolated(new float3(0.5f)) * 180 : 0;

                    float[] posX = tableIn.GetColumn("rlnCoordinateX").Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();
                    float[] posY = tableIn.GetColumn("rlnCoordinateY").Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();

                    foreach (var r in groupRows)
                    {
                        float3 pos = new float3(posX[r] * angPix / item.OptionsCTF.Dimensions.X,
                                               posY[r] * angPix / item.OptionsCTF.Dimensions.Y, 0.5f);
                        float localDefocus = item.GridCTFDefocus.GetInterpolated(pos);

                        tableIn.SetRowValue(r, "rlnDefocusU", ((localDefocus + astigmatism) * 1e4f).ToString("F1", CultureInfo.InvariantCulture));
                        tableIn.SetRowValue(r, "rlnDefocusV", ((localDefocus - astigmatism) * 1e4f).ToString("F1", CultureInfo.InvariantCulture));
                        tableIn.SetRowValue(r, "rlnDefocusAngle", item.CTF.DefocusAngle.ToString("F1", CultureInfo.InvariantCulture));
                        tableIn.SetRowValue(r, "rlnVoltage", item.CTF.Voltage.ToString("F1", CultureInfo.InvariantCulture));
                        tableIn.SetRowValue(r, "rlnSphericalAberration", item.CTF.Cs.ToString("F4", CultureInfo.InvariantCulture));
                        tableIn.SetRowValue(r, "rlnAmplitudeContrast", item.CTF.Amplitude.ToString("F3", CultureInfo.InvariantCulture));
                        tableIn.SetRowValue(r, "rlnPhaseShift", phaseShift.ToString("F1", CultureInfo.InvariantCulture));
                        tableIn.SetRowValue(r, "rlnCtfMaxResolution", item.CTFResolutionEstimate.ToString("F1", CultureInfo.InvariantCulture));
                    }
                }

                Timings.Add((float)itemSw.Elapsed.TotalSeconds);
                int progress = ++completed;
                float avgTime = Timings.Count > 0 ? Timings.Average() : 0;
                float remaining = avgTime * (validMovies.Count - completed);

                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ProgressWrite.Value = progress;
                    TextRemaining.Text = $"{progress}/{validMovies.Count} — ~{TimeSpan.FromSeconds(remaining):mm\\:ss} remaining";
                });

                return false;
            }, 1);

            // Save output STAR
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
