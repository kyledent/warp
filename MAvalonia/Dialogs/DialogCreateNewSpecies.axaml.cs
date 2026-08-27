using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
namespace MAvalonia.Dialogs;
public partial class DialogCreateNewSpecies : Window
{
    public string? Halfmap1Path { get; private set; }
    public string? Halfmap2Path { get; private set; }
    public string? MaskPath { get; private set; }
    public string? ParticlesPath { get; private set; }
    public DialogCreateNewSpecies() { InitializeComponent(); }
    private void ButtonCancel_OnClick(object? s, RoutedEventArgs e) => Close(null);
    private void ButtonCreate_OnClick(object? s, RoutedEventArgs e) => Close(new { Name = TextSpeciesName.Text, Diameter = TextDiameter.Text, Weight = TextWeight.Text, Symmetry = TextSymmetry.Text, Halfmap1 = Halfmap1Path, Halfmap2 = Halfmap2Path, Mask = MaskPath, Particles = ParticlesPath });
    private async void ButtonHalfmap1_OnClick(object? s, RoutedEventArgs e)
    { var f = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Half-map 1", AllowMultiple = false, FileTypeFilter = new[] { new FilePickerFileType("MRC") { Patterns = new[] { "*.mrc" } } } });
      if (f.Count > 0) { Halfmap1Path = f[0].Path.LocalPath; ButtonHalfmap1.Content = System.IO.Path.GetFileName(Halfmap1Path); } }
    private async void ButtonHalfmap2_OnClick(object? s, RoutedEventArgs e)
    { var f = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Half-map 2", AllowMultiple = false, FileTypeFilter = new[] { new FilePickerFileType("MRC") { Patterns = new[] { "*.mrc" } } } });
      if (f.Count > 0) { Halfmap2Path = f[0].Path.LocalPath; ButtonHalfmap2.Content = System.IO.Path.GetFileName(Halfmap2Path); } }
    private async void ButtonMask_OnClick(object? s, RoutedEventArgs e)
    { var f = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Mask", AllowMultiple = false, FileTypeFilter = new[] { new FilePickerFileType("MRC") { Patterns = new[] { "*.mrc" } } } });
      if (f.Count > 0) { MaskPath = f[0].Path.LocalPath; TextMaskStatus.Text = System.IO.Path.GetFileName(MaskPath); } }
    private async void ButtonParticles_OnClick(object? s, RoutedEventArgs e)
    { var f = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Particles", AllowMultiple = false, FileTypeFilter = new[] { new FilePickerFileType("STAR") { Patterns = new[] { "*.star" } } } });
      if (f.Count > 0) { ParticlesPath = f[0].Path.LocalPath; TextParticleStatus.Text = System.IO.Path.GetFileName(ParticlesPath); } }
}
