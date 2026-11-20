using Avalonia;
using System;
using System.Linq;

namespace WarpAvalonia;

class Program
{
    public static bool DemoMode { get; private set; }
    public static string? LoadSettingsPath { get; private set; }
    public static string? SaveSettingsAndExitPath { get; private set; }
    public static string? OpenDialogName { get; private set; }
    public static string? DialogFilePath { get; private set; }
    public static string? DumpControlsAndExitPath { get; private set; }

    static string? ArgValue(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    [STAThread]
    public static void Main(string[] args)
    {
        DemoMode = args.Contains("--demo");
        LoadSettingsPath = ArgValue(args, "--load-settings");
        SaveSettingsAndExitPath = ArgValue(args, "--save-settings-and-exit");
        OpenDialogName = ArgValue(args, "--open-dialog");
        DialogFilePath = ArgValue(args, "--dialog-file");
        DumpControlsAndExitPath = ArgValue(args, "--dump-controls-and-exit");

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
