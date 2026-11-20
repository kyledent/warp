using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Warp;
using Warp.Sociology;
using Warp.Tools;

namespace MAvalonia;

/// <summary>
/// Avalonia port of M's MainWindow (~1,004 lines).
/// Population management, species display, data source management, and refinement.
/// </summary>
public partial class MainWindow : Window
{
    private Population? ActivePopulation;
    private OptionsWarp Options = new();

    public MainWindow()
    {
        InitializeComponent();
        InitGPUStats();
    }

    #region Population Management

    private async void ButtonPopulationCreateNew_OnClick(object? sender, RoutedEventArgs e)
    {
        var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select folder for new population",
            AllowMultiple = false
        });
        if (folder.Count == 0) return;

        string populationPath = Path.Combine(folder[0].Path.LocalPath, "population.xml");

        try
        {
            ActivePopulation = new Population(populationPath);
            ActivePopulation.Save();
            ActivatePopulation();
        }
        catch (Exception ex)
        {
            Title = $"Error creating population: {ex.Message}";
        }
    }

    private async void ButtonPopulationLoad_OnClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load Population",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Population") { Patterns = new[] { "*.xml" } } }
        });
        if (files.Count == 0) return;

        try
        {
            ActivePopulation = new Population(files[0].Path.LocalPath);
            ActivatePopulation();
        }
        catch (Exception ex)
        {
            Title = $"Error loading population: {ex.Message}";
        }
    }

    private void ActivatePopulation()
    {
        if (ActivePopulation == null) return;

        PanelLanding.IsVisible = false;
        PanelMain.IsVisible = true;

        TextPopulationName.Text = ActivePopulation.Name;
        UpdateSourceStats();
        UpdateSpeciesDisplay();
    }

    #endregion

    #region Data Sources

    private async void ButtonPopulationEditSources_OnClick(object? sender, RoutedEventArgs e)
    {
        if (ActivePopulation == null) return;

        // In production: open DialogDataSources with ActivePopulation.Sources
        // For now: show source count
        TextSourceStats.Text = $"{ActivePopulation.Sources.Count} data source(s)";
    }

    private void UpdateSourceStats()
    {
        if (ActivePopulation == null) return;

        int nSources = ActivePopulation.Sources.Count;
        int nSeries = 0, nMicrographs = 0;

        // Source stats would be computed from actual Source.Files in production
        // For now: show source count only

        TextSourceStats.Text = $"{nSources} data source(s)";
    }

    #endregion

    #region Species Display

    private void UpdateSpeciesDisplay()
    {
        PanelSpecies.Children.Clear();
        if (ActivePopulation == null) return;

        foreach (var species in ActivePopulation.Species)
        {
            var card = CreateSpeciesCard(species);
            PanelSpecies.Children.Add(card);
        }
    }

    private Border CreateSpeciesCard(Species species)
    {
        var card = new Border
        {
            Width = 280,
            Margin = new Thickness(8),
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(6),
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(15, 255, 255, 255))
        };

        var stack = new StackPanel { Spacing = 4 };

        stack.Children.Add(new TextBlock
        {
            Text = species.Name,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold
        });

        stack.Children.Add(new TextBlock
        {
            Text = $"Symmetry: {species.Symmetry}",
            Foreground = Brushes.Gray
        });

        stack.Children.Add(new TextBlock
        {
            Text = $"Resolution: {species.GlobalResolution:F1} A",
            Foreground = Brushes.Gray
        });

        stack.Children.Add(new TextBlock
        {
            Text = $"Resolution: {species.GlobalResolution:F1} A",
            Foreground = Brushes.Gray
        });

        // Action buttons
        var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(new Button { Content = "Shift", Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Brushes.CornflowerBlue, FontSize = 11 });
        buttons.Children.Add(new Button { Content = "Rotate", Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Brushes.CornflowerBlue, FontSize = 11 });
        buttons.Children.Add(new Button { Content = "Sym Expand", Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Brushes.CornflowerBlue, FontSize = 11 });
        stack.Children.Add(buttons);

        card.Child = stack;
        return card;
    }

    private async void ButtonPopulationAddSpecies_OnClick(object? sender, RoutedEventArgs e)
    {
        if (ActivePopulation == null) return;

        // In production: open DialogAddSpecies / DialogCreateNewSpecies
        // For now: placeholder
        Title = "M — Select a map + STAR file to create a new species";
    }

    #endregion

    #region Refinement

    // Port of WPF M's ButtonPopulationRefine_Click and PerformRefinementIteration
    // (M/MainWindow.xaml.cs). WPF M hard-codes DoMultiProcess = true and ProcessesPerDevice = 1,
    // so only that path is ported: one WarpWorker process per selected GPU.

    private async void ButtonRefine_OnClick(object? sender, RoutedEventArgs e)
    {
        if (ActivePopulation == null)
            return;

        if (ActivePopulation.Species.Count == 0)
        {
            Title = "M — There are no species in this population. Please create one first.";
            return;
        }

        var options = ActivePopulation.LastRefinementOptions;
        var settings = new Dialogs.DialogRefinementSettings { DataContext = options };
        bool? start = await settings.ShowDialog<bool?>(this);
        settings.DataContext = null;
        if (start != true)
            return;

        options.DoAstigmatismDelta = options.DoDefocus;
        options.DoAstigmatismAngle = options.DoDefocus;

        PanelSpecies.Children.Clear();   // to avoid UI updates from the refinement thread

        ButtonRefine.IsEnabled = false;
        try
        {
            await PerformRefinementIteration(options);
        }
        finally
        {
            ButtonRefine.IsEnabled = true;
            UpdateSpeciesDisplay();
        }
    }

    private async Task PerformRefinementIteration(ProcessingOptionsMPARefine options)
    {
        const int ProcessesPerDevice = 1;
        var population = ActivePopulation!;

        List<int> usedDevices = GetDeviceList();
        if (usedDevices.Count == 0)
        {
            Title = "M — No GPU selected: tick at least one GPU in the list at the top.";
            return;
        }

        int nDevices = GPU.GetDeviceCount();
        var workers = new WorkerWrapper[nDevices * ProcessesPerDevice];
        var workerFolders = new string[workers.Length];
        var workerLogs = new string[workers.Length];
        var allWorkerFolders = new List<string>();

        string stagingDirectory = Path.Combine(population.FolderPath, "refinement_temp", "staging");
        Directory.CreateDirectory(stagingDirectory);

        var progress = new Dialogs.ProgressWindow("Preparing for refinement – this will take a few minutes per species", "");
        _ = progress.ShowDialog(this);

        int itemsCompleted = 0;
        int itemsToDo = population.Sources.Select(s => s.Files.Count).Sum();
        var currentlyRefinedItems = new string?[nDevices * ProcessesPerDevice];

        // Every second, show the last line of each busy worker's log, as WPF M does.
        var statusUpdater = new System.Timers.Timer(1001);
        statusUpdater.Elapsed += (s, e) =>
        {
            var message = new System.Text.StringBuilder();
            lock (currentlyRefinedItems)
            {
                foreach (int gpuID in usedDevices)
                    for (int i = 0; i < ProcessesPerDevice; i++)
                    {
                        int w = gpuID * ProcessesPerDevice + i;
                        if (currentlyRefinedItems[w] == null)
                            continue;
                        try { message.Append(currentlyRefinedItems[w] + ": " + File.ReadLines(workerLogs[w]).Last() + "\n"); }
                        catch { }
                    }
            }
            string text = message.ToString();
            Dispatcher.UIThread.Post(() => progress.SetMessage(text));
        };

        try
        {
            await Task.Run(() =>
            {
                void Message(string m) => Dispatcher.UIThread.Post(() => progress.SetMessage(m));
                void Heading(string t) => Dispatcher.UIThread.Post(() => progress.SetTitle(t));

                Message("Preparing refinement requisites...");
                var speciesPreparers = usedDevices.Select(i => new WorkerWrapper(i)).ToArray();
                Helper.ForEachGPU(population.Species, (species, gpuID) =>
                {
                    speciesPreparers[usedDevices.IndexOf(gpuID)].MPAPrepareSpecies(species.Path, stagingDirectory);
                }, 1, usedDevices);
                foreach (var item in speciesPreparers)
                    item.Dispose();

                GPU.CheckGPUExceptions();

                Heading("Performing refinement");

                foreach (var source in population.Sources)
                {
                    foreach (int gpuID in usedDevices)
                        for (int i = 0; i < ProcessesPerDevice; i++)
                        {
                            int w = gpuID * ProcessesPerDevice + i;
                            workerFolders[w] = Path.Combine(population.FolderPath, "refinement_temp", source.Name, $"worker{w}");
                            Directory.CreateDirectory(workerFolders[w]);

                            workers[w] = new WorkerWrapper(gpuID);
                            workers[w].SetHeaderlessParams(new int2(2), 0, "float");
                            workerLogs[w] = Path.Combine(workerFolders[w], "run.out");
                        }

                    Helper.ForEachGPUOnce(gpuID =>
                    {
                        for (int i = 0; i < ProcessesPerDevice; i++)
                            workers[gpuID * ProcessesPerDevice + i].MPAPreparePopulation(population.Path, stagingDirectory);
                    }, usedDevices);

                    Message($"Loading gain reference for {source.Name}...");
                    try
                    {
                        Helper.ForEachGPUOnce(gpuID =>
                        {
                            for (int i = 0; i < ProcessesPerDevice; i++)
                                workers[gpuID * ProcessesPerDevice + i].LoadGainRef(source.GainPath,
                                                                                    source.GainFlipX,
                                                                                    source.GainFlipY,
                                                                                    source.GainTranspose,
                                                                                    source.DefectsPath);
                        }, usedDevices);
                    }
                    catch
                    {
                        throw new Exception($"Could not load gain reference for {source.Name}.");
                    }

                    Message("Loading item metadata...");
                    string[] fileNames = source.Files.Values.ToArray();
                    var items = new Movie[fileNames.Length];
                    Parallel.For(0, fileNames.Length, i =>
                    {
                        string path = Path.Combine(source.FolderPath, fileNames[i]);
                        items[i] = source.IsTiltSeries ? new TiltSeries(path) : new Movie(path);
                    });

                    statusUpdater.Start();

                    // WPF M's scheduler without its memory budget, which it sets to 2^50 bytes
                    // with a footprint of 1 per item, i.e. never limiting: one item per worker
                    // at a time, last item first.
                    var remaining = new List<Movie>(items);
                    var tokens = new Queue<DeviceToken>();
                    for (int d = usedDevices.Count - 1; d >= 0; d--)
                        for (int i = 0; i < ProcessesPerDevice; i++)
                            tokens.Enqueue(new DeviceToken(usedDevices[d] * ProcessesPerDevice + i));
                    int nTokens = tokens.Count;
                    var errors = new System.Collections.Concurrent.ConcurrentQueue<Exception>();

                    while (remaining.Count > 0 && errors.IsEmpty)
                    {
                        DeviceToken token;
                        while (true)
                        {
                            lock (tokens)
                                if (tokens.Count > 0) { token = tokens.Dequeue(); break; }
                            Thread.Sleep(5);
                        }

                        Movie item = remaining[remaining.Count - 1];
                        remaining.RemoveAt(remaining.Count - 1);

                        var thread = new Thread(() =>
                        {
                            int gpuID = token.ID / ProcessesPerDevice;
                            int w = token.ID;
                            try
                            {
                                GPU.SetDevice(gpuID);
                                lock (currentlyRefinedItems)
                                    currentlyRefinedItems[w] = item.Name;

                                workers[w].MPARefine(item.Path, workerFolders[w], options, source);

                                lock (currentlyRefinedItems)
                                    currentlyRefinedItems[w] = null;

                                int done = Interlocked.Increment(ref itemsCompleted);
                                Dispatcher.UIThread.Post(() => progress.SetProgress(done, itemsToDo));
                            }
                            catch (Exception ex)
                            {
                                errors.Enqueue(new Exception($"Refining {item.Name}: {ex.Message}", ex));
                            }
                            finally
                            {
                                lock (tokens)
                                    tokens.Enqueue(token);
                            }
                        }) { Name = $"ForEachGPU Device {token.ID}" };
                        thread.Start();
                    }

                    while (true)
                    {
                        lock (tokens)
                            if (tokens.Count == nTokens) break;
                        Thread.Sleep(5);
                    }

                    statusUpdater.Stop();

                    if (errors.TryPeek(out var first))
                        throw first;

                    source.Commit();

                    Helper.ForEachGPUOnce(gpuID =>
                    {
                        for (int i = 0; i < ProcessesPerDevice; i++)
                        {
                            workers[gpuID * ProcessesPerDevice + i].MPASaveProgress(workerFolders[gpuID * ProcessesPerDevice + i]);
                            workers[gpuID * ProcessesPerDevice + i].Dispose();
                        }
                    }, usedDevices);

                    allWorkerFolders.AddRange(usedDevices.SelectMany(g => Enumerable.Range(0, ProcessesPerDevice)
                                                                                    .Select(i => workerFolders[g * ProcessesPerDevice + i])));
                }

                Heading("Finishing refinement");
                Message("Gathering intermediate results, then reconstructing and filtering...");

                Helper.ForEachGPU(population.Species, (species, gpuID) =>
                {
                    var worker = new WorkerWrapper(gpuID);
                    worker.MPAFinishSpecies(species.Path, stagingDirectory, allWorkerFolders.ToArray());
                    worker.Dispose();
                }, 1, usedDevices);

                Dispatcher.UIThread.Invoke(() =>
                {
                    for (int s = 0; s < population.Species.Count; s++)
                        population.Species[s] = Species.FromFile(population.Species[s].Path);
                });

                foreach (var folder in allWorkerFolders)
                    try { Directory.Delete(folder, true); }
                    catch { }

                population.Save();
            });

            progress.Finish();
        }
        catch (Exception exc)
        {
            statusUpdater.Stop();
            foreach (var worker in workers)
                try { worker?.Dispose(); } catch { }
            progress.ShowError("Something went wrong during refinement",
                               "Sorry! Here are the details:\n\n" + exc);
        }
    }

    #endregion

    #region GPU list (WPF M: title bar)

    private CheckBox[] CheckboxesGPUStats = Array.Empty<CheckBox>();
    private DispatcherTimer? TimerGPUStats;

    private void InitGPUStats()
    {
        int count;
        try { count = GPU.GetDeviceCount(); }
        catch { return; }   // no NativeAcceleration or no driver

        CheckboxesGPUStats = Enumerable.Range(0, count)
            .Select(i => new CheckBox { IsChecked = true, Margin = new Thickness(10, 0), Focusable = false })
            .ToArray();
        foreach (var box in CheckboxesGPUStats)
            PanelGPUStats.Children.Add(box);

        void Update()
        {
            for (int i = 0; i < CheckboxesGPUStats.Length; i++)
            {
                try
                {
                    long free = GPU.GetFreeMemory(i);
                    IntPtr namePtr = GPU.GetDeviceName(i);
                    string name = System.Runtime.InteropServices.Marshal.PtrToStringAnsi(namePtr) ?? "";
                    CPU.HostFree(namePtr);
                    CheckboxesGPUStats[i].Content = $"#{i}, {name}: {(free / 1024f):F1} GB";
                }
                catch { }
            }
        }

        Update();
        TimerGPUStats = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background, (s, e) => Update());
        TimerGPUStats.Start();
    }

    private List<int> GetDeviceList()
    {
        var devices = new List<int>();
        for (int i = 0; i < CheckboxesGPUStats.Length; i++)
            if (CheckboxesGPUStats[i].IsChecked == true)
                devices.Add(i);
        return devices;
    }

    #endregion
}
