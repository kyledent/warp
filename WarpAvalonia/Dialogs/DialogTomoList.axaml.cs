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
/// Avalonia port of Warp.Controls.TaskDialogs.Tomo.DialogTomoList.
/// Exports tilt series metadata to RELION STAR format.
/// </summary>
public partial class DialogTomoList : Window
{
    public TiltSeries[] Series;
    public string? ExportPath;
    public OptionsWarp Options;

    public DialogTomoList(TiltSeries[] series, OptionsWarp options)
    {
        InitializeComponent();
        Series = series;
        Options = options;
        DataContext = Options;
    }

    private void ButtonCancel_OnClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void ButtonWrite_OnClick(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Tomogram List",
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
            List<TiltSeries> validSeries = Series.Where(v =>
            {
                if (!filter && v.UnselectFilter && v.UnselectManual == null)
                    return false;
                if (!manual && v.UnselectManual != null && (bool)v.UnselectManual)
                    return false;
                return true;
            }).ToList();

            string pathPrefix = "";
            if (validSeries.Count > 0 && relative)
            {
                Uri uriStar = new Uri(ExportPath);
                pathPrefix = uriStar.MakeRelativeUri(new Uri(validSeries[0].Path)).ToString();
                pathPrefix = pathPrefix.Substring(0, pathPrefix.IndexOf(Helper.PathToNameWithExtension(pathPrefix)));
            }

            await Dispatcher.UIThread.InvokeAsync(() => ProgressWrite.Maximum = validSeries.Count);

            bool includeCTF = validSeries.Any(v => v.OptionsCTF != null);

            Star tableOut = new Star(new[] { "rlnMicrographName" });
            if (includeCTF)
                tableOut.AddColumn("rlnCtfMaxResolution");

            int r = 0;
            foreach (var series in validSeries)
            {
                List<string> row = new List<string> { pathPrefix + series.Name };
                if (includeCTF)
                    row.Add(series.OptionsCTF != null
                        ? series.CTFResolutionEstimate.ToString("F1", CultureInfo.InvariantCulture)
                        : "999");

                tableOut.AddRow(row.ToArray());
                int progress = ++r;
                await Dispatcher.UIThread.InvokeAsync(() => ProgressWrite.Value = progress);
            }

            tableOut.Save(ExportPath);
        });

        Close();
    }
}
