using System.Windows.Media;
using OwlWidgets.Common;
using OwlWidgets.Common.Bluetooth;
using OwlWidgets.Contracts;
using OwlWidgets.Widgets.DeviceBatteryInfo.Properties;

namespace OwlWidgets.Widgets.DeviceBatteryInfo.ViewModels;

/// <summary>A device on the card. Updated in place on the UI thread.</summary>
internal sealed class DeviceViewModel : ViewModelBase
{
    private static readonly Brush Low = Frozen(Colors.Red);
    private static readonly Brush Medium = Frozen(Colors.Yellow);
    private static readonly Brush High = Frozen(Colors.Green);
    private static readonly Brush Unknown = Frozen(Colors.Gray);

    private readonly string? _savedName;

    /// <param name="dataFolder">Files of the instance, where the user icons are.</param>
    public DeviceViewModel(Config.Device device, string dataFolder, IWidgetLogger? logger)
    {
        Address = BluetoothAddress.Normalize(device.Address);
        _savedName = string.IsNullOrWhiteSpace(device.Name) ? null : device.Name;
        Name = _savedName ?? BluetoothAddress.Format(Address);
        Icon = LoadIcon(device, dataFolder, logger);
        Color = Unknown;
    }

    public string Address { get; }

    public ImageSource? Icon { get; }

    public bool HasIcon => Icon != null;

    public string? Name
    {
        get => GetValue(() => Name);
        set => SetValue(() => Name, value);
    }

    /// <summary>The level, or why there is none.</summary>
    public string? Status
    {
        get => GetValue(() => Status);
        set => SetValue(() => Status, value);
    }

    public Brush? Color
    {
        get => GetValue(() => Color);
        set => SetValue(() => Color, value);
    }

    /// <summary>
    /// A disconnected device keeps the last level Windows got from it: it is shown, marked, with the grey dot.
    /// </summary>
    /// <param name="device">null: no paired device has this address.</param>
    public void Show(BluetoothDevice? device)
    {
        if (device == null)
        {
            Status = Strings.State_NotFound;
            Color = Unknown;
            return;
        }

        Name = device.Name;
        var level = device.BatteryLevel;
        if (device.IsConnected == false)
        {
            Status = level == null ? Strings.State_Disconnected : $"{Strings.State_Disconnected} · {level}%";
            Color = Unknown;
            return;
        }

        Status = level == null ? null : $"{level}%";
        Color = level switch
        {
            < 25 => Low,
            < 75 => Medium,
            <= 100 => High,
            _ => Unknown
        };
    }

    /// <returns>null: the Bluetooth sign.</returns>
    private static ImageSource? LoadIcon(Config.Device device, string dataFolder, IWidgetLogger? logger)
    {
        try
        {
            return DeviceIcons.Load(device.Icon, dataFolder);
        }
        catch (Exception exception)
        {
            logger?.Log(WidgetLogLevel.Warning, $"Icon {device.Icon} of {device.Address} was not read, the Bluetooth sign is shown.", exception);
            return null;
        }
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
