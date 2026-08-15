using GalaxyBudsClient.Platform.Interfaces;

namespace GalaxyBudsClient.iOS.Impl;

public class IosPlatformImplCreator : IPlatformImplCreator
{
    public IDesktopServices CreateDesktopServices() => new DesktopServices();
    public IBluetoothService? CreateBluetoothService() => null;
    public IHotkeyBroadcast? CreateHotkeyBroadcast() => null;
    public IHotkeyReceiver? CreateHotkeyReceiver() => null;
    public IMediaKeyRemote? CreateMediaKeyRemote() => null;
    public INotificationListener? CreateNotificationListener() => null;
    public IOfficialAppDetector? CreateOfficialAppDetector() => null;
}
