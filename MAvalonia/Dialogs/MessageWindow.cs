using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace MAvalonia.Dialogs;

/// <summary>
/// Stands in for MahApps' ShowMessageAsync, which WPF M uses for notices and Yes/No questions.
/// </summary>
public static class MessageWindow
{
    public static Task ShowAsync(Window owner, string title, string message) =>
        Show(owner, title, message, yesNo: false);

    /// <returns>true if the user chose Yes.</returns>
    public static Task<bool> AskAsync(Window owner, string title, string message) =>
        Show(owner, title, message, yesNo: true);

    private static async Task<bool> Show(Window owner, string title, string message, bool yesNo)
    {
        bool result = false;
        var window = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        if (yesNo)
        {
            var yes = new Button { Content = "Yes", MinWidth = 80, IsDefault = true };
            var no = new Button { Content = "No", MinWidth = 80, IsCancel = true };
            yes.Click += (s, e) => { result = true; window.Close(); };
            no.Click += (s, e) => window.Close();
            buttons.Children.Add(yes);
            buttons.Children.Add(no);
        }
        else
        {
            var ok = new Button { Content = "OK", MinWidth = 80, IsDefault = true, IsCancel = true };
            ok.Click += (s, e) => window.Close();
            buttons.Children.Add(ok);
        }

        window.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = title, FontSize = 20, TextWrapping = TextWrapping.Wrap, MaxWidth = 560 },
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 560 },
                buttons
            }
        };

        await window.ShowDialog(owner);
        return result;
    }
}
