using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Warp;
using Warp.Headers;
using Warp.Sociology;
using Warp.Tools;

namespace MAvalonia.Dialogs;

/// <summary>
/// Port of WPF M's DialogDataSources (M/Controls/Sociology/Dialogs/DialogDataSources.xaml.cs):
/// lists the population's sources, removes them, and adds local ones either from an existing
/// .source file or by creating a .source from a Warp .settings file.
/// </summary>
public partial class DialogDataSources : Window
{
    private readonly Population Population;

    public DialogDataSources(Population population)
    {
        InitializeComponent();
        Population = population;
        UpdateGridItems();
    }

    private void ButtonClose_OnClick(object? s, RoutedEventArgs e) => Close();

    private void UpdateGridItems()
    {
        GridSources.Children.Clear();
        GridSources.RowDefinitions.Clear();

        if (Population.Sources.Count == 0)
            return;

        bool anyTomo = Population.Sources.Any(s => s.IsTiltSeries);
        bool anyLocal = Population.Sources.Any(s => !s.IsRemote);

        void Add(Control c, int row, int column)
        {
            Grid.SetRow(c, row);
            Grid.SetColumn(c, column);
            GridSources.Children.Add(c);
        }

        TextBlock Cell(string text, bool header = false, HorizontalAlignment align = HorizontalAlignment.Left) => new TextBlock
        {
            Text = text,
            FontWeight = header ? FontWeight.Medium : FontWeight.Normal,
            Margin = new Thickness(0, 0, 16, 0),
            HorizontalAlignment = align,
            VerticalAlignment = VerticalAlignment.Center
        };

        GridSources.RowDefinitions.Add(new RowDefinition(30, GridUnitType.Pixel));
        Add(Cell("Name", true), 0, 1);
        Add(Cell("Path", true), 0, 2);
        Add(Cell("Files", true), 0, 3);
        Add(Cell("Pixel size", true), 0, 4);
        if (anyTomo)
            Add(Cell("Volume dimensions", true), 0, 5);
        if (anyLocal)
        {
            var freeze = Cell("Freeze", true);
            ToolTip.SetTip(freeze, "Don't change frame/tilt series alignment.");
            Add(freeze, 0, 6);
        }

        for (int s = 0; s < Population.Sources.Count; s++)
        {
            int row = s + 1;
            DataSource source = Population.Sources[s];
            GridSources.RowDefinitions.Add(new RowDefinition(30, GridUnitType.Pixel));

            Add(new TextBlock { Text = source.IsRemote ? "☁" : "📀", Width = 32, VerticalAlignment = VerticalAlignment.Center }, row, 0);
            Add(Cell(source.Name), row, 1);
            var path = Cell(Helper.ShortenString(source.Path, 50));
            ToolTip.SetTip(path, source.Path);
            Add(path, row, 2);
            Add(Cell(source.Files.Count.ToString(), align: HorizontalAlignment.Right), row, 3);
            Add(Cell($"{source.PixelSize} Å"), row, 4);
            if (source.IsTiltSeries)
                Add(Cell($"{source.DimensionsX} x {source.DimensionsY} x {source.DimensionsZ} px"), row, 5);

            // WPF M shows this checkbox without binding it to anything; it is kept, disabled, so the
            // table matches, rather than offering a switch that does nothing.
            var freezeBox = new CheckBox
            {
                IsChecked = source.IsRemote,
                IsEnabled = false,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTip.SetTip(freezeBox, "Don't change frame/tilt series alignment (not implemented in M).");
            Add(freezeBox, row, 6);

            var remove = new Button
            {
                Content = "REMOVE",
                FontWeight = FontWeight.Medium,
                Foreground = Brushes.Red,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };
            remove.Click += async (sender, args) =>
            {
                if (await MessageWindow.AskAsync(this, "Would you like to remove this data source?",
                                                 "This will not physically delete any files.")
                    && Population.Sources.Contains(source))
                {
                    Population.Sources.Remove(source);
                    UpdateGridItems();
                }
            };
            Add(remove, row, 7);
        }
    }

    private async void ButtonAddLocal_OnClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add a data source",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Warp Folder Settings") { Patterns = new[] { "*.settings" } },
                new FilePickerFileType("Warp Data Source") { Patterns = new[] { "*.source" } }
            }
        });
        if (files.Count == 0)
            return;

        string fileName = files[0].Path.LocalPath;
        var info = new FileInfo(fileName);

        if (info.Extension.ToLower() == ".settings")
        {
            await AddFromSettings(info);
            return;
        }

        try
        {
            if (Population.Sources.Any(s => s.Path == fileName))
            {
                await MessageWindow.ShowAsync(this, "Oopsie", "This data source is already part of this population.");
                return;
            }
            Population.Sources.Add(DataSource.FromFile(fileName));
            UpdateGridItems();
        }
        catch
        {
            await MessageWindow.ShowAsync(this, "Oopsie", $"An error was encountered when reading {info.Name}.");
        }
    }

    private async Task AddFromSettings(FileInfo info)
    {
        var options = new OptionsWarp();
        try
        {
            options.Load(info.FullName);
        }
        catch
        {
            await MessageWindow.ShowAsync(this, "Oopsie", $"An error was encountered when reading {info.Name}.");
            return;
        }

        #region Load items with metadata

        string fileExtension = options.Import.Extension;
        string[] availableFiles = Directory.EnumerateFiles(info.DirectoryName!, fileExtension).ToArray();
        var loaded = new Movie?[availableFiles.Length];

        string[] matchingStarNames = Array.Empty<string>();
        if (fileExtension != "*.tomostar" && availableFiles.Length > 0)
        {
            var first = new Movie(availableFiles[0]);
            if (Directory.Exists(first.MatchingDir))
                matchingStarNames = Directory.EnumerateFiles(first.MatchingDir, "*.star").ToArray();
        }

        var progress = new ProgressWindow("Loading metadata...", "");
        _ = progress.ShowDialog(this);
        await Task.Run(() =>
        {
            int done = 0;
            Parallel.For(0, availableFiles.Length, i =>
            {
                string file = availableFiles[i];
                string xmlPath = file.Substring(0, file.LastIndexOf(".")) + ".xml";
                if (File.Exists(xmlPath))
                    loaded[i] = fileExtension == "*.tomostar" ? new TiltSeries(file) : new Movie(file, null, matchingStarNames);
                int d = Interlocked.Increment(ref done);
                Dispatcher.UIThread.Post(() => progress.SetProgress(d, availableFiles.Length));
            });
        });
        progress.Finish();

        // WPF M keeps the slots of files without metadata as nulls and then dereferences them;
        // only items that have metadata are considered here.
        Movie[] items = loaded.Where(m => m != null).Select(m => m!).ToArray();
        if (items.Length == 0)
        {
            await MessageWindow.ShowAsync(this, "Oopsie", "No micrographs or tilt series found to match these settings.");
            return;
        }

        #endregion

        #region Figure out filtering status, as WPF M

        Movie[] itemsWithCTF = items.Where(v => v.OptionsCTF != null && v.CTF != null).ToArray();
        float2 Astig(Movie m) => new float2((float)Math.Cos((float)m.CTF.DefocusAngle * 2 * Helper.ToRad) * (float)m.CTF.DefocusDelta,
                                            (float)Math.Sin((float)m.CTF.DefocusAngle * 2 * Helper.ToRad) * (float)m.CTF.DefocusDelta);
        float2 astigmatismMean = new float2();
        float astigmatismStd = 0.1f;
        if (itemsWithCTF.Length > 0)
        {
            foreach (var item in itemsWithCTF)
                astigmatismMean += Astig(item);
            astigmatismMean /= itemsWithCTF.Length;

            astigmatismStd = 0;
            foreach (var item in itemsWithCTF)
                astigmatismStd += (Astig(item) - astigmatismMean).LengthSq();
            astigmatismStd = (float)Math.Max(1e-4, Math.Sqrt(astigmatismStd / itemsWithCTF.Length));
        }

        foreach (var item in items)
        {
            bool filterStatus = true;
            if (item.OptionsCTF != null)
            {
                filterStatus &= item.CTF.Defocus >= options.Filter.DefocusMin && item.CTF.Defocus <= options.Filter.DefocusMax;
                float astigmatismDeviation = (Astig(item) - astigmatismMean).Length() / astigmatismStd;
                filterStatus &= astigmatismDeviation <= (float)options.Filter.AstigmatismMax;
                filterStatus &= item.CTFResolutionEstimate <= options.Filter.ResolutionMax;
                filterStatus &= item.CTF.PhaseShift >= options.Filter.PhaseMin && item.CTF.PhaseShift <= options.Filter.PhaseMax;
                filterStatus &= item.GetParticleCount(options.Filter.ParticlesSuffix) >= options.Filter.ParticlesMin;
            }
            if (item.OptionsMovement != null)
                filterStatus &= item.MeanFrameMovement <= options.Filter.MotionMax;

            item.UnselectFilter = !filterStatus;
        }

        var optionsCTF = options.GetProcessingMovieCTF();
        var optionsMovement = options.GetProcessingMovieMovement();
        var optionsBoxNet = options.GetProcessingBoxNet();
        var optionsExport = options.GetProcessingMovieExport();

        var itemsProcessed = new List<Movie>();
        var itemsFilteredOut = new List<Movie>();
        var itemsUnselected = new List<Movie>();
        foreach (var item in items)
        {
            var status = SourceStatus.GetMovieProcessingStatus(item, optionsCTF, optionsMovement, optionsBoxNet, optionsExport, options);
            if (status == ProcessingStatus.Processed || (status == ProcessingStatus.Outdated && !item.UnselectFilter))
                itemsProcessed.Add(item);
            else if (status == ProcessingStatus.FilteredOut || (status == ProcessingStatus.Outdated && item.UnselectFilter))
                itemsFilteredOut.Add(item);
            else if (status == ProcessingStatus.LeaveOut)
                itemsUnselected.Add(item);
        }

        int usableFrames = items[0].GetType() == typeof(Movie)
            ? MapHeader.ReadFromFile(items[0].DataPath).Dimensions.Z
            : ((TiltSeries)items[0]).NTilts;

        #endregion

        var dialog = new DialogCreateSourceFromSettings();
        dialog.TextTitle.Text = $"Create data source from\n{info.Name}";
        dialog.SetCounts(itemsProcessed.Count, itemsFilteredOut.Count, itemsUnselected.Count);
        dialog.SetUsableFrames(usableFrames);
        if (await dialog.ShowDialog<bool>(this) != true)
            return;

        var newSource = new DataSource
        {
            PixelSize = options.Import.PixelSize,
            DimensionsX = options.Tomo.DimensionsX,
            DimensionsY = options.Tomo.DimensionsY,
            DimensionsZ = options.Tomo.DimensionsZ,
            FrameLimit = dialog.FrameLimit,
            GainPath = options.Import.CorrectGain ? options.Import.GainPath : "",
            GainFlipX = options.Import.GainFlipX,
            GainFlipY = options.Import.GainFlipY,
            GainTranspose = options.Import.GainTranspose,
            DefectsPath = options.Import.CorrectDefects ? options.Import.DefectsPath : "",
            DosePerAngstromFrame = options.Import.DosePerAngstromFrame,
            EERGroupFrames = options.Import.EERGroupFrames,
            Name = dialog.SourceName,
            // WPF M joins with "\\"; Path.Combine gives the right separator on every platform.
            Path = Path.Combine(info.DirectoryName!, Helper.RemoveInvalidChars(dialog.SourceName) + ".source")
        };

        if (Population.Sources.Any(s => s.Path == newSource.Path))
        {
            await MessageWindow.ShowAsync(this, "Oopsie",
                $"{Helper.PathToNameWithExtension(newSource.Path)} already exists in this population. Please choose a different name.");
            return;
        }

        var allItems = new List<Movie>(itemsProcessed);
        if (dialog.IncludeFilteredOut)
            allItems.AddRange(itemsFilteredOut);
        if (dialog.IncludeDeselected)
            allItems.AddRange(itemsUnselected);

        if (allItems.Count == 0)
        {
            await MessageWindow.ShowAsync(this, "Oopsie", "No micrographs or tilt series found to match these settings.");
            return;
        }

        progress = new ProgressWindow("Calculating data hashes...", "");
        _ = progress.ShowDialog(this);
        await Task.Run(() =>
        {
            int done = 0;
            foreach (var item in allItems)
            {
                newSource.Files.Add(item.GetDataHash(), item.Name);
                int d = ++done;
                Dispatcher.UIThread.Post(() => progress.SetProgress(d, allItems.Count));
            }
        });
        progress.Finish();

        string[] overlapping = Helper.Combine(Population.Sources.Select(s => s.Files.Where(f => newSource.Files.ContainsKey(f.Key))
                                                                                   .Select(f => f.Value).ToArray()).ToArray());
        if (overlapping.Length > 0)
        {
            string offenders = "";
            for (int o = 0; o < Math.Min(5, overlapping.Length); o++)
                offenders += "\n" + overlapping[o];
            if (overlapping.Length > 5)
                offenders += $"\n... and {overlapping.Length - 5} more.";

            await MessageWindow.ShowAsync(this, "Oopsie", "The new source contains files that are already used in this population:" + offenders);
            return;
        }

        progress = new ProgressWindow("Committing initial version...", "");
        _ = progress.ShowDialog(this);
        await Task.Run(() => newSource.Commit());
        progress.Finish();

        Population.Sources.Add(newSource);
        UpdateGridItems();
    }
}

public enum ProcessingStatus
{
    Processed = 1,
    Outdated = 2,
    Unprocessed = 3,
    FilteredOut = 4,
    LeaveOut = 5
}

public static class SourceStatus
{
    /// <summary>Verbatim port of WPF M's DialogDataSources.GetMovieProcessingStatus.</summary>
    public static ProcessingStatus GetMovieProcessingStatus(Movie movie, ProcessingOptionsMovieCTF optionsCTF, ProcessingOptionsMovieMovement optionsMovement,
                                                            ProcessingOptionsBoxNet optionsBoxNet, ProcessingOptionsMovieExport optionsExport,
                                                            OptionsWarp options, bool considerFilter = true)
    {
        bool doCTF = options.ProcessCTF;
        bool doMovement = options.ProcessMovement;
        bool doBoxNet = options.ProcessPicking;
        bool doExport = optionsExport.DoAverage || optionsExport.DoStack || optionsExport.DoDeconv;

        ProcessingStatus status = ProcessingStatus.Processed;

        if (movie.UnselectManual != null && (bool)movie.UnselectManual)
        {
            status = ProcessingStatus.LeaveOut;
        }
        else if (movie.OptionsCTF == null && movie.OptionsMovement == null && movie.OptionsMovieExport == null)
        {
            status = ProcessingStatus.Unprocessed;
        }
        else
        {
            if (doCTF && (movie.OptionsCTF == null || movie.OptionsCTF != optionsCTF))
                status = ProcessingStatus.Outdated;
            else if (doMovement && (movie.OptionsMovement == null || movie.OptionsMovement != optionsMovement))
                status = ProcessingStatus.Outdated;
            else if (doBoxNet && (movie.OptionsBoxNet == null || movie.OptionsBoxNet != optionsBoxNet))
                status = ProcessingStatus.Outdated;
            else if (doExport && (movie.OptionsMovieExport == null || movie.OptionsMovieExport != optionsExport))
                status = ProcessingStatus.Outdated;
        }

        if (status == ProcessingStatus.Processed && movie.UnselectFilter && movie.UnselectManual == null && considerFilter)
            status = ProcessingStatus.FilteredOut;

        return status;
    }
}
