namespace OwlWidgets.Widgets.BluetoothAudio;

public class Config
{
    /// <summary>
    /// Bluetooth address of the device, 12 hex digits without separators, as in
    /// <c>DEVPKEY_Bluetooth_DeviceAddress</c>; null: no device chosen.
    /// </summary>
    public string? Address { get; set; }

    /// <summary>Caption on the card; null or empty: the device name.</summary>
    public string? Title { get; set; }
}
