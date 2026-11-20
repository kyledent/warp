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

    private async void ButtonRefine_OnClick(object? sender, RoutedEventArgs e)
    {
        if (ActivePopulation == null || ActivePopulation.Species.Count == 0)
        {
            Title = "M — Add at least one species before refining";
            return;
        }

        // Not ported yet: the block below only simulates a run. Say so rather than report a refinement
        // that did not happen (WPF: M/MainWindow.xaml.cs, PerformRefinementIteration).
        Title = "M — Refinement is not yet available in this version. Use M (Windows) or MTools/MCore.";
        return;
#pragma warning disable CS0162

        ButtonRefine.IsEnabled = false;
        ButtonRefine.Content = "REFINING...";

        await Task.Run(() =>
        {
            try
            {
                // The full refinement pipeline (WPF MainWindow.cs lines 600-1000):
                // 1. Create ProcessingOptionsMPARefine from settings
                // 2. Spawn WorkerWrapper processes across GPUs
                // 3. For each species: half-set refinement with FSC tracking
                // 4. Update resolution estimates
                // 5. Save population

                // All processing logic is in WarpLib (Population.Refine, Species methods)
                // and is framework-agnostic.

                int nDevices = Math.Max(1, GPU.GetDeviceCount());

                Dispatcher.UIThread.InvokeAsync(() =>
                    Title = $"M — Refinement complete (simulated, {nDevices} GPU(s) available)");
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.InvokeAsync(() => Title = $"M — Refinement error: {ex.Message}");
            }
        });

        ButtonRefine.IsEnabled = true;
        ButtonRefine.Content = "REFINE";
    }
#pragma warning restore CS0162

    #endregion
}
