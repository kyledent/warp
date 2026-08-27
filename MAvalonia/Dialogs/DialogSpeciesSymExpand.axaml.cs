using Avalonia.Controls;
using Avalonia.Interactivity;
namespace MAvalonia.Dialogs;
public partial class DialogSpeciesSymExpand : Window
{
    public DialogSpeciesSymExpand(string currentSymmetry = "C1") { InitializeComponent(); TextCurrentSym.Text = currentSymmetry; }
    private void ButtonCancel_OnClick(object? s, RoutedEventArgs e) => Close(null);
    private void ButtonApply_OnClick(object? s, RoutedEventArgs e) => Close(TextNewSym.Text);
}
