using Avalonia;

namespace OppoPodsManager;

static class Program
{
    // Avalonia configuration, don't remove; used by Previewer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    /// <summary>Avalonia desktop entry point (AOT-compatible).</summary>
    public static void Main(string[] args)
        => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
}
