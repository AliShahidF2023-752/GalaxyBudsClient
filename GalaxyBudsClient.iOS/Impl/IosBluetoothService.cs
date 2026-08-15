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
            BluetoothException.ErrorCodes.UnsupportedDevice,
            "The iOS Bluetooth backend in this app is not implemented for Galaxy Buds protocol transport yet. " +
            "CoreBluetooth BR/EDR+GATT support and transport bridging require accessory/profile-specific integration " +
            "that is not mapped to this raw message backend in the current iOS build.");
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
