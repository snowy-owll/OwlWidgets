using System.Text.Json.Serialization;

namespace OwlWidgets.Widgets.DeviceBatteryInfo;

public class Config
{
    public const int DefaultUpdateInterval = 10000;

    public Config()
    {
        UpdateInterval = DefaultUpdateInterval;
    }

    /// <summary>Devices on the card, in its order.</summary>
    public List<Device> Devices { get; set; } = [];

    public int UpdateInterval { get; set; }

    /// <summary>
    /// Files of the icons the user added, in the folder <see cref="DeviceIcons.FolderName"/> of the instance files,
    /// in the order of the list. A file not named here is deleted by the settings screen.
    /// </summary>
    public List<string> UserIcons { get; set; } = [];

    public class Device
    {
        /// <summary>
        /// Bluetooth address of the device, 12 hex digits without separators, as in
        /// <c>DEVPKEY_Bluetooth_DeviceAddress</c>.
        /// </summary>
        public string Address { get; set; } = string.Empty;

        /// <summary>Name of the device when it was added: shown while the device is not found.</summary>
        public string? Name { get; set; }

        /// <summary>
        /// A key of <see cref="DeviceIcons.BuiltIn"/> or <c>user:{file}</c> of <see cref="UserIcons"/>;
        /// null: the Bluetooth glyph.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Icon { get; set; }
    }
}
