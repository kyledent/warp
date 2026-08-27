using Avalonia;
using System;
using System.Linq;

namespace WarpAvalonia;

class Program
{
    public static bool DemoMode { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        DemoMode = args.Contains("--demo");

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
