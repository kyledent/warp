using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Warp.Tools;

namespace WarpAvalonia.Dialogs;

/// <summary>
/// Avalonia port of Warp.Controls.BoxNetSelect.
/// Presents dialog to select BoxNet model from local directory.
/// </summary>
public partial class BoxNetSelect : Window
{
    public string? SelectedModelPath { get; private set; }

    public BoxNetSelect(string? modelDirectory = null)
    {
        InitializeComponent();
        if (modelDirectory != null && Directory.Exists(modelDirectory))
        {
            var models = Directory.EnumerateFiles(modelDirectory, "*.pt")
                .Concat(Directory.EnumerateFiles(modelDirectory, "*.onnx"))
                .Select(p => Helper.PathToName(p))
                .ToList();
            ListModels.ItemsSource = models;
        }
    }

    private void ButtonCancel_OnClick(object? sender, RoutedEventArgs e) => Close(null);

    private void ButtonUse_OnClick(object? sender, RoutedEventArgs e)
    {
        if (ListModels.SelectedItem is string name)
        {
            SelectedModelPath = name;
            Close(name);
        }
    }

    private async void ButtonBrowse_OnClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select BoxNet Model",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Models") { Patterns = new[] { "*.pt", "*.onnx" } } }
        });
        if (files.Count > 0)
        {
            SelectedModelPath = files[0].Path.LocalPath;
            Close(SelectedModelPath);
        }
    }
}
