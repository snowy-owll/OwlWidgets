using System.Text.Json;
using System.Text.Json.Serialization;
using OwlWidgets.Contracts;
using OwlWidgets.Services;

namespace OwlWidgets.Hosting;

/// <summary>
/// Light or dark theme of an instance: the application's one or fixed for the instance.
/// </summary>
internal enum InstanceTheme
{
    Application,
    Light,
    Dark
}

internal sealed class InstanceRecord
{
    public Guid InstanceId { get; set; }

    public Guid WidgetId { get; set; }

    /// <summary>The user's name of the instance; null: the widget type name is shown.</summary>
    public string? Tag { get; set; }

    public bool StartWithManager { get; set; } = true;

    public bool RestartOnCrash { get; set; } = true;

    /// <summary>Position of the <see cref="Anchor"/> corner of the card.</summary>
    public double X { get; set; }

    /// <inheritdoc cref="X"/>
    public double Y { get; set; }

    /// <summary>The corner the position is given for; the wrapper keeps it in place when the card changes its size.</summary>
    public CardCorner Anchor { get; set; }

    /// <summary>
    /// The last settled size of the card as the wrapper reported it; 0 until the first report. With it a change
    /// of the anchor of a stopped instance gives the position of the new corner without moving the card.
    /// </summary>
    public double CardWidth { get; set; }

    /// <inheritdoc cref="CardWidth"/>
    public double CardHeight { get; set; }

    /// <summary>
    /// Above all windows, games included, instead of between the desktop icons and regular windows. Turned on and off
    /// by the widget (<c>set-topmost</c>) or on the settings screen.
    /// </summary>
    public bool Topmost { get; set; }

    /// <summary>null: the default declared by the widget descriptor.</summary>
    public bool? InheritHostTheme { get; set; }

    public InstanceTheme Theme { get; set; } = InstanceTheme.Application;
}

internal sealed class InstanceRegistryFile
{
    public int Version { get; set; } = 1;

    public List<InstanceRecord> Instances { get; set; } = [];
}

internal sealed class InstanceRegistry
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _path;

    public InstanceRegistry(AppPaths paths) => _path = paths.InstancesPath;

    public bool Existed { get; private set; }

    public List<InstanceRecord> Instances { get; } = [];

    public void Load()
    {
        Instances.Clear();
        if (!File.Exists(_path))
        {
            Existed = false;
            return;
        }

        Existed = true;
        var file = JsonSerializer.Deserialize<InstanceRegistryFile>(File.ReadAllText(_path), Options);
        if (file?.Instances != null)
            Instances.AddRange(file.Instances);
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(new InstanceRegistryFile { Version = 1, Instances = Instances }, Options);
        AtomicFile.Write(_path, json);
    }
}
