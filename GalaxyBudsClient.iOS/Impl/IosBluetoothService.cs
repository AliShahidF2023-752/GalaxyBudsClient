using System;
using System.Threading;
using System.Threading.Tasks;
using GalaxyBudsClient.Platform;
using GalaxyBudsClient.Platform.Interfaces;
using GalaxyBudsClient.Platform.Model;

namespace GalaxyBudsClient.iOS.Impl;

#pragma warning disable CS0067

public class IosBluetoothService : IBluetoothService
{
    public event EventHandler<BluetoothException>? BluetoothErrorAsync;
    public event EventHandler? Connecting;
    public event EventHandler? Connected;
    public event EventHandler? RfcommConnected;
    public event EventHandler<string>? Disconnected;
    public event EventHandler<byte[]>? NewDataAvailable;

    public bool IsStreamConnected => false;

    private static BluetoothException BuildUnsupportedException()
    {
        return new BluetoothException(
            BluetoothException.ErrorCodes.Unknown,
            "Galaxy Buds require Bluetooth Classic RFCOMM/SPP for protocol traffic. " +
            "iOS app sandbox APIs still restrict direct RFCOMM/SPP access to MFi External Accessory integrations, " +
            "so non-MFi Galaxy Buds cannot be connected from this iOS build.");
    }

    public async Task ConnectAsync(string macAddress, string serviceUuid, CancellationToken cancelToken)
    {
        Connecting?.Invoke(this, EventArgs.Empty);
        BluetoothErrorAsync?.Invoke(this, BuildUnsupportedException());
        await Task.CompletedTask;
    }

    public async Task DisconnectAsync()
    {
        Disconnected?.Invoke(this, "No Bluetooth session is available on iOS for RFCOMM/SPP in this build.");
        await Task.CompletedTask;
    }

    public async Task SendAsync(byte[] data)
    {
        await Task.CompletedTask;
    }

    public async Task<BluetoothDevice[]> GetDevicesAsync()
    {
        await Task.CompletedTask;
        throw BuildUnsupportedException();
    }
}
