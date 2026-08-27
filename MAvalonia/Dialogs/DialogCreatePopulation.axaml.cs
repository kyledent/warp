using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace MAvalonia.Dialogs;

public partial class DialogCreatePopulation : Window
{
    public string? PopulationName => TextPopulationName.Text;
    public string? FolderPath { get; private set; }

    public DialogCreatePopulation()
    {
        InitializeComponent();
    }

    private void ButtonCancel_OnClick(object? sender, RoutedEventArgs e) => Close(null);

    private void ButtonCreate_OnClick(object? sender, RoutedEventArgs e) => Close(FolderPath);

    private async void ButtonChangeFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select folder for population", AllowMultiple = false
        });
        if (folder.Count > 0)
        {
            FolderPath = folder[0].Path.LocalPath;
            ButtonChangeFolder.Content = FolderPath;
            ButtonCreate.IsVisible = true;
        }
    }
}
