using Avalonia.Controls;
using Avalonia.Interactivity;
namespace MAvalonia.Dialogs;
public partial class DialogRefinementSettings : Window
{
    public DialogRefinementSettings() { InitializeComponent(); }
    private void ButtonCancel_OnClick(object? s, RoutedEventArgs e) => Close(null);
    private void ButtonRefine_OnClick(object? s, RoutedEventArgs e) => Close(true);
}
