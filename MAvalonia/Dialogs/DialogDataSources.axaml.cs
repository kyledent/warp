using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Warp.Sociology;
namespace MAvalonia.Dialogs;
public partial class DialogDataSources : Window
{
    public Population Population;
    public DialogDataSources(Population population) { InitializeComponent(); Population = population;
        ListSources.ItemsSource = population.Sources.Select(s => s.Name).ToList(); }
    private void ButtonClose_OnClick(object? s, RoutedEventArgs e) => Close();
    private async void ButtonAdd_OnClick(object? s, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        { Title = "Select Warp .settings file", AllowMultiple = false,
          FileTypeFilter = new[] { new FilePickerFileType("Settings") { Patterns = new[] { "*.settings" } } } });
        if (files.Count > 0)
        {
            var source = new DataSource { Path = files[0].Path.LocalPath, Name = System.IO.Path.GetFileNameWithoutExtension(files[0].Path.LocalPath) };
            Population.Sources.Add(source);
            ListSources.ItemsSource = Population.Sources.Select(src => src.Name).ToList();
        }
    }
    private void ButtonRemove_OnClick(object? s, RoutedEventArgs e)
    {
        if (ListSources.SelectedIndex >= 0 && ListSources.SelectedIndex < Population.Sources.Count)
        { Population.Sources.RemoveAt(ListSources.SelectedIndex);
          ListSources.ItemsSource = Population.Sources.Select(src => src.Name).ToList(); }
    }
}
