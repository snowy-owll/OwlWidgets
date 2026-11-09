using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace OwlWidgets.Widgets.SystemMonitor;

/// <summary>What a drive is, for the switches of the settings.</summary>
public enum DriveKind
{
    /// <summary>A disk inside the computer; each can be turned off on its own (<see cref="Config.HiddenDrives"/>).</summary>
    Local,

    /// <summary>On USB, FireWire or a memory card, and optical discs.</summary>
    External,

    /// <summary>A network share mapped to a letter.</summary>
    Network
}

/// <summary>
/// Tells the kind of a drive without reading it: the type of the letter comes from Windows, and the bus of
/// a fixed disk from its storage descriptor, which the disk driver keeps; the media and a network server are not touched.
/// </summary>
public static class DriveKinds
{
    /// <returns>Null: no drive behind the letter.</returns>
    public static DriveKind? Of(DriveInfo drive) => drive.DriveType switch
    {
        DriveType.Network => DriveKind.Network,
        DriveType.Removable or DriveType.CDRom => DriveKind.External,
        // A USB hard disk or stick with a fixed disk inside reports itself as fixed: only its bus tells it.
        DriveType.Fixed => IsExternalBus(drive.Name) ? DriveKind.External : DriveKind.Local,
        DriveType.Ram => DriveKind.Local,
        _ => null
    };

    /// <summary>
    /// Whether the card reads the drive. Only the type of the letter and the bus are asked here,
    /// so a drive turned off is never read: its size and free space are asked only after this.
    /// </summary>
    public static bool IsShown(DriveInfo drive, Config config) => Of(drive) switch
    {
        DriveKind.Local => !IsHidden(drive, config),
        DriveKind.External => config.ShowExternalDrives && !IsHidden(drive, config),
        DriveKind.Network => config.ShowNetworkDrives && !IsHidden(drive, config),
        _ => false
    };

    /// <summary>
    /// What a drive turned off is kept by: the volume GUID path (<c>\\?\Volume{…}\</c>), the same for a USB disk
    /// under any letter, from the mount manager without reading the disk; a network drive has no volume, its root.
    /// </summary>
    public static string Id(DriveInfo drive)
    {
        if (drive.DriveType != DriveType.Network)
        {
            var buffer = new char[64];
            if (GetVolumeNameForVolumeMountPointW(drive.Name, buffer, buffer.Length))
                return new string(buffer).TrimEnd('\0');
        }

        return drive.Name;
    }

    private static bool IsHidden(DriveInfo drive, Config config)
    {
        var id = Id(drive);
        return config.HiddenDrives.Any(hidden => string.Equals(hidden.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsExternalBus(string root)
    {
        // Access 0: the descriptor is a query of the device, not a read of the volume, and needs no rights.
        using var volume = CreateFileW(@"\\.\" + root.TrimEnd('\\'), 0, FileShareReadWrite, 0, OpenExisting, 0, 0);
        if (volume.IsInvalid)
            return false;

        var query = new byte[12]; // STORAGE_PROPERTY_QUERY: StorageDeviceProperty, PropertyStandardQuery
        var descriptor = new byte[1024];
        if (!DeviceIoControl(volume, IoctlStorageQueryProperty, query, query.Length, descriptor, descriptor.Length, out var returned, 0)
            || returned < BusTypeOffset + 4)
            return false;

        var bus = BitConverter.ToUInt32(descriptor, BusTypeOffset);
        return bus is BusType1394 or BusTypeUsb or BusTypeSd or BusTypeMmc;
    }

    private const uint IoctlStorageQueryProperty = 0x002D1400;
    private const uint FileShareReadWrite = 0x1 | 0x2;
    private const uint OpenExisting = 3;
    /// <summary>STORAGE_DEVICE_DESCRIPTOR.BusType, after two sizes, four bytes of flags and four offsets.</summary>
    private const int BusTypeOffset = 28;
    private const uint BusType1394 = 0x4;
    private const uint BusTypeUsb = 0x7;
    private const uint BusTypeSd = 0xC;
    private const uint BusTypeMmc = 0xD;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPointW(string mountPoint, [Out] char[] volumeName, int length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] input, int inputSize, byte[] output, int outputSize,
        out int returned, nint overlapped);
}
