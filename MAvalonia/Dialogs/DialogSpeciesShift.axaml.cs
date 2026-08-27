using Avalonia.Controls;
using Avalonia.Interactivity;
namespace MAvalonia.Dialogs;
public partial class DialogSpeciesShift : Window
{
    public DialogSpeciesShift() { InitializeComponent(); }
    private void ButtonCancel_OnClick(object? s, RoutedEventArgs e) => Close(null);
    private void ButtonApply_OnClick(object? s, RoutedEventArgs e) => Close(new float[] {
        float.Parse(TextX.Text ?? "0"), float.Parse(TextY.Text ?? "0"), float.Parse(TextZ.Text ?? "0") });
}
