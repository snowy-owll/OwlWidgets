using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace OwlWidgets.Widgets.SystemMonitor;

/// <summary>
/// The web server of LibreHardwareMonitor (Options → Remote Web Server): its address and, when HTTP Basic
/// Authentication is turned on there, the user name and password. LibreHardwareMonitor lets the port and the
/// network interface be changed, so the address is the whole of it, not only the port.
/// </summary>
public class LibreHardwareMonitorServer
{
    public const string DefaultAddress = "http://localhost:8085";

    /// <summary>Kept apart from the passwords that DPAPI protects for other programs of the user.</summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("OwlWidgets.SystemMonitor.LibreHardwareMonitor");

    /// <summary>As typed; empty: <see cref="DefaultAddress"/>. Without a scheme it is http.</summary>
    public string Address { get; set; } = DefaultAddress;

    /// <summary>Empty: the server is asked without authentication.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// The password protected by DPAPI for the current Windows user, in Base64: the settings file lies next to
    /// the exe, and only this user can read it back. Empty: none.
    /// </summary>
    [JsonPropertyName("Password")]
    public string ProtectedPassword { get; set; } = string.Empty;

    /// <returns>The address of the server, or null when <paramref name="text"/> is not one.</returns>
    public static Uri? ParseAddress(string? text)
    {
        var address = string.IsNullOrWhiteSpace(text) ? DefaultAddress : text.Trim();
        if (!address.Contains("://", StringComparison.Ordinal))
            address = "http://" + address;
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host))
        {
            return null;
        }

        return uri;
    }

    public static string ProtectPassword(string password) => password.Length == 0
        ? string.Empty
        : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(password), Entropy, DataProtectionScope.CurrentUser));

    /// <returns>Empty: no password.</returns>
    /// <exception cref="CryptographicException">Protected by another Windows user or on another computer.</exception>
    /// <exception cref="FormatException">Not Base64.</exception>
    public string ReadPassword() => ProtectedPassword.Length == 0
        ? string.Empty
        : Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(ProtectedPassword), Entropy, DataProtectionScope.CurrentUser));
}
