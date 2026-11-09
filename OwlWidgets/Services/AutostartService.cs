using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace OwlWidgets.Services;

/// <summary>
/// The start of the application when the user signs in to Windows.
/// </summary>
internal interface IAutostartService
{
    /// <summary>Windows starts this very executable at sign-in.</summary>
    bool IsEnabled { get; }

    /// <returns>false: the change failed and was logged.</returns>
    bool SetEnabled(bool enabled);
}

/// <summary>
/// A value in <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>: no administrator rights, and the value itself
/// is the setting, so removing it elsewhere turns the setting off. A value for another copy of the application
/// counts as off and is replaced when the setting is turned on.
/// </summary>
internal sealed class AutostartService : IAutostartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "OwlWidgets";

    private readonly ILogger<AutostartService> _log;

    public AutostartService(ILogger<AutostartService> log) => _log = log;

    private static string Command => $"\"{Environment.ProcessPath}\"";

    public bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is string value
                    && string.Equals(value.Trim(), Command, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception exception)
            {
                _log.LogWarning(exception, "Autostart value was not read");
                return false;
            }
        }
    }

    public bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
                key.SetValue(ValueName, Command);
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            _log.LogInformation(enabled ? "Autostart turned on: {Command}" : "Autostart turned off", Command);
            return true;
        }
        catch (Exception exception)
        {
            _log.LogError(exception, "Autostart was not turned {State}", enabled ? "on" : "off");
            return false;
        }
    }
}
