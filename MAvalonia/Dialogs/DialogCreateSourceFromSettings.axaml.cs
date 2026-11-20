using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MAvalonia.Dialogs;

/// <summary>
/// Name, inclusion rules and frame limit for a new data source. Closes with true on CREATE;
/// the caller reads the fields. Port of WPF M's DialogCreateSourceFromSettings.
/// </summary>
public partial class DialogCreateSourceFromSettings : Window
{
    public DialogCreateSourceFromSettings()
    {
        InitializeComponent();
    }

    public void SetCounts(int processed, int filteredOut, int deselected)
    {
        TextProcessed.Text = $"{processed} processed";
        TextFilteredOut.Text = $"{filteredOut} filtered out";
        TextDeselected.Text = $"{deselected} deselected";
    }

    public void SetUsableFrames(int frames)
    {
        NumericFrames.Maximum = frames;
        NumericFrames.Value = frames;
    }

    public string SourceName => TextSourceName.Text ?? "";
    public bool IncludeFilteredOut => CheckFilter.IsChecked == true;
    public bool IncludeDeselected => CheckManual.IsChecked == true;
    public int FrameLimit => (int)(NumericFrames.Value ?? 1);

    private void ButtonCancel_OnClick(object? s, RoutedEventArgs e) => Close(false);
    private void ButtonCreate_OnClick(object? s, RoutedEventArgs e) => Close(true);
}
