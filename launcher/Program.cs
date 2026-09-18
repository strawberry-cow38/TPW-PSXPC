using Avalonia;

// Avalonia entry point. Kept to the minimum: everything interesting is in MainWindow, and everything
// DECIDABLE is in core/TPW.Launcher where a test can reach it.
static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
