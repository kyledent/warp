using Avalonia.Controls;
using Avalonia.Interactivity;
namespace MAvalonia.Dialogs;
public partial class DialogSpeciesRotate : Window
{
    public DialogSpeciesRotate() { InitializeComponent(); }
    private void ButtonCancel_OnClick(object? s, RoutedEventArgs e) => Close(null);
    private void ButtonApply_OnClick(object? s, RoutedEventArgs e) => Close(new float[] {
        float.Parse(TextRot.Text ?? "0"), float.Parse(TextTilt.Text ?? "0"), float.Parse(TextPsi.Text ?? "0") });
}
