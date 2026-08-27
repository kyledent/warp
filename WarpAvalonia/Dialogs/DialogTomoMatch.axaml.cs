using System;
using System.Collections.Generic;
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
/// Avalonia port of Warp.Controls.TaskDialogs.Tomo.DialogTomoMatch.
/// Template matching on reconstructed 3D tomograms with GPU acceleration.
/// </summary>
public partial class DialogTomoMatch : Window
{
    private TiltSeries[] Series;
    private string PathTemplate;
    private OptionsWarp Options;
    private bool IsCanceled = false;

    public DialogTomoMatch(TiltSeries[] series, string pathTemplate, OptionsWarp options)
    {
        InitializeComponent();
        Series = series;
        PathTemplate = pathTemplate;
        Options = options;
        DataContext = Options;

        TextTemplateName.Text = Helper.PathToNameWithExtension(pathTemplate);
        MapHeader header = MapHeader.ReadFromFile(pathTemplate);
        if (Math.Abs(header.PixelSize.X - 1f) > 1e-6f)
            Options.Tasks.TomoMatchTemplatePixel = (decimal)header.PixelSize.X;
    }

    private void ButtonCancel_OnClick(object? sender, RoutedEventArgs e) => Close();

    private async void ButtonReconstruct_OnClick(object? sender, RoutedEventArgs e)
    {
        bool filter = CheckFilter.IsChecked == true;
        bool manual = CheckManual.IsChecked == true;

        List<TiltSeries> validSeries = Series.Where(v =>
        {
            if (!filter && v.UnselectFilter && v.UnselectManual == null) return false;
            if (!manual && v.UnselectManual != null && (bool)v.UnselectManual) return false;
            if (v.OptionsCTF == null) return false;
            return true;
        }).ToList();

        if (validSeries.Count == 0) { Close(); return; }

        PanelConfiguration.IsVisible = false;
        PanelProgress.IsVisible = true;

        int nParallel = Math.Min(validSeries.Count, GPU.GetDeviceCount());
        int completed = 0;
        ProgressOverall.Maximum = validSeries.Count;

        await Task.Run(() =>
        {
            WarpImage[] template = new WarpImage[nParallel];
            template[0] = WarpImage.FromFile(PathTemplate);
            template[0].PixelSize = (float)Options.Tasks.TomoMatchTemplatePixel;
            for (int i = 1; i < template.Length; i++)
                template[i] = template[0].GetCopy();

            Helper.ForEachGPU(validSeries, (item, gpuID) =>
            {
                if (IsCanceled) return true;

                ProcessingOptionsTomoFullMatch seriesOptions = Options.GetProcessingTomoFullMatch();
                seriesOptions.TemplateName = Helper.PathToName(PathTemplate);
                seriesOptions.NormalizeScores = true;

                item.MatchFull(seriesOptions, template[gpuID], (size, value, name) =>
                {
                    Dispatcher.UIThread.InvokeAsync(() => TextProgress.Text = $"GPU {gpuID}: {name} ({(int)(value + 0.5f)}/{size.Elements()})");
                    return IsCanceled;
                });

                int progress = ++completed;
                Dispatcher.UIThread.InvokeAsync(() => ProgressOverall.Value = progress);

                return false;
            }, 1, Helper.ArrayOfSequence(0, nParallel, 1).ToList());
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
