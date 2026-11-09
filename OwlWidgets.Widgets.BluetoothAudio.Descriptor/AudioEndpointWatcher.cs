using System.Runtime.InteropServices;
using OwlWidgets.Widgets.BluetoothAudio.Interop;

namespace OwlWidgets.Widgets.BluetoothAudio;

/// <summary>
/// Raises <see cref="Changed"/> when an audio endpoint appears, disappears or changes its state:
/// a Bluetooth device connecting or disconnecting does all three.
/// </summary>
/// <remarks>Create and dispose from a thread pool thread: the registration is a COM call into the audio service.</remarks>
public sealed class AudioEndpointWatcher : IDisposable
{
    private readonly IMMDeviceEnumerator _enumerator;
    private readonly NotificationClient _client;
    private bool _disposed;

    public AudioEndpointWatcher()
    {
        _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        _client = new NotificationClient(this);
        var hr = _enumerator.RegisterEndpointNotificationCallback(_client);
        if (hr < 0)
        {
            CoreAudio.Release(_enumerator);
            Marshal.ThrowExceptionForHR(hr);
        }
    }

    /// <summary>Raised on a Core Audio thread; a handler must return quickly.</summary>
    public event EventHandler? Changed;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _enumerator.UnregisterEndpointNotificationCallback(_client);
        CoreAudio.Release(_enumerator);
    }

    private void Raise()
    {
        if (!_disposed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class NotificationClient(AudioEndpointWatcher owner) : IMMNotificationClient
    {
        public int OnDeviceStateChanged(string deviceId, uint newState)
        {
            owner.Raise();
            return 0;
        }

        public int OnDeviceAdded(string deviceId)
        {
            owner.Raise();
            return 0;
        }

        public int OnDeviceRemoved(string deviceId)
        {
            owner.Raise();
            return 0;
        }

        public int OnDefaultDeviceChanged(EDataFlow flow, int role, string? defaultDeviceId) => 0;

        public int OnPropertyValueChanged(string deviceId, PROPERTYKEY key) => 0;
    }
}
