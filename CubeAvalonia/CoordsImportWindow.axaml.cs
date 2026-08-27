using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CubeAvalonia;

public partial class CoordsImportWindow : Window
{
    public CoordsImportWindow()
    {
        InitializeComponent();
    }

    private void ButtonImport_OnClick(object? sender, RoutedEventArgs e) => Close(true);
    private void ButtonCancel_OnClick(object? sender, RoutedEventArgs e) => Close(false);
}
