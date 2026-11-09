using System.Runtime.InteropServices;
using OwlWidgets.Common.Bluetooth;
using OwlWidgets.Widgets.BluetoothAudio.Interop;

namespace OwlWidgets.Widgets.BluetoothAudio;

/// <summary>A paired Bluetooth audio device: the audio endpoints of one Bluetooth address.</summary>
/// <param name="Address">12 hex digits, upper case, without separators.</param>
/// <param name="IsConnected">At least one of its audio endpoints is active.</param>
public sealed record BluetoothAudioDevice(string Address, string Name, bool IsConnected);

/// <summary>
/// Paired Bluetooth audio devices through Core Audio, the way the Windows settings connect them:
/// the audio endpoint leads to the KS filter of the Bluetooth audio driver, and the filter takes
/// <c>KSPROPSETID_BtAudio</c> requests. The Bluetooth APIs of Windows cannot connect a classic device.
/// </summary>
/// <remarks>Blocking COM calls: call from a thread pool thread, not from the UI thread.</remarks>
public static class BluetoothAudioDevices
{
    public static IReadOnlyList<BluetoothAudioDevice> GetAll()
    {
        var endpoints = Enumerate(withControl: false);
        return endpoints
            .GroupBy(endpoint => endpoint.Address)
            .Select(group => new BluetoothAudioDevice(group.Key, NameOf(group), group.Any(endpoint => endpoint.Active)))
            .OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <returns>null: no audio endpoint of a paired device has this address.</returns>
    public static BluetoothAudioDevice? Find(string address)
    {
        address = BluetoothAddress.Normalize(address);
        var endpoints = Enumerate(withControl: false).Where(endpoint => endpoint.Address == address).ToList();
        return endpoints.Count == 0 ? null : new BluetoothAudioDevice(address, NameOf(endpoints), endpoints.Any(endpoint => endpoint.Active));
    }

    /// <summary>
    /// Sends the request to every KS filter of the device: A2DP and Hands-Free are separate filters.
    /// Success means only that the driver took the request: whether the device follows shows in its endpoints.
    /// </summary>
    /// <exception cref="InvalidOperationException">No filter of the device took the request.</exception>
    public static void RequestConnection(string address, bool connect)
    {
        address = BluetoothAddress.Normalize(address);
        var endpoints = Enumerate(withControl: true);
        try
        {
            var filters = endpoints
                .Where(endpoint => endpoint.Address == address && endpoint.Control != null)
                .GroupBy(endpoint => endpoint.FilterId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            if (filters.Count == 0)
                throw new InvalidOperationException($"No Bluetooth audio filter of {address} is found.");

            var accepted = 0;
            var lastError = 0;
            foreach (var filter in filters)
            {
                var property = new KSPROPERTY
                {
                    Set = CoreAudio.KSPROPSETID_BtAudio,
                    Id = connect ? CoreAudio.KSPROPERTY_ONESHOT_RECONNECT : CoreAudio.KSPROPERTY_ONESHOT_DISCONNECT,
                    Flags = CoreAudio.KSPROPERTY_TYPE_GET
                };
                var hr = filter.Control!.KsProperty(ref property, (uint)Marshal.SizeOf<KSPROPERTY>(), IntPtr.Zero, 0, out _);
                if (hr >= 0)
                    accepted++;
                else
                    lastError = hr;
            }

            if (accepted == 0)
                throw new InvalidOperationException(
                    $"No Bluetooth audio filter of {address} took the request.", Marshal.GetExceptionForHR(lastError));
        }
        finally
        {
            foreach (var endpoint in endpoints)
                CoreAudio.Release(endpoint.Control);
        }
    }

    /// <summary>The A2DP node is named after the device; the Hands-Free one adds "Hands-Free" to it.</summary>
    private static string NameOf(IEnumerable<Endpoint> endpoints)
    {
        var list = endpoints.ToList();
        return list.FirstOrDefault(endpoint => endpoint.IsA2dp && !string.IsNullOrEmpty(endpoint.Name))?.Name
            ?? list.FirstOrDefault(endpoint => !string.IsNullOrEmpty(endpoint.Name))?.Name
            ?? BluetoothAddress.Format(list[0].Address);
    }

    /// <summary>Every audio endpoint in any state whose KS filter belongs to a Bluetooth device.</summary>
    /// <param name="withControl">Also open <see cref="IKsControl"/> of each filter; the caller releases it.</param>
    private static List<Endpoint> Enumerate(bool withControl)
    {
        var result = new List<Endpoint>();
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        IMMDeviceCollection? collection = null;
        try
        {
            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(EDataFlow.eAll, CoreAudio.DEVICE_STATEMASK_ALL, out collection));
            Marshal.ThrowExceptionForHR(collection.GetCount(out var count));
            for (uint i = 0; i < count; i++)
            {
                if (collection.Item(i, out var device) < 0)
                    continue;
                try
                {
                    var endpoint = Read(enumerator, device, withControl);
                    if (endpoint != null)
                        result.Add(endpoint);
                }
                finally
                {
                    CoreAudio.Release(device);
                }
            }
        }
        finally
        {
            CoreAudio.Release(collection);
            CoreAudio.Release(enumerator);
        }

        return result;
    }

    private static Endpoint? Read(IMMDeviceEnumerator enumerator, IMMDevice device, bool withControl)
    {
        var iid = CoreAudio.IID_IDeviceTopology;
        if (device.Activate(ref iid, CoreAudio.CLSCTX_ALL, IntPtr.Zero, out var topologyObject) < 0)
            return null;
        IConnector? connector = null;
        try
        {
            // The KS filter of the driver is connected directly to the only connector of the endpoint.
            var topology = (IDeviceTopology)topologyObject;
            if (topology.GetConnector(0, out connector) < 0 || connector.GetDeviceIdConnectedTo(out var filterId) < 0)
                return null;
            var node = KsFilterNode.FindBluetooth(filterId);
            if (node == null)
                return null;

            device.GetState(out var state);
            var control = withControl ? OpenControl(enumerator, filterId) : null;
            return new Endpoint(
                node.Value.Address,
                node.Value.Name,
                node.Value.InstanceId.StartsWith(@"BTHENUM\", StringComparison.OrdinalIgnoreCase),
                filterId,
                state == CoreAudio.DEVICE_STATE_ACTIVE,
                control);
        }
        finally
        {
            CoreAudio.Release(connector);
            CoreAudio.Release(topologyObject);
        }
    }

    /// <summary>The filter is a Core Audio device of its own; null: it has no endpoint now (not present).</summary>
    private static IKsControl? OpenControl(IMMDeviceEnumerator enumerator, string filterId)
    {
        if (enumerator.GetDevice(filterId, out var filter) < 0)
            return null;
        try
        {
            var iid = CoreAudio.IID_IKsControl;
            return filter.Activate(ref iid, CoreAudio.CLSCTX_ALL, IntPtr.Zero, out var control) < 0 ? null : (IKsControl)control;
        }
        finally
        {
            CoreAudio.Release(filter);
        }
    }

    private sealed record Endpoint(string Address, string? Name, bool IsA2dp, string FilterId, bool Active, IKsControl? Control);
}
