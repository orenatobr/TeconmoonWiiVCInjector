using Avalonia;

namespace WiiVCInjector;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // UI self-test: never touch the real settings.json
        if (Environment.GetEnvironmentVariable("WIIVC_UITEST") == "1")
            Environment.SetEnvironmentVariable("WIIVC_SETTINGS", Path.Combine(Path.GetTempPath(), $"wiivc-uitest-{Environment.ProcessId}.json"));
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect();
}
