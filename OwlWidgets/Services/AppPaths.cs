namespace OwlWidgets.Services;

/// <summary>
/// Where the manager keeps its files. Everything lies next to the executable.
/// </summary>
internal sealed class AppPaths
{
    public AppPaths(string baseDirectory) => BaseDirectory = baseDirectory;

    /// <summary>The folder of <c>OwlWidgets.exe</c>.</summary>
    public string BaseDirectory { get; }

    public string DataRoot => Path.Combine(BaseDirectory, "Data");

    public string InstancesPath => Path.Combine(DataRoot, "instances.json");

    public string ManagerSettingsPath => Path.Combine(DataRoot, "manager.json");

    public string LogsRoot => Path.Combine(DataRoot, "logs");

    public string WidgetsRoot => Path.Combine(BaseDirectory, "Widgets");

    public string HostExe => Path.Combine(BaseDirectory, "OwlWidgets.WidgetHost.exe");

    /// <summary>
    /// The assemblies the manager and the wrapper give to every widget, descriptor and runtime alike.
    /// A copy in a widget folder is not used: the type of an interface must be the one of this delivery.
    /// </summary>
    public static IReadOnlyList<string> SharedAssemblies { get; } =
        ["OwlWidgets.Contracts", "OwlWidgets.Common", "OwlWidgets.UI.Common"];

    public string SharedAssemblyPath(string name) => Path.Combine(BaseDirectory, name + ".dll");

    public string TrayIcon => Path.Combine(BaseDirectory, "Assets", "Images", "icon.ico");

    public string SettingsPath(Guid instanceId) =>
        Path.Combine(DataRoot, "settings", instanceId.ToString("N") + ".json");

    /// <summary>Files a widget keeps for one instance; see <c>IWidgetHostContext.InstanceDataFolder</c>.</summary>
    public string InstanceDataFolder(Guid instanceId) =>
        Path.Combine(DataRoot, "storage", instanceId.ToString("N"));
}

internal static class AtomicFile
{
    /// <summary>
    /// Writes a temporary file in the same folder and replaces the target with it.
    /// </summary>
    public static void Write(string path, string contents)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        var temp = path + ".tmp";
        File.WriteAllText(temp, contents);
        File.Move(temp, path, overwrite: true);
    }
}
