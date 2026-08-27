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
using Warp.Tools;

namespace WarpAvalonia.Dialogs;

/// <summary>
/// Avalonia port of Warp.Controls.DialogTomoReconstruction.
/// Reconstructs full tomograms with optional deconvolution, half-maps for denoising,
/// and voxel coverage filtering. Multi-GPU support via Helper.ForEachGPU.
/// </summary>
public partial class DialogTomoReconstruction : Window
{
    private TiltSeries[] Series;
    private OptionsWarp Options;
    private bool IsCanceled = false;
    private List<int> DeviceList;

    public DialogTomoReconstruction(TiltSeries[] series, OptionsWarp options, List<int> deviceList)
    {
        InitializeComponent();
        Series = series;
        Options = options;
        DeviceList = deviceList;
        DataContext = Options;

        // Check if frame-based denoising is possible
        bool canUseFrames = false;
        if (series.Length > 0)
        {
            Movie firstMovie = new Movie(Path.Combine(series[0].DataOrProcessingDirectoryName, series[0].TiltMoviePaths[0]));
            canUseFrames = File.Exists(firstMovie.AverageEvenPath) && File.Exists(firstMovie.AverageOddPath);
        }
        if (!canUseFrames)
        {
            Options.Tasks.TomoFullReconstructDenoisingFrames = false;
            Options.Tasks.TomoFullReconstructDenoisingTilts = true;
            RadioDenoisingFrames.IsEnabled = false;
        }
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

        int completed = 0;
        ProgressOverall.Maximum = validSeries.Count;

        await Task.Run(() =>
        {
            Helper.ForEachGPU(validSeries, (item, gpuID) =>
            {
                if (IsCanceled) return true;

                ProcessingOptionsTomoFullReconstruction seriesOptions = Options.GetProcessingTomoFullReconstruction();

                item.ReconstructFull(seriesOptions, (size, value, name) =>
                {
                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        TextProgress.Text = $"GPU {gpuID}: {name} ({value}/{size.X * size.Y * size.Z})";
                    });
                    return IsCanceled;
                });

                int progress = ++completed;
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ProgressOverall.Value = progress;
                    TextProgress.Text = $"Completed {progress}/{validSeries.Count}";
                });

                return false;
            }, 1, DeviceList);
        });

        Close();
    }

    private void ButtonAbort_OnClick(object? sender, RoutedEventArgs e)
    {
        ButtonAbort.IsEnabled = false;
        IsCanceled = true;
    }
}
