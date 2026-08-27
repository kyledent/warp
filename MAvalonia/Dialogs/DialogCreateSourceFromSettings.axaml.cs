using Avalonia.Controls;
using Avalonia.Interactivity;
namespace MAvalonia.Dialogs;
public partial class DialogCreateSourceFromSettings : Window
{
    public DialogCreateSourceFromSettings() { InitializeComponent(); }
    private void ButtonCancel_OnClick(object? s, RoutedEventArgs e) => Close(null);
    private void ButtonCreate_OnClick(object? s, RoutedEventArgs e) => Close(TextSourceName.Text);
}
