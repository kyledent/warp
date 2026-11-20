using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Warp;
using Warp.Tools;
using WarpAvalonia.Controls;
using WarpAvalonia.Processing;

namespace WarpAvalonia;

public partial class MainWindow : Window
{
    private OptionsWarp Options;
    private bool IsProcessing = false;
    private CancellationTokenSource? ProcessingCts;
    private FileDiscoverer Discoverer = new();
    private Movie? DisplayedMovie;
    private Movie[] CurrentItems = Array.Empty<Movie>();

    private static readonly Color[] StatusColors = {
        Color.FromArgb(200, 0x77, 0xcd, 0x77),  // 0: processed (green)
        Color.FromArgb(200, 0xff, 0xc4, 0x56),  // 1: outdated (yellow)
        Color.FromArgb(200, 0xff, 0x7d, 0x7d),  // 2: unprocessed (red)
        Color.FromArgb(200, 0x7e, 0xdf, 0xff),  // 3: filtered (cyan)
        Color.FromArgb(200, 0xc0, 0xc0, 0xc0),  // 4: deselected (gray)
    };

    private StringBuilder LogBuffer = new();

    /// <summary>
    /// Append a timestamped message to the scrollable log panel.
    /// Thread-safe — dispatches to UI thread if needed.
    /// </summary>
    public void Log(string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        LogBuffer.AppendLine(line);

        if (Dispatcher.UIThread.CheckAccess())
        {
            TextLog.Text = LogBuffer.ToString();
            ScrollLog.ScrollToEnd();
        }
        else
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                TextLog.Text = LogBuffer.ToString();
                ScrollLog.ScrollToEnd();
            });
        }
    }

    private async void ButtonCopyLog_OnClick(object? sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
            await clipboard.SetTextAsync(LogBuffer.ToString());
        Log("Log copied to clipboard.");
    }

    private void ButtonClearLog_OnClick(object? sender, RoutedEventArgs e)
    {
        LogBuffer.Clear();
        TextLog.Text = "";
    }

    public MainWindow()
    {
        InitializeComponent();

        Options = new OptionsWarp();

        // Start from WarpLib's defaults, then the previous session's settings, as WPF Warp does.
        // Test hook: --load-settings <file> replaces the previous session's settings.
        if (!string.IsNullOrEmpty(Program.LoadSettingsPath))
            Options.Load(Program.LoadSettingsPath);
        else if (File.Exists(PreviousSettingsPath))
            try { Options.Load(PreviousSettingsPath); }
            catch (Exception ex) { Log($"Could not load {PreviousSettingsPath}: {ex.Message}"); }

        DataContext = Options;

        Options.PropertyChanged += Options_PropertyChanged;
        TomoAdjustInterface();
        RefreshPathLabels();

        // The test hooks must not write previous.settings anywhere, nor prompt.
        bool testHooks = !string.IsNullOrEmpty(Program.LoadSettingsPath) || !string.IsNullOrEmpty(Program.SaveSettingsAndExitPath)
                         || !string.IsNullOrEmpty(Program.OpenDialogName);
        OptionsAutoSave = !testHooks;
        OptionsLookForFolderOptions = !testHooks;

        // Test hook: --save-settings-and-exit <file> saves once the window has bound and laid out
        // (so any value a control coerced is what gets written), then closes.
        if (!string.IsNullOrEmpty(Program.SaveSettingsAndExitPath))
        {
            Opened += (s, e) => Dispatcher.UIThread.Post(() =>
            {
                Options.Save(Program.SaveSettingsAndExitPath);
                Console.WriteLine($"Settings saved to {Program.SaveSettingsAndExitPath}");
                Close();
            }, DispatcherPriority.Background);
        }

        InitGPUStats();

        // Test hook: --open-dialog <name> [--dialog-file <path>] --dump-controls-and-exit <out.tsv>
        if (!string.IsNullOrEmpty(Program.OpenDialogName))
            Opened += (s, e) => Dispatcher.UIThread.Post(OpenDialogForTest, DispatcherPriority.Background);

        // Wire up FileDiscoverer
        Discoverer.FilesChanged += () => Dispatcher.UIThread.InvokeAsync(OnFilesChanged);
        Discoverer.IncubationStarted += () => Dispatcher.UIThread.InvokeAsync(() =>
            Log("New files detected, waiting for write to complete..."));
        Discoverer.IncubationEnded += () => Dispatcher.UIThread.InvokeAsync(() =>
            Log("Ready"));

        if (!string.IsNullOrEmpty(Options.Import.DataFolder) && Directory.Exists(Options.Import.DataFolder))
            StartDiscovery();

        if (Program.DemoMode)
        {
            TextStatus.Text = $"DEMO MODE — OptionsWarp bound ({Options.GetType().Assembly.GetName().Name}), synthetic data loaded";
            LoadDemoData();
        }
        else
        {
            TextStatus.Text = "Ready. Select a data folder to begin, or relaunch with --demo for synthetic data.";
        }
    }

    /// <summary>
    /// Loads synthetic test data into all visualization controls.
    /// Only called when launched with --demo flag.
    /// </summary>
    private void LoadDemoData()
    {
        // Micrograph display (Real Space tab)
        MicrographDisplayControl.LoadTestData(512, 512);

        // Scatter plot colors (matching Warp's status palette)
        var statusColors = new[] {
            Avalonia.Media.Color.FromArgb(200, 0x77, 0xcd, 0x77),  // processed (green)
            Avalonia.Media.Color.FromArgb(200, 0xff, 0xc4, 0x56),  // outdated (yellow)
            Avalonia.Media.Color.FromArgb(200, 0xff, 0x7d, 0x7d),  // unprocessed (red)
            Avalonia.Media.Color.FromArgb(200, 0x7e, 0xdf, 0xff),  // filtered (cyan)
        };

        // Overview scatter plots
        PlotDefocus.PointColors = statusColors;
        PlotDefocus.Points = SingleAxisScatter.GenerateTestData(80, 3.0, 1.5);
        PlotDefocus.RangeHighlightMin = 0.5;
        PlotDefocus.RangeHighlightMax = 6.0;

        PlotResolution.PointColors = statusColors;
        PlotResolution.Points = SingleAxisScatter.GenerateTestData(80, 5.0, 2.0);

        PlotMotion.PointColors = statusColors;
        PlotMotion.Points = SingleAxisScatter.GenerateTestData(80, 8.0, 3.0);

        PlotParticles.PointColors = statusColors;
        PlotParticles.Points = SingleAxisScatter.GenerateTestData(80, 500, 200);

        // Processing status donut chart
        ChartStatus.SetSlices(new System.Collections.Generic.List<ChartSlice>
        {
            new("Processed", 45, Avalonia.Media.Color.Parse("#77cd77")),
            new("Outdated", 8, Avalonia.Media.Color.Parse("#ffc456")),
            new("Unprocessed", 15, Avalonia.Media.Color.Parse("#ff7d7d")),
            new("Filtered Out", 7, Avalonia.Media.Color.Parse("#7edfff")),
            new("Deselected", 5, Avalonia.Media.Color.Parse("#c0c0c0")),
        });
        ChartStatus.CenterText = "45";

        // Astigmatism scatter
        PlotAstigmatism.PointColors = new[] {
            Avalonia.Media.Color.FromArgb(150, 0x77, 0xcd, 0x77),
            Avalonia.Media.Color.FromArgb(150, 0xff, 0xc4, 0x56),
            Avalonia.Media.Color.FromArgb(150, 0xff, 0x7d, 0x7d),
            Avalonia.Media.Color.FromArgb(150, 0x7e, 0xdf, 0xff),
        };
        PlotAstigmatism.AxisMax = 500;
        PlotAstigmatism.Points = DualAxisScatter.GenerateTestData(80, 200);

        // CTF display (Fourier Space tab)
        CTFDisplayControl.LoadTestData(defocus: 3.0, cs: 2.7, voltage: 300, pixelSize: 1.5);

        // Processing status bar
        StatusBarControl.SetItems(ProcessingStatusBar.GenerateTestItems(120));
        StatusBarControl.SetActiveIndex(45);
    }

    #region Options: previous.settings, path labels, GPU list (as WPF Warp)

    const string DefaultOptionsName = "previous.settings";

    // WPF Warp keeps previous.settings next to Warp.exe. A Linux install or image is read-only,
    // so the per-user application-data folder is used instead.
    static readonly string PreviousSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Warp", DefaultOptionsName);

    bool OptionsAutoSave = false;
    bool OptionsLookForFolderOptions = false;

    private async void Options_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Options_PropertyChanged(sender, e));
            return;
        }

        string name = e.PropertyName ?? "";

        if (name is "Import.DataFolder" or "Import.ProcessingFolder" or "Import.DoRecursiveSearch")
        {
            RefreshPathLabels();

            if (OptionsLookForFolderOptions && !string.IsNullOrEmpty(Options.Import.ProcessingOrDataFolder))
            {
                OptionsLookForFolderOptions = false;
                try
                {
                    string folderOptions = Path.Combine(Options.Import.ProcessingOrDataFolder, DefaultOptionsName);
                    if (File.Exists(folderOptions) &&
                        await AskYesNo("Options File Found in Folder",
                                       "A file with options from a previous Warp session was found in this folder. Load it?"))
                    {
                        if (string.IsNullOrEmpty(Options.Import.ProcessingFolder))
                        {
                            string selected = Options.Import.DataFolder;
                            Options.Load(folderOptions);
                            Options.Import.DataFolder = selected;
                        }
                        else
                        {
                            string selected = Options.Import.ProcessingFolder;
                            Options.Load(folderOptions);
                            Options.Import.ProcessingFolder = selected;
                        }
                        RefreshPathLabels();
                        Log($"Settings loaded from {folderOptions}");
                    }
                }
                catch (Exception ex) { Log($"Could not load folder settings: {ex.Message}"); }
                finally { OptionsLookForFolderOptions = true; }
            }

            if (!string.IsNullOrEmpty(Options.Import.DataFolder))
                StartDiscovery();
            TomoAdjustInterface();
        }
        else if (name == "Import.Extension")
        {
            if (!string.IsNullOrEmpty(Options.Import.DataFolder))
                StartDiscovery();
            TomoAdjustInterface();
        }
        else if (name == "Import.GainPath")
        {
            if (!string.IsNullOrEmpty(Options.Import.GainPath))
            {
                if (!File.Exists(Options.Import.GainPath))
                {
                    Options.Import.GainPath = "";
                    return;
                }
                Options.Import.GainReferenceHash = MathHelper.GetSHA1(Options.Import.GainPath, 1 << 20);
            }
            RefreshPathLabels();
        }
        else if (name == "Import.DefectsPath")
        {
            if (!string.IsNullOrEmpty(Options.Import.DefectsPath))
            {
                if (!File.Exists(Options.Import.DefectsPath))
                {
                    Options.Import.DefectsPath = "";
                    return;
                }
                Options.Import.DefectMapHash = MathHelper.GetSHA1(Options.Import.DefectsPath, 1 << 20);
            }
            RefreshPathLabels();
        }
        else if (name == "Picking.ModelPath")
        {
            RefreshPathLabels();
        }

        SaveDefaultSettings();
    }

    private void SaveDefaultSettings()
    {
        if (!OptionsAutoSave)
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PreviousSettingsPath)!);
            Options.Save(PreviousSettingsPath);
        }
        catch { }

        if (!string.IsNullOrEmpty(Options.Import.ProcessingOrDataFolder))
            try { Options.Save(Path.Combine(Options.Import.ProcessingOrDataFolder, DefaultOptionsName)); }
            catch { }
    }

    private static TextBlock PathLabel(string path, string empty) => new TextBlock
    {
        Text = string.IsNullOrEmpty(path) ? empty : path,
        TextTrimming = TextTrimming.PathSegmentEllipsis,
        MaxWidth = 330
    };

    private void RefreshPathLabels()
    {
        void Set(Button button, string path, string empty)
        {
            button.Content = PathLabel(path, empty);
            ToolTip.SetTip(button, string.IsNullOrEmpty(path) ? null : path);
        }

        Set(ButtonInputPath, Options.Import.DataFolder, "Select folder...");
        Set(ButtonInputProcessingPath, Options.Import.ProcessingFolder, "Select folder...");
        Set(ButtonGainPath, Options.Import.GainPath, "Select reference...");
        Set(ButtonDefectsPath, Options.Import.DefectsPath, "Select defect map...");
        Set(ButtonPickingModelName, Options.Picking.ModelPath, "Select BoxNet model...");
    }

    private async Task<bool> AskYesNo(string title, string message)
    {
        bool result = false;
        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var yes = new Button { Content = "Yes", MinWidth = 80, IsDefault = true };
        var no = new Button { Content = "No", MinWidth = 80, IsCancel = true };
        yes.Click += (s, e) => { result = true; dialog.Close(); };
        no.Click += (s, e) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { yes, no }
                }
            }
        };
        await dialog.ShowDialog(this);
        return result;
    }

    private CheckBox[] CheckboxesGPUStats = Array.Empty<CheckBox>();
    private DispatcherTimer? TimerGPUStats;

    /// <summary>
    /// One checkbox per GPU with its free memory, refreshed every 200 ms, as WPF Warp's title bar.
    /// Unticked GPUs are not used for processing.
    /// </summary>
    private void InitGPUStats()
    {
        int count;
        try { count = GPU.GetDeviceCount(); }
        catch { return; }   // no NativeAcceleration or no driver: processing reports it when started

        CheckboxesGPUStats = Enumerable.Range(0, count)
            .Select(i => new CheckBox { IsChecked = true, Margin = new Avalonia.Thickness(10, 0), Focusable = false })
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
                    string gpuName = System.Runtime.InteropServices.Marshal.PtrToStringAnsi(namePtr) ?? "";
                    CPU.HostFree(namePtr);
                    CheckboxesGPUStats[i].Content = $"#{i}, {gpuName}: {(free / 1024f):F1} GB";
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

    #region Test hook: open a task dialog and dump its controls

    private Window? CreateDialog(string name, string file)
    {
        var movies = CurrentItems.Where(m => m is not TiltSeries).ToArray();
        var series = CurrentItems.OfType<TiltSeries>().ToArray();
        return name switch
        {
            "BoxNetExport" => new Dialogs.BoxNetExport(movies, Options),
            "BoxNetTrain" => new Dialogs.BoxNetTrain(movies, Options),
            "Dialog2DDefocusUpdate" => new Dialogs.Dialog2DDefocusUpdate(movies, file, Options),
            "Dialog2DList" => new Dialogs.Dialog2DList(movies, Options),
            "Dialog2DMatch" => new Dialogs.Dialog2DMatch(movies, file, Options),
            "Dialog2DParticleExport" => new Dialogs.Dialog2DParticleExport(movies, file, Options),
            "Dialog2DParticleImport" => new Dialogs.Dialog2DParticleImport(movies, file, Options),
            "DialogTomoImportImod" => new Dialogs.DialogTomoImportImod(Options),
            "DialogTomoList" => new Dialogs.DialogTomoList(series, Options),
            "DialogTomoMatch" => new Dialogs.DialogTomoMatch(series, file, Options),
            "DialogTomoParticleExport" => new Dialogs.DialogTomoParticleExport(series, file, Options),
            "DialogTomoReconstruction" => new Dialogs.DialogTomoReconstruction(series, Options, GetDeviceList()),
            _ => null
        };
    }

    private void OpenDialogForTest()
    {
        string name = Program.OpenDialogName!;
        string file = Program.DialogFilePath ?? "";
        Window? dialog;
        try { dialog = CreateDialog(name, file); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"--open-dialog {name}: {ex.GetType().Name}: {ex.Message}");
            Environment.ExitCode = 2;
            Close();
            return;
        }
        if (dialog == null)
        {
            Console.Error.WriteLine($"--open-dialog: unknown dialog '{name}'");
            Environment.ExitCode = 2;
            Close();
            return;
        }

        if (string.IsNullOrEmpty(Program.DumpControlsAndExitPath))
        {
            dialog.ShowDialog(this);
            return;
        }

        dialog.Opened += (s, e) => Dispatcher.UIThread.Post(() =>
        {
            File.WriteAllText(Program.DumpControlsAndExitPath!, DumpControls(dialog));
            Console.WriteLine($"Controls of {name} written to {Program.DumpControlsAndExitPath}");
            dialog.Close();
            Close();
        }, DispatcherPriority.Background);
        dialog.Show(this);
    }

    /// <summary>
    /// One row per visible text, value slider, check box, combo box or text box, in layout order:
    /// what a user sees, without clicking.
    /// </summary>
    private static string DumpControls(Control root)
    {
        static string Clean(object? o) => (o?.ToString() ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
        static string ContentText(object? content) => content switch
        {
            TextBlock t => t.Text ?? "",
            null => "",
            _ => content.ToString() ?? ""
        };

        var sb = new StringBuilder();
        sb.AppendLine("type\tname\ttext\tvalue\tmin\tmax\tstep\tdecimals\tsuffix\tenabled");
        foreach (var c in Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(root).OfType<Control>())
        {
            if (!c.IsEffectivelyVisible)
                continue;
            string enabled = c.IsEffectivelyEnabled ? "1" : "0";
            switch (c)
            {
                case ValueSlider v:
                    sb.AppendLine($"ValueSlider\t{Clean(v.Name)}\t\t{v.Value.ToString(CultureInfo.InvariantCulture)}\t{v.MinValue.ToString(CultureInfo.InvariantCulture)}\t{v.MaxValue.ToString(CultureInfo.InvariantCulture)}\t{v.StepSize.ToString(CultureInfo.InvariantCulture)}\t{v.DecimalPlaces}\t{Clean(v.Suffix)}\t{enabled}");
                    break;
                case CheckBox cb:
                    sb.AppendLine($"CheckBox\t{Clean(cb.Name)}\t{Clean(ContentText(cb.Content))}\t{Clean(cb.IsChecked)}\t\t\t\t\t\t{enabled}");
                    break;
                case ComboBox combo:
                    sb.AppendLine($"ComboBox\t{Clean(combo.Name)}\t\t{Clean(combo.SelectedItem is ComboBoxItem ci ? ci.Content : combo.SelectedItem)}\t\t\t\t\t\t{enabled}");
                    break;
                case TextBox tb:
                    sb.AppendLine($"TextBox\t{Clean(tb.Name)}\t\t{Clean(tb.Text)}\t\t\t\t\t\t{enabled}");
                    break;
                case Button b:
                    sb.AppendLine($"Button\t{Clean(b.Name)}\t{Clean(ContentText(b.Content))}\t\t\t\t\t\t\t{enabled}");
                    break;
                case TextBlock t when t.Parent is not ValueSlider && t.Parent is not Button && t.Parent is not CheckBox
                                     && !string.IsNullOrWhiteSpace(t.Text):
                    sb.AppendLine($"Text\t{Clean(t.Name)}\t{Clean(t.Text)}\t\t\t\t\t\t\t{enabled}");
                    break;
            }
        }
        return sb.ToString();
    }

    #endregion

    /// <summary>
    /// Shows the 2D or the tilt-series controls, following WPF Warp's TomoAdjustInterface:
    /// tilt series hide motion, models, picking, output and the 2D tasks; 2D data hides the
    /// tomogram dimensions and the 3D tasks, and shows EER grouping only for EER data.
    /// </summary>
    private void TomoAdjustInterface()
    {
        bool isTomo = Options.Import.ExtensionTomoSTAR;

        PanelTomoDims.IsVisible = isTomo;
        PanelOverviewTasks3D.IsVisible = isTomo;

        PanelSections2D.IsVisible = !isTomo;
        PanelOverviewTasks2D.IsVisible = !isTomo;
        PanelExposure2D.IsVisible = !isTomo;
        PanelEERGrouping.IsVisible = !isTomo && Options.Import.ExtensionEER;

        if (isTomo)
        {
            Options.ProcessMovement = false;
            Options.Export.DoAverage = false;
            Options.Export.DoDeconvolve = false;
            Options.Export.DoStack = false;
        }
    }

    #region File Discovery & Statistics

    private void StartDiscovery()
    {
        string extension = Options.Import.Extension;
        if (string.IsNullOrEmpty(extension)) extension = "*.*";

        Discoverer.ChangePath(
            Options.Import.DataFolder,
            Options.Import.ProcessingFolder,
            extension,
            Options.Import.DoRecursiveSearch);

        Log($"Scanning {Options.Import.DataFolder}...");
    }

    private void OnFilesChanged()
    {
        CurrentItems = Discoverer.GetImmutableFiles();

        // Update status bar with real items
        var statusItems = CurrentItems.Select(m =>
        {
            var status = GetMovieStatus(m);
            return new StatusItem(m.RootName, status);
        }).ToList();

        StatusBarControl.SetItems(statusItems);

        // Auto-select first item
        if (DisplayedMovie == null && CurrentItems.Length > 0)
            SetDisplayedMovie(CurrentItems[0]);

        Log($"Found {CurrentItems.Length} items");

        UpdateStatsAll();
    }

    private void SetDisplayedMovie(Movie movie)
    {
        DisplayedMovie = movie;

        // Find index in current items for status bar highlight
        int idx = Array.IndexOf(CurrentItems, movie);
        if (idx >= 0) StatusBarControl.SetActiveIndex(idx);

        // Update CTF display
        if (movie.PS1D != null && movie.Simulated1D != null)
        {
            float defocus = movie.CTF != null ? (float)movie.CTF.Defocus : 3.0f;
            float cs = movie.CTF != null ? (float)movie.CTF.Cs : 2.7f;
            float voltage = movie.CTF != null ? (float)movie.CTF.Voltage : 300f;
            float pixelSize = movie.OptionsCTF != null ? (float)movie.OptionsCTF.BinnedPixelSizeMean : 1.5f;
            CTFDisplayControl.LoadTestData(defocus, cs, voltage, pixelSize);
        }

        // Update micrograph display if average exists
        if (File.Exists(movie.AveragePath))
        {
            try
            {
                var header = Warp.Headers.MapHeader.ReadFromFile(movie.AveragePath);
                float[] data = Warp.Image.FromFile(movie.AveragePath).GetHostContinuousCopy();
                MicrographDisplayControl.LoadImageData(data, header.Dimensions.X, header.Dimensions.Y,
                    movie.OptionsCTF != null ? (float)movie.OptionsCTF.BinnedPixelSizeMean : 1.5f);
            }
            catch (Exception ex)
            {
                Log($"Failed to load micrograph: {ex.Message}");
            }
        }
    }

    private void UpdateStatsAll()
    {
        if (CurrentItems.Length == 0) return;

        int nProcessed = 0, nOutdated = 0, nUnprocessed = 0, nFiltered = 0, nDeselected = 0;

        var defocusPoints = new ObservableCollection<ScatterPoint>();
        var resolutionPoints = new ObservableCollection<ScatterPoint>();
        var motionPoints = new ObservableCollection<ScatterPoint>();
        var particlePoints = new ObservableCollection<ScatterPoint>();

        foreach (var item in CurrentItems)
        {
            var status = GetMovieStatus(item);
            int colorID = status switch
            {
                ItemStatus.Processed => 0,
                ItemStatus.Outdated => 1,
                ItemStatus.Unprocessed => 2,
                ItemStatus.FilteredOut => 3,
                ItemStatus.Deselected => 4,
                _ => 2
            };

            switch (status)
            {
                case ItemStatus.Processed: nProcessed++; break;
                case ItemStatus.Outdated: nOutdated++; break;
                case ItemStatus.Unprocessed: nUnprocessed++; break;
                case ItemStatus.FilteredOut: nFiltered++; break;
                case ItemStatus.Deselected: nDeselected++; break;
            }

            double defocus = item.CTF != null ? (double)item.CTF.Defocus : double.NaN;
            double resolution = item.CTFResolutionEstimate > 0 ? (double)item.CTFResolutionEstimate : double.NaN;
            double motion = item.MeanFrameMovement > 0 ? (double)item.MeanFrameMovement : double.NaN;
            double particles = double.NaN; // Particle count not directly on Movie base

            defocusPoints.Add(new ScatterPoint(defocus, colorID));
            resolutionPoints.Add(new ScatterPoint(resolution, colorID));
            motionPoints.Add(new ScatterPoint(motion, colorID));
            particlePoints.Add(new ScatterPoint(particles, colorID));
        }

        // Update plots
        PlotDefocus.PointColors = StatusColors;
        PlotDefocus.Points = defocusPoints;

        PlotResolution.PointColors = StatusColors;
        PlotResolution.Points = resolutionPoints;

        PlotMotion.PointColors = StatusColors;
        PlotMotion.Points = motionPoints;

        PlotParticles.PointColors = StatusColors;
        PlotParticles.Points = particlePoints;

        // Update donut chart
        ChartStatus.SetSlices(new List<ChartSlice>
        {
            new("Processed", nProcessed, Color.Parse("#77cd77")),
            new("Outdated", nOutdated, Color.Parse("#ffc456")),
            new("Unprocessed", nUnprocessed, Color.Parse("#ff7d7d")),
            new("Filtered Out", nFiltered, Color.Parse("#7edfff")),
            new("Deselected", nDeselected, Color.Parse("#c0c0c0")),
        });
        ChartStatus.CenterText = nProcessed.ToString();

        Log($"{CurrentItems.Length} items: {nProcessed} processed, {nOutdated} outdated, {nUnprocessed} unprocessed");
    }

    private static ItemStatus GetMovieStatus(Movie movie)
    {
        if (movie.UnselectManual != null && (bool)movie.UnselectManual)
            return ItemStatus.Deselected;
        if (movie.UnselectFilter)
            return ItemStatus.FilteredOut;
        if (movie.OptionsCTF != null)
            return ItemStatus.Processed;
        return ItemStatus.Unprocessed;
    }

    #endregion

    #region Settings Save/Load

    private async void ButtonOptionsSave_OnClick(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Settings",
            DefaultExtension = "settings",
            FileTypeChoices = new[] { new FilePickerFileType("Warp Settings") { Patterns = new[] { "*.settings" } } }
        });

        if (file != null)
        {
            Options.Save(file.Path.LocalPath);
            Log($"Settings saved to {file.Path.LocalPath}");
        }
    }

    private async void ButtonOptionsLoad_OnClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load Settings",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Warp Settings") { Patterns = new[] { "*.settings" } } }
        });

        if (files.Count > 0)
        {
            Options.Load(files[0].Path.LocalPath);
            Log($"Settings loaded from {files[0].Path.LocalPath}");
        }
    }

    #endregion

    #region Input Path Buttons

    private async void ButtonInputPath_OnClick(object? sender, RoutedEventArgs e)
    {
        var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Data Folder",
            AllowMultiple = false
        });

        if (folder.Count > 0)
        {
            Options.Import.DataFolder = folder[0].Path.LocalPath;
            Log($"Data folder: {folder[0].Path.LocalPath}");
        }
    }

    private async void ButtonInputProcessingPath_OnClick(object? sender, RoutedEventArgs e)
    {
        var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Processing Folder",
            AllowMultiple = false
        });

        if (folder.Count > 0)
        {
            Options.Import.ProcessingFolder = folder[0].Path.LocalPath;
            Log($"Processing folder: {folder[0].Path.LocalPath}");
        }
    }

    private void ButtonInputRemoveProcessingPath_OnClick(object? sender, RoutedEventArgs e)
    {
        Options.Import.ProcessingFolder = "";
    }

    #endregion

    #region Preprocessing Path Buttons

    private async void ButtonGainPath_OnClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Gain Reference",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Gain Reference") { Patterns = new[] { "*.mrc", "*.dm4", "*.gain", "*.*" } } }
        });

        if (files.Count > 0)
        {
            Options.Import.GainPath = files[0].Path.LocalPath;
        }
    }

    private async void ButtonDefectsPath_OnClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Defect Map",
            AllowMultiple = false
        });

        if (files.Count > 0)
        {
            Options.Import.DefectsPath = files[0].Path.LocalPath;
        }
    }

    #endregion

    #region Picking Model

    private async void ButtonPickingModelName_OnClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select BoxNet Model",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("BoxNet Model") { Patterns = new[] { "*.pt", "*.onnx", "*.*" } } }
        });

        if (files.Count > 0)
        {
            Options.Picking.ModelPath = files[0].Path.LocalPath;
        }
    }

    #endregion

    #region Task Dialog Launchers

    private async void ButtonTasksExportMicrographs_OnClick(object? sender, RoutedEventArgs e)
    {
        var movies = CurrentItems.Where(m => m is not TiltSeries).ToArray();
        var dialog = new Dialogs.Dialog2DList(movies, Options);
        await dialog.ShowDialog(this);
    }

    private async void ButtonTasksAdjustDefocus_OnClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select STAR file with particles",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("STAR") { Patterns = new[] { "*.star" } } }
        });
        if (files.Count == 0) return;

        var movies = CurrentItems.Where(m => m is not TiltSeries).ToArray();
        var dialog = new Dialogs.Dialog2DDefocusUpdate(movies, files[0].Path.LocalPath, Options);
        await dialog.ShowDialog(this);
    }

    private async void ButtonTasksExportParticles_OnClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select STAR file with particles",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("STAR") { Patterns = new[] { "*.star" } } }
        });
        if (files.Count == 0) return;

        var movies = CurrentItems.Where(m => m is not TiltSeries).ToArray();
        var dialog = new Dialogs.Dialog2DParticleExport(movies, files[0].Path.LocalPath, Options);
        await dialog.ShowDialog(this);
    }

    private async void ButtonTasksMatch_OnClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select template MRC volume",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("MRC") { Patterns = new[] { "*.mrc" } } }
        });
        if (files.Count == 0) return;

        var movies = CurrentItems.Where(m => m is not TiltSeries).ToArray();
        var dialog = new Dialogs.Dialog2DMatch(movies, files[0].Path.LocalPath, Options);
        await dialog.ShowDialog(this);
    }

    private async void ButtonTasksImportParticles_OnClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select a file with particle coordinates for either the entire data set or one movie",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("STAR") { Patterns = new[] { "*.star" } } }
        });
        if (files.Count == 0) return;

        var movies = CurrentItems.Where(m => m is not TiltSeries).ToArray();
        var dialog = new Dialogs.Dialog2DParticleImport(movies, files[0].Path.LocalPath, Options);
        await dialog.ShowDialog(this);
        UpdateStatsAll();
    }

    private async void ButtonTasksExportBoxNet_OnClick(object? sender, RoutedEventArgs e)
    {
        var movies = CurrentItems.Where(m => m is not TiltSeries).ToArray();
        var dialog = new Dialogs.BoxNetExport(movies, Options);
        await dialog.ShowDialog(this);
    }

    private async void ButtonTasksExportTomograms_OnClick(object? sender, RoutedEventArgs e)
    {
        var series = CurrentItems.OfType<TiltSeries>().ToArray();
        var dialog = new Dialogs.DialogTomoList(series, Options);
        await dialog.ShowDialog(this);
    }

    private async void ButtonTasksImportImod_OnClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new Dialogs.DialogTomoImportImod(Options);
        await dialog.ShowDialog(this);
    }

    private async void ButtonTasksReconstructTomograms_OnClick(object? sender, RoutedEventArgs e)
    {
        var series = CurrentItems.OfType<TiltSeries>().ToArray();
        var devices = GetDeviceList();
        if (devices.Count == 0) { Log("No GPU selected."); return; }
        var dialog = new Dialogs.DialogTomoReconstruction(series, Options, devices);
        await dialog.ShowDialog(this);
        UpdateStatsAll();
    }

    private async void ButtonTasksExportSubtomograms_OnClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select STAR file with particle coordinates",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("STAR") { Patterns = new[] { "*.star" } } }
        });
        if (files.Count == 0) return;

        var series = CurrentItems.OfType<TiltSeries>().ToArray();
        var dialog = new Dialogs.DialogTomoParticleExport(series, files[0].Path.LocalPath, Options);
        await dialog.ShowDialog(this);
        UpdateStatsAll();
    }

    private async void ButtonTasksMatchTomograms_OnClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select template MRC volume",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("MRC") { Patterns = new[] { "*.mrc" } } }
        });
        if (files.Count == 0) return;

        var series = CurrentItems.OfType<TiltSeries>().ToArray();
        var dialog = new Dialogs.DialogTomoMatch(series, files[0].Path.LocalPath, Options);
        await dialog.ShowDialog(this);
        UpdateStatsAll();
    }

    #endregion

    #region Processing

    private void ButtonStartProcessing_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!IsProcessing)
        {
            StartProcessing();
        }
        else
        {
            StopProcessing();
        }
    }

    private void StartProcessing()
    {
        // Validate before starting
        if (string.IsNullOrEmpty(Options.Import.DataFolder) || !System.IO.Directory.Exists(Options.Import.DataFolder))
        {
            Log("Cannot start processing: no data folder selected.");
            return;
        }

        if (!Options.ProcessCTF && !Options.ProcessMovement && !Options.ProcessPicking)
        {
            Log("Cannot start processing: no processing steps enabled (CTF, Motion, or Picking).");
            return;
        }

        // Check GPU availability
        int nDevices = 0;
        try { nDevices = GPU.GetDeviceCount(); }
        catch (Exception ex)
        {
            Log($"GPU ERROR: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
                Log($"  Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            Log($"  Stack: {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim() ?? "N/A"}");
            Log("Processing requires NativeAcceleration.so + CUDA runtime.");
            Log("To build NativeAcceleration.so, see warp/BUILD_LOG_2026-02-13.md");
            return;
        }

        if (nDevices == 0)
        {
            Log("No GPU devices found. Processing requires NVIDIA GPU with CUDA drivers.");
            return;
        }

        var usedDevices = GetDeviceList();
        if (usedDevices.Count == 0)
        {
            Log("No GPU selected: tick at least one GPU in the list above the tabs.");
            return;
        }
        nDevices = usedDevices.Count;

        IsProcessing = true;
        ButtonStartProcessing.Content = "STOP PROCESSING";
        ProcessingCts = new CancellationTokenSource();
        var ct = ProcessingCts.Token;

        Log($"Processing started on {nDevices} GPU(s)...");

        Task.Run(async () =>
        {
            int itemsProcessed = 0;

            while (!ct.IsCancellationRequested)
            {
                // Get items needing processing
                var items = Discoverer.GetImmutableFiles();
                var needProcessing = items.Where(m =>
                {
                    if (m.UnselectManual != null && (bool)m.UnselectManual) return false;
                    if (m.UnselectFilter) return false;

                    if (Options.ProcessCTF && m.OptionsCTF == null) return true;
                    if (Options.ProcessMovement && m.OptionsMovement == null) return true;

                    return false;
                }).ToList();

                if (needProcessing.Count == 0)
                {
                    await Task.Delay(500, ct).ConfigureAwait(false);
                    await Dispatcher.UIThread.InvokeAsync(() =>
                        Log($"Processing idle — {items.Length} items, all up to date"));
                    continue;
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                    Log($"Processing {needProcessing.Count} items on {nDevices} GPU(s)..."));

                Helper.ForEachGPU(needProcessing, (item, gpuID) =>
                {
                    if (ct.IsCancellationRequested) return true;

                    try
                    {
                        if (Options.ProcessCTF && item.OptionsCTF == null)
                        {
                            var ctfOptions = Options.GetProcessingMovieCTF();
                            item.ProcessCTF(null, ctfOptions);
                        }

                        if (Options.ProcessMovement && item.OptionsMovement == null)
                        {
                            var movementOptions = Options.GetProcessingMovieMovement();
                            item.ProcessShift(null, movementOptions);
                        }

                        item.SaveMeta();
                        itemsProcessed++;

                        Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            Log($"Processed {item.RootName} ({itemsProcessed} total)");
                            UpdateStatsAll();
                        });
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.UIThread.InvokeAsync(() =>
                            Log($"Error processing {item.RootName}: {ex.Message}"));
                    }

                    return false;
                }, 1, usedDevices);
            }
        }, ct);
    }

    private void StopProcessing()
    {
        ProcessingCts?.Cancel();
        IsProcessing = false;
        ButtonStartProcessing.Content = "START PROCESSING";
        Log("Processing stopped.");
    }

    #endregion
}
