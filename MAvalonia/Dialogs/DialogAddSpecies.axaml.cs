using Avalonia.Controls;
using Avalonia.Interactivity;
namespace MAvalonia.Dialogs;
public partial class DialogAddSpecies : Window
{
    public DialogAddSpecies() { InitializeComponent(); }
    private void ButtonCancel_OnClick(object? s, RoutedEventArgs e) => Close(null);
    private void ButtonNext_OnClick(object? s, RoutedEventArgs e) => Close(RadioFromScratch.IsChecked == true ? "scratch" : "local");
}
