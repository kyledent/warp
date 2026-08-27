using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Warp;
using Warp.Headers;
using Warp.Tools;
using WarpImage = Warp.Image;

namespace WarpAvalonia.Dialogs;

/// <summary>
/// Avalonia port of Warp.Controls.TaskDialogs.TwoD.Dialog2DMatch.
/// Template matching across 2D micrographs using GPU acceleration.
/// </summary>
public partial class Dialog2DMatch : Window
{
    private Movie[] Movies;
    private string PathTemplate;
    private OptionsWarp Options;
    private bool IsCanceled = false;

    public Dialog2DMatch(Movie[] movies, string pathTemplate, OptionsWarp options)
    {
        InitializeComponent();
        Movies = movies;
        PathTemplate = pathTemplate;
        Options = options;
        DataContext = Options;

        TextTemplateName.Text = Helper.PathToNameWithExtension(pathTemplate);
        MapHeader header = MapHeader.ReadFromFile(pathTemplate);
        if (Math.Abs(header.PixelSize.X - 1f) > 1e-6f)
            Options.Tasks.TomoMatchTemplatePixel = (decimal)header.PixelSize.X;
    }

    private void ButtonCancel_OnClick(object? sender, RoutedEventArgs e) => Close();

    private async void ButtonMatch_OnClick(object? sender, RoutedEventArgs e)
    {
        bool filter = CheckFilter.IsChecked == true;
        bool manual = CheckManual.IsChecked == true;

        List<Movie> validMovies = Movies.Where(v =>
        {
            if (!filter && v.UnselectFilter && v.UnselectManual == null) return false;
            if (!manual && v.UnselectManual != null && (bool)v.UnselectManual) return false;
            if (v.OptionsCTF == null) return false;
            return true;
        }).ToList();

        if (validMovies.Count == 0) { Close(); return; }

        PanelConfiguration.IsVisible = false;
        PanelProgress.IsVisible = true;

        int nParallel = Math.Min(validMovies.Count, GPU.GetDeviceCount());
        int completed = 0;
        ProgressOverall.Maximum = validMovies.Count;

        await Task.Run(() =>
        {
            // Load template and make copies for all devices
            WarpImage[] template = new WarpImage[nParallel];
            template[0] = WarpImage.FromFile(PathTemplate);
            template[0].PixelSize = (float)Options.Tasks.TomoMatchTemplatePixel;
            for (int i = 1; i < template.Length; i++)
                template[i] = template[0].GetCopy();

            // Load gain/defect references
            WarpImage?[] imageGain = new WarpImage?[nParallel];
            DefectModel?[] defectMap = new DefectModel?[nParallel];
            if (!string.IsNullOrEmpty(Options.Import.GainPath) && Options.Import.CorrectGain && File.Exists(Options.Import.GainPath))
                for (int d = 0; d < nParallel; d++)
                    imageGain[d] = ImageHelper.LoadAndPrepareGainReference(Options);
            if (!string.IsNullOrEmpty(Options.Import.DefectsPath) && Options.Import.CorrectDefects && File.Exists(Options.Import.DefectsPath))
                for (int d = 0; d < nParallel; d++)
                    defectMap[d] = ImageHelper.LoadAndPrepareDefectMap(Options);

            Helper.ForEachGPU(validMovies, (item, gpuID) =>
            {
                if (IsCanceled) return true;

                Dispatcher.UIThread.InvokeAsync(() => TextProgress.Text = $"GPU {gpuID}: Loading data...");

                ProcessingOptionsFullMatch matchOptions = Options.GetProcessingFullMatch();
                matchOptions.TemplateName = Helper.PathToName(PathTemplate);

                // Load and prepare original movie
                decimal scaleFactor = 1M / matchOptions.DownsampleFactor;
                MapHeader originalHeader;
                WarpImage originalStack;
                ImageHelper.LoadAndPrepareHeaderAndMap(item.DataPath, imageGain[gpuID], defectMap[gpuID], scaleFactor, out originalHeader, out originalStack);

                if (IsCanceled) { originalStack?.Dispose(); return true; }

                item.MatchFull(originalStack, matchOptions, template[gpuID], (size, value, name) =>
                {
                    Dispatcher.UIThread.InvokeAsync(() => TextProgress.Text = $"GPU {gpuID}: {name} ({value}/{size.Elements()})");
                    return IsCanceled;
                });

                originalStack?.Dispose();

                int progress = ++completed;
                Dispatcher.UIThread.InvokeAsync(() => ProgressOverall.Value = progress);

                return false;
            }, 1);
        });

        Close();
    }

    private void ButtonAbort_OnClick(object? sender, RoutedEventArgs e)
    {
        ButtonAbort.IsEnabled = false;
        ButtonAbort.Content = "CANCELING...";
        IsCanceled = true;
    }
}
