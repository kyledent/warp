using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Warp;
using Warp.Headers;
using Warp.Tools;

namespace WarpAvalonia.Dialogs;

/// <summary>
/// Avalonia port of Warp.Controls.Dialog2DParticleImport.
/// Imports particle coordinates from STAR files with coordinate scaling and per-micrograph output.
/// </summary>
public partial class Dialog2DParticleImport : Window
{
    public Movie[] Movies;
    public string ImportPath;
    public OptionsWarp Options;

    public Dialog2DParticleImport(Movie[] movies, string importPath, OptionsWarp options)
    {
        InitializeComponent();
        Movies = movies;
        Options = options;
        ImportPath = importPath;
        DataContext = Options;

        TextSuffix.Text = Helper.PathToName(importPath);

        Star tableIn = new Star(importPath, "", null, 1);
        if (tableIn.RowCount > 0 && tableIn.HasColumn("rlnMagnification") && tableIn.HasColumn("rlnDetectorPixelSize"))
        {
            Options.Tasks.InputPixelSize = (decimal)(tableIn.GetRowValueFloat(0, "rlnDetectorPixelSize") / tableIn.GetRowValueFloat(0, "rlnMagnification")) * 1e4M;
            Options.Tasks.InputShiftPixelSize = Options.Tasks.InputPixelSize;
        }
    }

    private void ButtonCancel_OnClick(object? sender, RoutedEventArgs e) => Close();

    private async void ButtonWrite_OnClick(object? sender, RoutedEventArgs e)
    {
        string suffix = TextSuffix.Text ?? "";
        float scaleCoords = (float)(Options.Tasks.InputPixelSize / Options.Import.BinnedPixelSize);
        float scaleShifts = (float)(Options.Tasks.InputShiftPixelSize / Options.Import.BinnedPixelSize);

        ProgressWrite.IsVisible = true;
        PanelButtons.IsVisible = false;
        IsEnabled = false;

        await Task.Run(async () =>
        {
            Star tableIn = new Star(ImportPath);

            if (tableIn.RowCount == 0 || !tableIn.HasColumn("rlnCoordinateX") || !tableIn.HasColumn("rlnCoordinateY") || !tableIn.HasColumn("rlnMicrographName"))
                return;

            if (Movies.Length == 0) return;

            Directory.CreateDirectory(Movies[0].MatchingDir);
            await Dispatcher.UIThread.InvokeAsync(() => ProgressWrite.Maximum = Movies.Length);

            int indexMicName = tableIn.GetColumnID("rlnMicrographName");
            tableIn.ModifyAllValuesInColumn("rlnMicrographName", v => Helper.PathToName(v));

            int indexCoordX = tableIn.GetColumnID("rlnCoordinateX");
            int indexCoordY = tableIn.GetColumnID("rlnCoordinateY");

            // Subtract origin from coordinates
            int indexOriginX = tableIn.GetColumnID("rlnOriginX");
            if (indexOriginX >= 0)
            {
                tableIn.ModifyAllValuesInColumn("rlnCoordinateX", (v, r) =>
                    (float.Parse(v) * scaleCoords - float.Parse(tableIn.GetRowValue(r, indexOriginX)) * scaleShifts).ToString("F4"));
                tableIn.SetColumn("rlnOriginX", Helper.ArrayOfConstant("0.0", tableIn.RowCount));
            }
            else
                tableIn.ModifyAllValuesInColumn("rlnCoordinateX", v => (float.Parse(v) * scaleCoords).ToString("F4"));

            int indexOriginY = tableIn.GetColumnID("rlnOriginY");
            if (indexOriginY >= 0)
            {
                tableIn.ModifyAllValuesInColumn("rlnCoordinateY", (v, r) =>
                    (float.Parse(v) * scaleCoords - float.Parse(tableIn.GetRowValue(r, indexOriginY)) * scaleShifts).ToString("F4"));
                tableIn.SetColumn("rlnOriginY", Helper.ArrayOfConstant("0.0", tableIn.RowCount));
            }
            else
                tableIn.ModifyAllValuesInColumn("rlnCoordinateY", v => (float.Parse(v) * scaleCoords).ToString("F4"));

            var groupedRows = tableIn.GetAllRows().GroupBy(row => row[indexMicName]).ToDictionary(g => g.Key, g => g.ToList());

            foreach (var movie in Movies)
            {
                if (groupedRows.ContainsKey(movie.RootName))
                {
                    int3 dims = MapHeader.ReadFromFile(movie.DataPath,
                        new int2(Options.Import.HeaderlessWidth, Options.Import.HeaderlessHeight),
                        Options.Import.HeaderlessOffset,
                        ImageFormatsHelper.StringToType(Options.Import.HeaderlessType)).Dimensions;
                    float3 binnedDims = new float3(dims) * (float)Options.Import.PixelSize / (float)Options.Import.BinnedPixelSize;

                    List<string[]> rows = groupedRows[movie.RootName];
                    foreach (var row in rows)
                    {
                        row[indexMicName] = movie.Name;
                        float coordX = float.Parse(row[indexCoordX]);
                        if (Options.Tasks.InputFlipX) coordX = binnedDims.X - coordX;
                        float coordY = float.Parse(row[indexCoordY]);
                        if (Options.Tasks.InputFlipY) coordY = binnedDims.Y - coordY;
                        row[indexCoordX] = coordX.ToString("F4");
                        row[indexCoordY] = coordY.ToString("F4");
                    }

                    Star tableOut = new Star(tableIn.GetColumnNames());
                    foreach (var row in rows) tableOut.AddRow(row);
                    tableOut.Save(Path.Combine(movie.MatchingDir, movie.RootName + "_" + suffix + ".star"));
                    movie.UpdateParticleCount("_" + suffix);
                }

                await Dispatcher.UIThread.InvokeAsync(() => ProgressWrite.Value++);
            }
        });

        Close();
    }
}
