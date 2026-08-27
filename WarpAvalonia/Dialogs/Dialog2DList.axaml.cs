using System;
using System.Collections.Generic;
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

namespace WarpAvalonia.Dialogs;

/// <summary>
/// Avalonia port of Warp.Controls.Dialog2DList.
/// Exports micrograph metadata to RELION STAR format with optional polishing data.
/// </summary>
public partial class Dialog2DList : Window
{
    public Movie[] Movies;
    public string? ExportPath;
    public OptionsWarp Options;

    public Dialog2DList(Movie[] movies, OptionsWarp options)
    {
        InitializeComponent();
        Movies = movies;
        Options = options;
        DataContext = Options;
    }

    private void ButtonCancel_OnClick(object? sender, RoutedEventArgs e) => Close();

    private async void ButtonWrite_OnClick(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Micrograph List",
            DefaultExtension = "star",
            FileTypeChoices = new[] { new FilePickerFileType("STAR Files") { Patterns = new[] { "*.star" } } }
        });
        if (file == null) return;
        ExportPath = file.Path.LocalPath;

        bool relative = CheckRelative.IsChecked == true;
        bool filter = CheckFilter.IsChecked == true;
        bool manual = CheckManual.IsChecked == true;

        ProgressWrite.IsVisible = true;
        PanelButtons.IsVisible = false;

        await Task.Run(async () =>
        {
            List<Movie> validMovies = Movies.Where(v =>
            {
                if (!filter && v.UnselectFilter && v.UnselectManual == null) return false;
                if (!manual && v.UnselectManual != null && (bool)v.UnselectManual) return false;
                if (Options.Tasks.MicListMakePolishing && (v.OptionsMovement == null || v.GridMovementX.Dimensions.Z <= 1)) return false;
                return true;
            }).ToList();

            string pathPrefix = "", pathPrefixAverage = "";
            if (validMovies.Count > 0 && relative)
            {
                Uri uriStar = new Uri(ExportPath);
                pathPrefix = Helper.PathToFolder(uriStar.MakeRelativeUri(new Uri(validMovies[0].Path)).ToString());
                if (pathPrefix == validMovies[0].Name) pathPrefix = "";
                pathPrefixAverage = Helper.PathToFolder(uriStar.MakeRelativeUri(new Uri(validMovies[0].AveragePath)).ToString());
            }

            await Dispatcher.UIThread.InvokeAsync(() => ProgressWrite.Maximum = validMovies.Count);

            bool includeCTF = validMovies.Any(v => v.OptionsCTF != null);
            bool includePolishing = validMovies.Any(v => v.OptionsMovement != null) && Options.Tasks.MicListMakePolishing;

            Star tableOut = new Star(new[] { "rlnMicrographName" });
            if (includeCTF)
            {
                tableOut.AddColumn("rlnMagnification");
                tableOut.AddColumn("rlnDetectorPixelSize");
                tableOut.AddColumn("rlnVoltage");
                tableOut.AddColumn("rlnSphericalAberration");
                tableOut.AddColumn("rlnAmplitudeContrast");
                tableOut.AddColumn("rlnPhaseShift");
                tableOut.AddColumn("rlnDefocusU");
                tableOut.AddColumn("rlnDefocusV");
                tableOut.AddColumn("rlnDefocusAngle");
                tableOut.AddColumn("rlnCtfMaxResolution");
                tableOut.AddColumn("rlnBeamTiltX");
                tableOut.AddColumn("rlnBeamTiltY");
            }
            if (includePolishing)
            {
                tableOut.AddColumn("rlnMicrographMetadata");
                if (validMovies.Count > 0)
                    Directory.CreateDirectory(validMovies[0].MotionTrackDir);
            }

            int r = 0;
            Parallel.ForEach(validMovies, new ParallelOptions { MaxDegreeOfParallelism = 4 }, movie =>
            {
                List<string> row = new List<string> { pathPrefixAverage + movie.RootName + ".mrc" };

                if (movie.OptionsCTF != null)
                    row.AddRange(new[]
                    {
                        (1e4M / movie.OptionsCTF.BinnedPixelSizeMean).ToString("F1", CultureInfo.InvariantCulture),
                        "1.0",
                        movie.CTF.Voltage.ToString("F1", CultureInfo.InvariantCulture),
                        movie.CTF.Cs.ToString("F4", CultureInfo.InvariantCulture),
                        movie.CTF.Amplitude.ToString("F3", CultureInfo.InvariantCulture),
                        (movie.CTF.PhaseShift * 180M).ToString("F1", CultureInfo.InvariantCulture),
                        ((movie.CTF.Defocus + movie.CTF.DefocusDelta / 2) * 1e4M).ToString("F1", CultureInfo.InvariantCulture),
                        ((movie.CTF.Defocus - movie.CTF.DefocusDelta / 2) * 1e4M).ToString("F1", CultureInfo.InvariantCulture),
                        movie.CTF.DefocusAngle.ToString("F1", CultureInfo.InvariantCulture),
                        movie.CTFResolutionEstimate.ToString("F1", CultureInfo.InvariantCulture),
                        movie.CTF.BeamTilt.X.ToString("F2", CultureInfo.InvariantCulture),
                        movie.CTF.BeamTilt.Y.ToString("F2", CultureInfo.InvariantCulture)
                    });
                else
                    row.AddRange(Helper.ArrayOfFunction(i => "0.0", 12));

                if (includePolishing)
                {
                    row.Add(Path.Combine(movie.MotionTrackDir, movie.RootName + ".star"));

                    MapHeader header = MapHeader.ReadFromFile(movie.DataPath);
                    float2[] motionTrack = movie.GetMotionTrack(new float2(0.5f, 0.5f), 1)
                        .Select(v => v / (float)Options.Import.PixelSize).ToArray();

                    StarParameters paramsTable = new StarParameters(
                        new[] { "rlnImageSizeX", "rlnImageSizeY", "rlnImageSizeZ",
                                "rlnMicrographMovieName", "rlnMicrographBinning",
                                "rlnMicrographOriginalPixelSize", "rlnMicrographDoseRate",
                                "rlnMicrographPreExposure", "rlnVoltage",
                                "rlnMicrographStartFrame", "rlnMotionModelVersion" },
                        new[] { header.Dimensions.X.ToString(), header.Dimensions.Y.ToString(),
                                header.Dimensions.Z.ToString(), pathPrefix + movie.Name,
                                Math.Pow(2.0, (double)Options.Import.BinTimes).ToString("F5"),
                                Options.Import.PixelSize.ToString("F5"),
                                Options.Import.DosePerAngstromFrame.ToString("F5"),
                                "0", Options.CTF.Voltage.ToString(), "1", "0" });

                    Star trackTable = new Star(new[]
                    {
                        Helper.ArrayOfSequence(1, motionTrack.Length + 1, 1).Select(v => v.ToString()).ToArray(),
                        motionTrack.Select(v => (-v.X).ToString("F5")).ToArray(),
                        motionTrack.Select(v => (-v.Y).ToString("F5")).ToArray()
                    }, new[] { "rlnMicrographFrameNumber", "rlnMicrographShiftX", "rlnMicrographShiftY" });

                    Star.SaveMultitable(Path.Combine(movie.MotionTrackDir, movie.RootName + ".star"),
                        new Dictionary<string, Star> { { "general", paramsTable }, { "global_shift", trackTable } });
                }

                lock (tableOut) tableOut.AddRow(row.ToArray());

                lock (this)
                {
                    r++;
                    if (r % 10 == 0 || r == validMovies.Count)
                    {
                        int progress = r;
                        Dispatcher.UIThread.InvokeAsync(() => ProgressWrite.Value = progress);
                    }
                }
            });

            tableOut.Save(ExportPath);
        });

        Close();
    }
}
