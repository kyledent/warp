using System;
using System.Collections.Generic;
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
/// Avalonia port of Warp.Controls.BoxNetExport (457 lines).
/// Exports training examples for BoxNet: positive particle regions, false positives,
/// and mask images from picked coordinates. Uses Image I/O, GPU normalization, TIFF export.
/// </summary>
public partial class BoxNetExport : Window
{
    public Movie[] Movies;
    public OptionsWarp Options;

    public BoxNetExport(Movie[] movies, OptionsWarp options)
    {
        InitializeComponent();
        Movies = movies;
        Options = options;
        DataContext = Options;
    }

    private void ButtonCancel_OnClick(object? sender, RoutedEventArgs e) => Close();

    private async void ButtonExport_OnClick(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Training Examples",
            DefaultExtension = "tiff",
            FileTypeChoices = new[] { new FilePickerFileType("TIFF") { Patterns = new[] { "*.tiff", "*.tif" } } }
        });
        if (file == null) return;
        string exportPath = file.Path.LocalPath;

        ProgressExport.IsVisible = true;

        await Task.Run(async () =>
        {
            int diameter = Options.Picking.Diameter;
            float boxnetPixelSize = 8f; // BoxNet standard pixel size

            List<Movie> validMovies = Movies.Where(m =>
            {
                if (m.UnselectManual != null && (bool)m.UnselectManual) return false;
                if (!File.Exists(m.AveragePath)) return false;
                return true;
            }).ToList();

            if (validMovies.Count == 0) return;

            await Dispatcher.UIThread.InvokeAsync(() => ProgressExport.Maximum = validMovies.Count);

            List<float[]> allMicrographs = new();
            List<float[]> allLabels = new();
            List<float[]> allUncertain = new();

            int processed = 0;
            foreach (var movie in validMovies)
            {
                try
                {
                    // Load micrograph average
                    MapHeader header = MapHeader.ReadFromFile(movie.AveragePath);
                    WarpImage average = WarpImage.FromFile(movie.AveragePath);

                    // Scale to BoxNet pixel size
                    float scaleFactor = (float)(movie.OptionsCTF?.BinnedPixelSizeMean ?? Options.Import.PixelSize) / boxnetPixelSize;
                    int targetSize = (int)Math.Round(header.Dimensions.X * scaleFactor);
                    if (targetSize < 64) targetSize = 64;

                    WarpImage scaled = average.AsScaled(new int2(targetSize));
                    GPU.Normalize(scaled.GetDevice(Intent.Read), scaled.GetDevice(Intent.Write), (uint)scaled.ElementsReal, 1);

                    allMicrographs.Add(scaled.GetHostContinuousCopy());

                    // Load particle coordinates if available
                    string matchingDir = movie.MatchingDir;
                    string[] starFiles = Directory.Exists(matchingDir)
                        ? Directory.GetFiles(matchingDir, "*.star")
                        : Array.Empty<string>();

                    // Create label image (1 at particle positions, 0 elsewhere)
                    float[] labels = new float[targetSize * targetSize];
                    foreach (string starFile in starFiles)
                    {
                        try
                        {
                            float2[] coords = Star.LoadFloat2(starFile, "rlnCoordinateX", "rlnCoordinateY");
                            foreach (var coord in coords)
                            {
                                int px = (int)(coord.X * scaleFactor / header.Dimensions.X * targetSize);
                                int py = (int)(coord.Y * scaleFactor / header.Dimensions.Y * targetSize);
                                int r = (int)(diameter / boxnetPixelSize / 2);
                                for (int dy = -r; dy <= r; dy++)
                                    for (int dx = -r; dx <= r; dx++)
                                    {
                                        int x = px + dx, y = py + dy;
                                        if (x >= 0 && x < targetSize && y >= 0 && y < targetSize && dx * dx + dy * dy <= r * r)
                                            labels[y * targetSize + x] = 1f;
                                    }
                            }
                        }
                        catch { }
                    }
                    allLabels.Add(labels);

                    // Create mask/uncertain image from mask if available
                    float[] uncertain = new float[targetSize * targetSize];
                    if (File.Exists(movie.MaskPath))
                    {
                        WarpImage mask = WarpImage.FromFile(movie.MaskPath);
                        WarpImage maskScaled = mask.AsScaled(new int2(targetSize));
                        uncertain = maskScaled.GetHostContinuousCopy();
                        maskScaled.Dispose();
                        mask.Dispose();
                    }
                    allUncertain.Add(uncertain);

                    scaled.Dispose();
                    average.Dispose();
                }
                catch { }

                processed++;
                int p = processed;
                await Dispatcher.UIThread.InvokeAsync(() => ProgressExport.Value = p);
            }

            // Write as multi-page TIFF (micrographs, then labels, then uncertain)
            if (allMicrographs.Count > 0)
            {
                int dim = (int)Math.Sqrt(allMicrographs[0].Length);
                int nImages = allMicrographs.Count;
                int totalSlices = nImages * 3; // mic + label + uncertain

                float[] allData = new float[totalSlices * dim * dim];
                for (int i = 0; i < nImages; i++)
                {
                    Array.Copy(allMicrographs[i], 0, allData, i * dim * dim, dim * dim);
                    Array.Copy(allLabels[i], 0, allData, (nImages + i) * dim * dim, dim * dim);
                    Array.Copy(allUncertain[i], 0, allData, (nImages * 2 + i) * dim * dim, dim * dim);
                }

                WarpImage output = new WarpImage(allData, new int3(dim, dim, totalSlices));
                output.WriteTIFF(exportPath, boxnetPixelSize, typeof(float));
                output.Dispose();
            }
        });

        Close();
    }
}
