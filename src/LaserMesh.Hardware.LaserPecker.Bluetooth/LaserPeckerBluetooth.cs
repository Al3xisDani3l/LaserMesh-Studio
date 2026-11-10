// SPDX-License-Identifier: GPL-3.0-or-later
// GATT profiles and transport behavior adapted from PyroGraph transport.py.
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Channels;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace LaserMesh.Hardware.LaserPecker.Bluetooth;

/// <summary>Discovers and queries LaserPecker over the Windows Bluetooth LE adapter.</summary>
public sealed class LaserPeckerBluetooth
{
    private static readonly (Guid Service, Guid Write, Guid Notify)[] Profiles =
    [
        (new("49535343-fe7d-4ae5-8fa9-9fafd205e455"), new("49535343-8841-43f4-a8d4-ecbe34729bb3"),
            new("49535343-1e4d-4bd9-ba61-23c647249616")),
        (new("0000fff0-0000-1000-8000-00805f9b34fb"), new("0000fff2-0000-1000-8000-00805f9b34fb"),
            new("0000fff1-0000-1000-8000-00805f9b34fb")),
        (new("0000abf0-0000-1000-8000-00805f9b34fb"), new("0000abf3-0000-1000-8000-00805f9b34fb"),
            new("0000abf4-0000-1000-8000-00805f9b34fb"))
    ];

    /// <summary>Finds an exact advertised name, reads status and firmware, then closes the session.</summary>
    public async Task<(WorkingStatus Status, ushort Firmware)> ProbeAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var phase = "detectar el adaptador";
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(35));
        var token = deadline.Token;

        try
        {
            var adapter = await BluetoothAdapter.GetDefaultAsync().AsTask(token).ConfigureAwait(false);
            if (adapter is null || !adapter.IsLowEnergySupported)
                throw new IOException("Windows no encontró un adaptador Bluetooth LE.");

            phase = $"encontrar el anuncio de {name}";
            var advertisement = await FindAsync(name, token).ConfigureAwait(false);
            phase = "abrir el dispositivo y descubrir GATT";
            using var device = await BluetoothLEDevice.FromBluetoothAddressAsync(advertisement.BluetoothAddress,
                advertisement.BluetoothAddressType).AsTask(token).ConfigureAwait(false)
                ?? throw new IOException("Windows no pudo abrir el dispositivo BLE.");

            var services = await device.GetGattServicesAsync(BluetoothCacheMode.Uncached).AsTask(token).ConfigureAwait(false);
            try
            {
                EnsureSuccess(services.Status, "descubrir servicios");
                foreach (var profile in Profiles)
                {
                    var service = services.Services.FirstOrDefault(x => x.Uuid == profile.Service);
                    if (service is null)
                        continue;
                    var tx = await GetCharacteristicAsync(service, profile.Write, token).ConfigureAwait(false);
                    var rx = await GetCharacteristicAsync(service, profile.Notify, token).ConfigureAwait(false);
                    if (tx is null || rx is null
                        || !tx.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse)
                        || !rx.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
                        continue;

                    phase = "consultar estado y versión por GATT";
                    return await ReadDeviceAsync(device, service, tx, rx, token).ConfigureAwait(false);
                }
                throw new IOException("El dispositivo no expone un perfil GATT de LaserPecker compatible.");
            }
            finally
            {
                foreach (var service in services.Services)
                    service.Dispose();
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Se agotó el tiempo para {phase}.");
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            throw new IOException($"Windows Bluetooth falló al {phase}: {ex.Message}", ex);
        }
    }

    private static async Task<BluetoothLEAdvertisementReceivedEventArgs> FindAsync(string name, CancellationToken token)
    {
        using var scan = CancellationTokenSource.CreateLinkedTokenSource(token);
        scan.CancelAfter(TimeSpan.FromSeconds(15));
        var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
        var found = new TaskCompletionSource<BluetoothLEAdvertisementReceivedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Received(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            if (string.Equals(args.Advertisement.LocalName, name, StringComparison.OrdinalIgnoreCase))
                found.TrySetResult(args);
        }

        void Stopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
        {
            if (args.Error != BluetoothError.Success)
                found.TrySetException(new IOException($"El escaneo Bluetooth se detuvo: {args.Error}."));
        }

        watcher.Received += Received;
        watcher.Stopped += Stopped;
        try
        {
            watcher.Start();
            return await found.Task.WaitAsync(scan.Token).ConfigureAwait(false);
        }
        finally
        {
            watcher.Received -= Received;
            watcher.Stopped -= Stopped;
            watcher.Stop();
        }
    }

    private static async Task<GattCharacteristic?> GetCharacteristicAsync(GattDeviceService service, Guid id, CancellationToken token)
    {
        var result = await service.GetCharacteristicsForUuidAsync(id, BluetoothCacheMode.Cached)
            .AsTask(token).ConfigureAwait(false);
        EnsureSuccess(result.Status, "consultar características");
        return result.Characteristics.FirstOrDefault();
    }

    private static async Task<(WorkingStatus, ushort)> ReadDeviceAsync(BluetoothLEDevice device, GattDeviceService service,
        GattCharacteristic tx, GattCharacteristic rx, CancellationToken token)
    {
        var channel = Channel.CreateBounded<LaserPeckerFrame>(new BoundedChannelOptions(32)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });
        var assembler = new FrameAssembler();
        void Received(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            lock (assembler)
            {
                foreach (var frame in assembler.Feed(args.CharacteristicValue.ToArray()))
                    channel.Writer.TryWrite(frame);
            }
        }

        void ConnectionChanged(BluetoothLEDevice sender, object args)
        {
            if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
                channel.Writer.TryComplete(new IOException("Se perdió la conexión con LaserPecker."));
        }

        rx.ValueChanged += Received;
        device.ConnectionStatusChanged += ConnectionChanged;
        try
        {
            var subscription = await rx.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask(token).ConfigureAwait(false);
            EnsureSuccess(subscription, "suscribir notificaciones");

            var status = await QueryAsync(0, frame => frame.Payload.Length >= 21).ConfigureAwait(false);
            var version = await QueryAsync(3, frame => frame.Payload.Length is >= 6 and < 21).ConfigureAwait(false);
            return (LaserPeckerReplies.Status(status), LaserPeckerReplies.Version(version).Software);
        }
        finally
        {
            rx.ValueChanged -= Received;
            device.ConnectionStatusChanged -= ConnectionChanged;
            channel.Writer.TryComplete();
        }

        async Task<LaserPeckerFrame> QueryAsync(byte query, Func<LaserPeckerFrame, bool> matches)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var requestToken = timeout.Token;
            lock (assembler)
            {
                assembler.Reset();
                while (channel.Reader.TryRead(out _)) { }
            }

            var request = LaserPeckerProtocol.Query(query);
            // Queries are 11 bytes, below even the default ATT payload of 20.
            var mtu = service.Session.MaxPduSize;
            if (mtu > 3 && request.Length > mtu - 3)
                throw new IOException("La MTU Bluetooth no permite enviar la consulta LP2.");

            try
            {
                var write = await tx.WriteValueWithResultAsync(request.AsBuffer(), GattWriteOption.WriteWithoutResponse)
                    .AsTask(requestToken).ConfigureAwait(false);
                EnsureSuccess(write.Status, "enviar consulta");
                while (true)
                {
                    var frame = await channel.Reader.ReadAsync(requestToken).ConfigureAwait(false);
                    if (frame.Function == 0 && matches(frame))
                        return frame;
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new TimeoutException($"LaserPecker no respondió a la consulta {query}.");
            }
            catch (ChannelClosedException ex)
            {
                throw new IOException("Se perdió el canal de notificaciones Bluetooth.", ex);
            }
        }
    }

    private static void EnsureSuccess(GattCommunicationStatus status, string action)
    {
        if (status != GattCommunicationStatus.Success)
            throw new IOException($"No se pudo {action} por BLE: {status}.");
    }
}
