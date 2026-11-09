namespace OwlWidgets.Common.Bluetooth;

/// <summary>Bluetooth address as settings keep it: 12 hex digits, upper case, without separators.</summary>
public static class BluetoothAddress
{
    /// <summary>Upper case hex digits without separators; an empty string for null.</summary>
    public static string Normalize(string? address) =>
        new((address ?? string.Empty).Where(Uri.IsHexDigit).Select(char.ToUpperInvariant).ToArray());

    /// <summary>"E8:EE:CC:7E:AF:84" for "E8EECC7EAF84"; other strings as they are.</summary>
    public static string Format(string address)
    {
        var digits = Normalize(address);
        return digits.Length == 12 ? string.Join(':', digits.Chunk(2).Select(pair => new string(pair))) : address;
    }
}
