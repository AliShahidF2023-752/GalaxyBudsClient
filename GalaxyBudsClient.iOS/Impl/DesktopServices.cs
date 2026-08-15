using Foundation;
using GalaxyBudsClient.Platform;
using UIKit;

namespace GalaxyBudsClient.iOS.Impl;

public class DesktopServices : BaseDesktopServices
{
    public override bool IsAutoStartEnabled { get; set; } = false;

    public override void OpenUri(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return;

        var nsUrl = new NSUrl(uri);
        if (UIApplication.SharedApplication.CanOpenUrl(nsUrl))
            UIApplication.SharedApplication.OpenUrl(nsUrl);
    }
}
