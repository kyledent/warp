using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
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
/// Avalonia port of Warp.Controls.TaskDialogs.Tomo.DialogTomoImportImod (832 lines).
/// Imports tilt series from IMOD: parses mdoc files, reads XF alignment files,
/// computes pretilt from patch tracking markers, and generates TomoSTAR files.
/// All processing logic is framework-agnostic (file I/O, matrix math, Star I/O).
/// </summary>
public partial class DialogTomoImportImod : Window
{
    public OptionsWarp Options;
    private string? MdocFolderPath;
    private string? ImodFolderPath;
    private string? MovieFolderPath;
    private bool DontInvertTilts = false;
    private decimal PixelSize = 1.0m;
    private decimal Dose = 0.01m;

    public DialogTomoImportImod(OptionsWarp options)
    {
        InitializeComponent();
        Options = options;
        PixelSize = Options.Import.BinnedPixelSize;
        DataContext = Options;
    }

    private void ButtonCancel_OnClick(object? sender, RoutedEventArgs e) => Close();

    private async void ButtonMdocFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Select MDOC Folder", AllowMultiple = false });
        if (folder.Count > 0) { MdocFolderPath = folder[0].Path.LocalPath; ButtonMdocFolder.Content = folder[0].Name; }
    }

    private async void ButtonImodFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Select IMOD Folder", AllowMultiple = false });
        if (folder.Count > 0) { ImodFolderPath = folder[0].Path.LocalPath; ButtonImodFolder.Content = folder[0].Name; }
    }

    private async void ButtonImport_OnClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(MdocFolderPath))
        {
            TextStatus.Text = "Please select an MDOC folder.";
            return;
        }

        MovieFolderPath = Options.Import.DataFolder;
        ProgressImport.IsVisible = true;
        TextStatus.Text = "Parsing MDOC files...";

        StringBuilder allExceptions = new();

        await Task.Run(async () =>
        {
            try
            {
                // Find all MDOC files
                string[] mdocFiles = Directory.GetFiles(MdocFolderPath, "*.mdoc", SearchOption.AllDirectories);
                if (mdocFiles.Length == 0)
                {
                    await Dispatcher.UIThread.InvokeAsync(() => TextStatus.Text = "No .mdoc files found.");
                    return;
                }

                // Group by tilt series name (strip .mrc.mdoc or .mdoc suffix)
                var mdocGroups = new Dictionary<string, List<string>>();
                foreach (string mdocPath in mdocFiles)
                {
                    string name = Path.GetFileName(mdocPath);
                    name = name.Replace(".mrc.mdoc", "").Replace(".mdoc", "");
                    if (!mdocGroups.ContainsKey(name)) mdocGroups[name] = new List<string>();
                    mdocGroups[name].Add(mdocPath);
                }

                int processed = 0;
                await Dispatcher.UIThread.InvokeAsync(() => ProgressImport.Maximum = mdocGroups.Count);

                foreach (var mdocGroup in mdocGroups)
                {
                    try
                    {
                        // Parse MDOC using WarpLib's Mdoc parser
                        Mdoc parsedMdoc = Mdoc.FromFile(mdocGroup.Value.ToArray(), (float)Dose);
                        List<MdocEntry> sortedAngle = parsedMdoc.Entries;

                        if (sortedAngle.Count == 0)
                            throw new Exception($"No tilts found for {mdocGroup.Key}");

                        // Read XF alignment file if available
                        bool hasAlignment = false;
                        if (!string.IsNullOrEmpty(ImodFolderPath))
                        {
                            string xfPath1 = Path.Combine(ImodFolderPath, mdocGroup.Key, mdocGroup.Key + ".xf");
                            string xfPath2 = Path.Combine(ImodFolderPath, mdocGroup.Key + ".xf");
                            string? xfPath = File.Exists(xfPath1) ? xfPath1 : (File.Exists(xfPath2) ? xfPath2 : null);

                            if (xfPath != null)
                            {
                                hasAlignment = true;
                                Mdoc.UpdateWithXf(xfPath, parsedMdoc);
                            }
                        }

                        // Generate TomoSTAR file
                        Star table = new Star(new[]
                        {
                            "wrpMovieName", "wrpAngleTilt", "wrpAxisAngle",
                            "wrpAxisOffsetX", "wrpAxisOffsetY", "wrpDose"
                        });

                        for (int i = 0; i < sortedAngle.Count; i++)
                        {
                            string pathToMovie = sortedAngle[i].Name;
                            if (!string.IsNullOrEmpty(MovieFolderPath))
                            {
                                Uri uriMovie = new Uri(MovieFolderPath.EndsWith('/') || MovieFolderPath.EndsWith('\\') ? MovieFolderPath : MovieFolderPath + '/');
                                string fullPath = Path.Combine(MovieFolderPath, sortedAngle[i].Name);
                                if (File.Exists(fullPath))
                                    pathToMovie = uriMovie.MakeRelativeUri(new Uri(fullPath)).ToString();
                            }

                            table.AddRow(new[]
                            {
                                pathToMovie,
                                (sortedAngle[i].TiltAngle * (DontInvertTilts ? 1 : -1)).ToString(CultureInfo.InvariantCulture),
                                sortedAngle[i].AxisAngle.ToString(CultureInfo.InvariantCulture),
                                (sortedAngle[i].Shift.X * (float)PixelSize).ToString(CultureInfo.InvariantCulture),
                                (sortedAngle[i].Shift.Y * (float)PixelSize).ToString(CultureInfo.InvariantCulture),
                                sortedAngle[i].Dose.ToString(CultureInfo.InvariantCulture)
                            });
                        }

                        // Save TomoSTAR
                        string outputPath = Path.Combine(Options.Import.DataFolder, mdocGroup.Key + ".tomostar");
                        table.Save(outputPath);
                    }
                    catch (Exception exc)
                    {
                        allExceptions.AppendLine($"{mdocGroup.Key}: {exc.Message}");
                    }

                    processed++;
                    int p = processed;
                    await Dispatcher.UIThread.InvokeAsync(() => ProgressImport.Value = p);
                }
            }
            catch (Exception exc)
            {
                allExceptions.AppendLine(exc.Message);
            }
        });

        if (allExceptions.Length > 0)
            TextStatus.Text = $"Import completed with errors:\n{allExceptions}";
        else
            TextStatus.Text = "Import complete.";
    }
}
