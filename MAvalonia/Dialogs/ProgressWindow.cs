using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace MAvalonia.Dialogs;

/// <summary>
/// Stands in for the MahApps progress dialog WPF M shows during refinement: a title, a
/// multi-line message, and a progress bar that starts indeterminate. It cannot be closed by
/// the user while work runs; ShowError turns it into an error report with a Close button.
/// Call its methods on the UI thread.
/// </summary>
public class ProgressWindow : Window
{
    private readonly TextBlock TextTitle;
    private readonly TextBlock TextMessage;
    private readonly ProgressBar Bar;
    private readonly Button ButtonClose;
    private bool AllowClose;

    public ProgressWindow(string title, string message)
    {
        Title = "M";
        Width = 720;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        TextTitle = new TextBlock { Text = title, FontSize = 22, TextWrapping = TextWrapping.Wrap };
        TextMessage = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("monospace"), FontSize = 12 };
        Bar = new ProgressBar { IsIndeterminate = true, Height = 8, Minimum = 0, Maximum = 1 };
        ButtonClose = new Button { Content = "CLOSE", IsVisible = false, HorizontalAlignment = HorizontalAlignment.Right };
        ButtonClose.Click += (s, e) => { AllowClose = true; Close(); };

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children = { TextTitle, Bar, TextMessage, ButtonClose }
        };

        Closing += (s, e) => { if (!AllowClose) e.Cancel = true; };
    }

    public void SetTitle(string title) => TextTitle.Text = title;

    public void SetMessage(string message) => TextMessage.Text = message;

    public void SetIndeterminate() => Bar.IsIndeterminate = true;

    public void SetProgress(int done, int total)
    {
        Bar.IsIndeterminate = false;
        Bar.Maximum = System.Math.Max(1, total);
        Bar.Value = done;
    }

    /// <summary>Close once the work has finished.</summary>
    public void Finish()
    {
        AllowClose = true;
        Close();
    }

    /// <summary>Keep the window open with the error, and let the user close it.</summary>
    public void ShowError(string title, string details)
    {
        TextTitle.Text = title;
        TextMessage.Text = details;
        Bar.IsVisible = false;
        ButtonClose.IsVisible = true;
    }
}
