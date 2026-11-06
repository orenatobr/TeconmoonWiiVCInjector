using Avalonia;

namespace WiiVCInjector;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (Environment.GetEnvironmentVariable("WIIVC_SELFTEST") == "1")
            Environment.Exit(SelfTest.Run());
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect();
}
