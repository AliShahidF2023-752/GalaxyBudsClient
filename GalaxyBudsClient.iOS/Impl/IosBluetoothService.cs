using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBluetooth;
using CoreFoundation;
using Foundation;
using GalaxyBudsClient.Platform;
using GalaxyBudsClient.Platform.Interfaces;
using GalaxyBudsClient.Platform.Model;

namespace GalaxyBudsClient.iOS.Impl;

#pragma warning disable CA1416
#pragma warning disable CS0067

public class IosBluetoothService : IBluetoothService
{
    private readonly ConcurrentDictionary<string, DiscoveredDevice> _devices = new();

    private readonly CentralDelegate _centralDelegate;
    private readonly PeripheralDelegate _peripheralDelegate;
    private readonly CBCentralManager _central;

    private CBPeripheral? _activePeripheral;
    private CBCharacteristic? _writeCharacteristic;
    private CBCharacteristic? _notifyCharacteristic;
    private CBUUID? _targetServiceUuid;

    private TaskCompletionSource<bool>? _connectTcs;
    private TaskCompletionSource<bool>? _readyTcs;

    private volatile CBManagerState _currentState = CBManagerState.Unknown;

    public event EventHandler<BluetoothException>? BluetoothErrorAsync;
    public event EventHandler? Connecting;
    public event EventHandler? Connected;
    public event EventHandler? RfcommConnected;
    public event EventHandler<string>? Disconnected;
    public event EventHandler<byte[]>? NewDataAvailable;

    public bool IsStreamConnected { get; private set; }

    public IosBluetoothService()
    {
        _centralDelegate = new CentralDelegate(this);
        _peripheralDelegate = new PeripheralDelegate(this);
        _central = new CBCentralManager(_centralDelegate, DispatchQueue.MainQueue);
    }

    public async Task ConnectAsync(string macAddress, string serviceUuid, CancellationToken cancelToken)
    {
        await EnsureBluetoothReadyAsync(cancelToken);

        _targetServiceUuid = CBUUID.FromString(serviceUuid);
        _writeCharacteristic = null;
        _notifyCharacteristic = null;

        var peripheral = ResolvePeripheral(macAddress);
        if (peripheral == null)
        {
            throw new BluetoothException(BluetoothException.ErrorCodes.ConnectFailed,
                "The selected Bluetooth device is not available on iOS. Scan for devices first and select it again.");
        }

        _activePeripheral = peripheral;
        _activePeripheral.Delegate = _peripheralDelegate;

        Connecting?.Invoke(this, EventArgs.Empty);

        _connectTcs = NewSignal();
        _readyTcs = NewSignal();

        using var connectCancellation = cancelToken.Register(() =>
        {
            try
            {
                if (_activePeripheral != null)
                {
                    _central.CancelPeripheralConnection(_activePeripheral);
                }
            }
            catch
            {
                // ignored
            }
            _connectTcs?.TrySetCanceled(cancelToken);
            _readyTcs?.TrySetCanceled(cancelToken);
        });

        _central.ConnectPeripheral(_activePeripheral);
        await _connectTcs.Task;

        _activePeripheral.DiscoverServices([_targetServiceUuid]);
        await _readyTcs.Task;
    }

    public async Task DisconnectAsync()
    {
        if (_activePeripheral != null)
        {
            _central.CancelPeripheralConnection(_activePeripheral);
        }

        IsStreamConnected = false;
        await Task.CompletedTask;
    }

    public async Task SendAsync(byte[] data)
    {
        if (!IsStreamConnected || _activePeripheral == null || _writeCharacteristic == null)
        {
            return;
        }

        var type = _writeCharacteristic.Properties.HasFlag(CBCharacteristicProperties.Write)
            ? CBCharacteristicWriteType.WithResponse
            : CBCharacteristicWriteType.WithoutResponse;

        _activePeripheral.WriteValue(NSData.FromArray(data), _writeCharacteristic, type);
        await Task.CompletedTask;
    }

    public async Task<BluetoothDevice[]> GetDevicesAsync()
    {
        await EnsureBluetoothReadyAsync(CancellationToken.None);

        _devices.Clear();
        _central.ScanForPeripherals((CBUUID[]?)null);

        await Task.Delay(5000);

        _central.StopScan();

        return _devices.Values
            .Select(ToBluetoothDevice)
            .ToArray();
    }

    private static TaskCompletionSource<bool> NewSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private async Task EnsureBluetoothReadyAsync(CancellationToken cancelToken)
    {
        if (_currentState == CBManagerState.PoweredOn)
        {
            return;
        }

        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (_currentState is CBManagerState.Unknown or CBManagerState.Resetting)
        {
            cancelToken.ThrowIfCancellationRequested();
            if (DateTime.UtcNow >= deadline)
            {
                break;
            }

            await Task.Delay(200, cancelToken);
        }

        if (_currentState != CBManagerState.PoweredOn)
        {
            throw BuildStateException(_currentState);
        }
    }

    private CBPeripheral? ResolvePeripheral(string identifier)
    {
        if (_devices.TryGetValue(identifier, out var discovered))
        {
            return discovered.Peripheral;
        }

        if (Guid.TryParse(identifier, out var guid))
        {
            var nsId = new NSUuid(guid.ToString());
            return _central.RetrievePeripheralsWithIdentifiers([nsId]).FirstOrDefault();
        }

        return null;
    }

    private static BluetoothException BuildStateException(CBManagerState state)
    {
        return state switch
        {
            CBManagerState.Unauthorized => new BluetoothException(BluetoothException.ErrorCodes.NoAdaptersAvailable,
                "Bluetooth permission was denied. Enable Bluetooth access for this app in iOS Settings."),
            CBManagerState.Unsupported => new BluetoothException(BluetoothException.ErrorCodes.UnsupportedDevice,
                "This Apple device does not support CoreBluetooth."),
            CBManagerState.PoweredOff => new BluetoothException(BluetoothException.ErrorCodes.NoAdaptersAvailable,
                "Bluetooth is turned off. Enable Bluetooth in iOS Settings and try again."),
            _ => new BluetoothException(BluetoothException.ErrorCodes.Unknown,
                "Bluetooth is not ready on iOS. Please try again.")
        };
    }

    private BluetoothDevice ToBluetoothDevice(DiscoveredDevice discovered)
    {
        var name = string.IsNullOrWhiteSpace(discovered.Name) ? "Unknown Bluetooth Device" : discovered.Name;
        var isConnected = discovered.Peripheral.State == CBPeripheralState.Connected;
        return new BluetoothDevice(
            name,
            discovered.Identifier,
            isConnected,
            true,
            new BluetoothCoD((uint)BluetoothCoD.Major.AudioVideo, 0),
            discovered.ServiceUuids);
    }

    private void OnStateUpdated(CBManagerState state)
    {
        _currentState = state;
    }

    private void OnDiscovered(CBPeripheral peripheral, NSDictionary advertisementData)
    {
        var identifier = peripheral.Identifier.AsString();
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return;
        }

        var name = peripheral.Name ?? advertisementData[CBAdvertisement.DataLocalNameKey]?.ToString() ?? identifier;
        var serviceUuids = ParseServiceUuids(advertisementData);
        _devices[identifier] = new DiscoveredDevice(identifier, name, peripheral, serviceUuids);
    }

    private static Guid[]? ParseServiceUuids(NSDictionary advertisementData)
    {
        if (advertisementData[CBAdvertisement.DataServiceUUIDsKey] is not NSArray array)
        {
            return null;
        }

        var uuids = new List<Guid>();
        foreach (var item in array)
        {
            if (item is not CBUUID cbUuid)
            {
                continue;
            }

            if (Guid.TryParse(cbUuid.ToString(), out var guid))
            {
                uuids.Add(guid);
            }
        }

        return uuids.Count == 0 ? null : uuids.ToArray();
    }

    private void OnConnected(CBPeripheral peripheral)
    {
        _activePeripheral = peripheral;
        _activePeripheral.Delegate = _peripheralDelegate;
        _connectTcs?.TrySetResult(true);
        Connected?.Invoke(this, EventArgs.Empty);
    }

    private void OnFailedToConnect(NSError? error)
    {
        var ex = new BluetoothException(BluetoothException.ErrorCodes.ConnectFailed,
            error?.LocalizedDescription ?? "Connection failed on iOS.");
        BluetoothErrorAsync?.Invoke(this, ex);
        _connectTcs?.TrySetException(ex);
    }

    private void OnDisconnected(NSError? error)
    {
        IsStreamConnected = false;
        _writeCharacteristic = null;
        _notifyCharacteristic = null;
        _readyTcs?.TrySetCanceled();
        Disconnected?.Invoke(this, error?.LocalizedDescription ?? "Disconnected");
    }

    private void OnServicesDiscovered(CBPeripheral peripheral, NSError? error)
    {
        if (error != null)
        {
            var ex = new BluetoothException(BluetoothException.ErrorCodes.ConnectFailed, error.LocalizedDescription);
            BluetoothErrorAsync?.Invoke(this, ex);
            _readyTcs?.TrySetException(ex);
            return;
        }

        var targetService = peripheral.Services?.FirstOrDefault(s =>
            _targetServiceUuid == null || s.UUID.Equals(_targetServiceUuid));

        if (targetService == null)
        {
            var ex = new BluetoothException(BluetoothException.ErrorCodes.UnsupportedDevice,
                "No matching configuration service found on the selected device.");
            BluetoothErrorAsync?.Invoke(this, ex);
            _readyTcs?.TrySetException(ex);
            return;
        }

        peripheral.DiscoverCharacteristics((CBUUID[]?)null, targetService);
    }

    private void OnCharacteristicsDiscovered(CBPeripheral peripheral, CBService service, NSError? error)
    {
        if (error != null)
        {
            var ex = new BluetoothException(BluetoothException.ErrorCodes.ConnectFailed, error.LocalizedDescription);
            BluetoothErrorAsync?.Invoke(this, ex);
            _readyTcs?.TrySetException(ex);
            return;
        }

        var characteristics = service.Characteristics ?? [];
        _writeCharacteristic = characteristics.FirstOrDefault(c =>
            c.Properties.HasFlag(CBCharacteristicProperties.Write) ||
            c.Properties.HasFlag(CBCharacteristicProperties.WriteWithoutResponse));

        _notifyCharacteristic = characteristics.FirstOrDefault(c =>
            c.Properties.HasFlag(CBCharacteristicProperties.Notify) ||
            c.Properties.HasFlag(CBCharacteristicProperties.Indicate) ||
            c.Properties.HasFlag(CBCharacteristicProperties.Read));

        if (_writeCharacteristic == null || _notifyCharacteristic == null)
        {
            var ex = new BluetoothException(BluetoothException.ErrorCodes.UnsupportedDevice,
                "The selected device service does not expose required read/write characteristics.");
            BluetoothErrorAsync?.Invoke(this, ex);
            _readyTcs?.TrySetException(ex);
            return;
        }

        peripheral.SetNotifyValue(true, _notifyCharacteristic);
        IsStreamConnected = true;
        RfcommConnected?.Invoke(this, EventArgs.Empty);
        _readyTcs?.TrySetResult(true);
    }

    private void OnCharacteristicValueUpdated(CBCharacteristic characteristic, NSError? error)
    {
        if (error != null)
        {
            BluetoothErrorAsync?.Invoke(this, new BluetoothException(
                BluetoothException.ErrorCodes.ReceiveFailed,
                error.LocalizedDescription));
            return;
        }

        var payload = characteristic.Value?.ToArray();
        if (payload is { Length: > 0 })
        {
            NewDataAvailable?.Invoke(this, payload);
        }
    }

    private sealed record DiscoveredDevice(string Identifier, string Name, CBPeripheral Peripheral, Guid[]? ServiceUuids);

    private sealed class CentralDelegate(IosBluetoothService service) : CBCentralManagerDelegate
    {
        public override void UpdatedState(CBCentralManager central)
            => service.OnStateUpdated(central.State);

        public override void DiscoveredPeripheral(CBCentralManager central, CBPeripheral peripheral,
            NSDictionary advertisementData, NSNumber rssi)
            => service.OnDiscovered(peripheral, advertisementData);

        public override void ConnectedPeripheral(CBCentralManager central, CBPeripheral peripheral)
            => service.OnConnected(peripheral);

        public override void FailedToConnectPeripheral(CBCentralManager central, CBPeripheral peripheral, NSError error)
            => service.OnFailedToConnect(error);

        public override void DisconnectedPeripheral(CBCentralManager central, CBPeripheral peripheral, NSError error)
            => service.OnDisconnected(error);
    }

    private sealed class PeripheralDelegate(IosBluetoothService service) : CBPeripheralDelegate
    {
        public override void DiscoveredService(CBPeripheral peripheral, NSError error)
            => service.OnServicesDiscovered(peripheral, error);

        public override void DiscoveredCharacteristic(CBPeripheral peripheral, CBService serviceRef, NSError error)
            => service.OnCharacteristicsDiscovered(peripheral, serviceRef, error);

        public override void UpdatedCharacterteristicValue(CBPeripheral peripheral, CBCharacteristic characteristic,
            NSError error)
            => service.OnCharacteristicValueUpdated(characteristic, error);
    }
}
