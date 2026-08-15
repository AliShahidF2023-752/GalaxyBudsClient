using Avalonia;
using Avalonia.iOS;
using GalaxyBudsClient.iOS.Impl;
using GalaxyBudsClient.Platform;
using ReactiveUI.Avalonia;
using UIKit;

namespace GalaxyBudsClient.iOS;

public static class Program
{
    static void Main(string[] args)
    {
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}

public class AppDelegate : AvaloniaAppDelegate<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        GalaxyBudsClient.Program.Startup(false);
        PlatformImpl.InjectExternalBackend(new IosPlatformImplCreator());

        return base.CustomizeAppBuilder(builder)
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI();
    }
}
