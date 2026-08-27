using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Warp;
using Warp.Tools;

namespace WarpAvalonia.Dialogs;

/// <summary>
/// Avalonia port of Warp.Controls.Dialog2DDefocusUpdate.
/// Updates particle defocus values by interpolating local CTF defocus at particle coordinates.
/// </summary>
public partial class Dialog2DDefocusUpdate : Window
{
    public Movie[] Movies;
    public string ImportPath;
    public string? ExportPath;
    public OptionsWarp Options;

    public Dialog2DDefocusUpdate(Movie[] movies, string importPath, OptionsWarp options)
    {
        InitializeComponent();
        Movies = movies;
        ImportPath = importPath;
        Options = options;

        if (movies.Length > 0)
            Options.Tasks.InputPixelSize = movies.First(m => m.OptionsCTF != null).OptionsCTF.BinnedPixelSizeMean;

        DataContext = Options;
    }

    private void ButtonCancel_OnClick(object? sender, RoutedEventArgs e) => Close();

    private async void ButtonWrite_OnClick(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Updated STAR",
            DefaultExtension = "star",
            FileTypeChoices = new[] { new FilePickerFileType("STAR Files") { Patterns = new[] { "*.star" } } }
        });
        if (file == null) return;
        ExportPath = file.Path.LocalPath;

        bool skip = RadioSkip.IsChecked == true;
        bool delete = RadioDelete.IsChecked == true;
        float angPix = (float)Options.Tasks.InputPixelSize;
        bool filter = CheckFilter.IsChecked == true;
        bool manual = CheckManual.IsChecked == true;

        ProgressWrite.IsVisible = true;
        PanelButtons.IsVisible = false;
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
            List<string> validMovieNames = validMovies.Select(m => m.RootName).ToList();

            Star tableIn = new Star(ImportPath);
            if (!tableIn.HasColumn("rlnMicrographName") || !tableIn.HasColumn("rlnCoordinateX") || !tableIn.HasColumn("rlnCoordinateY"))
                throw new Exception("Missing required columns: rlnMicrographName, rlnCoordinateX, rlnCoordinateY");

            // Group rows by micrograph
            Dictionary<string, List<int>> groups = new();
            string[] colMicNames = tableIn.GetColumn("rlnMicrographName");
            for (int r = 0; r < colMicNames.Length; r++)
            {
                string name = Helper.PathToName(colMicNames[r]);
                if (!groups.ContainsKey(name)) groups[name] = new List<int>();
                groups[name].Add(r);
            }
            groups = groups.Where(g => validMovieNames.Contains(g.Key)).ToDictionary(g => g.Key, g => g.Value);

            // Track which rows are included
            bool[] rowsIncluded = new bool[tableIn.RowCount];
            foreach (var g in groups) foreach (var r in g.Value) rowsIncluded[r] = true;
            List<int> rowsNotIncluded = new();
            for (int r = 0; r < rowsIncluded.Length; r++) if (!rowsIncluded[r]) rowsNotIncluded.Add(r);

            // Ensure columns exist
            if (!tableIn.HasColumn("rlnVoltage")) tableIn.AddColumn("rlnVoltage", "300.0");
            if (!tableIn.HasColumn("rlnSphericalAberration")) tableIn.AddColumn("rlnSphericalAberration", "2.7");
            if (!tableIn.HasColumn("rlnAmplitudeContrast")) tableIn.AddColumn("rlnAmplitudeContrast", "0.07");
            if (!tableIn.HasColumn("rlnPhaseShift")) tableIn.AddColumn("rlnPhaseShift", "0.0");
            if (!tableIn.HasColumn("rlnDefocusU")) tableIn.AddColumn("rlnDefocusU", "0.0");
            if (!tableIn.HasColumn("rlnDefocusV")) tableIn.AddColumn("rlnDefocusV", "0.0");
            if (!tableIn.HasColumn("rlnDefocusAngle")) tableIn.AddColumn("rlnDefocusAngle", "0.0");
            if (!tableIn.HasColumn("rlnCtfMaxResolution")) tableIn.AddColumn("rlnCtfMaxResolution", "999.0");

            await Dispatcher.UIThread.InvokeAsync(() => ProgressWrite.Maximum = validMovies.Count);

            float[] posX = tableIn.GetColumn("rlnCoordinateX").Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();
            float[] posY = tableIn.GetColumn("rlnCoordinateY").Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();

            int i = 0;
            foreach (var movie in validMovies)
            {
                if (groups.ContainsKey(movie.RootName))
                {
                    List<int> groupRows = groups[movie.RootName];
                    float astigmatism = (float)movie.CTF.DefocusDelta / 2;
                    float phaseShift = movie.OptionsCTF.DoPhase ? movie.GridCTFPhase.GetInterpolated(new float3(0.5f)) * 180 : 0;

                    foreach (var r in groupRows)
                    {
                        float3 position = new float3(posX[r] * angPix / movie.OptionsCTF.Dimensions.X,
                                                     posY[r] * angPix / movie.OptionsCTF.Dimensions.Y, 0.5f);
                        float localDefocus = movie.GridCTFDefocus.GetInterpolated(position);

                        tableIn.SetRowValue(r, "rlnDefocusU", ((localDefocus + astigmatism) * 1e4f).ToString("F1", CultureInfo.InvariantCulture));
                        tableIn.SetRowValue(r, "rlnDefocusV", ((localDefocus - astigmatism) * 1e4f).ToString("F1", CultureInfo.InvariantCulture));
                        tableIn.SetRowValue(r, "rlnDefocusAngle", movie.CTF.DefocusAngle.ToString("F1", CultureInfo.InvariantCulture));
                        tableIn.SetRowValue(r, "rlnVoltage", movie.CTF.Voltage.ToString("F1", CultureInfo.InvariantCulture));
                        tableIn.SetRowValue(r, "rlnSphericalAberration", movie.CTF.Cs.ToString("F4", CultureInfo.InvariantCulture));
                        tableIn.SetRowValue(r, "rlnAmplitudeContrast", movie.CTF.Amplitude.ToString("F3", CultureInfo.InvariantCulture));
                        tableIn.SetRowValue(r, "rlnPhaseShift", phaseShift.ToString("F1", CultureInfo.InvariantCulture));
                        tableIn.SetRowValue(r, "rlnCtfMaxResolution", movie.CTFResolutionEstimate.ToString("F1", CultureInfo.InvariantCulture));
                    }
                }

                int progress = ++i;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ProgressWrite.IsIndeterminate = false;
                    ProgressWrite.Value = progress;
                });
            }

            if (delete) tableIn.RemoveRows(rowsNotIncluded.ToArray());
            tableIn.Save(ExportPath);
        });

        Close();
    }
}
