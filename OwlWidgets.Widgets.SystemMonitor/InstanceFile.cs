using System.IO;
using System.Text.Json;
using OwlWidgets.Contracts;

namespace OwlWidgets.Widgets.SystemMonitor;

/// <summary>
/// A small JSON file in the instance files (<c>InstanceDataFolder</c>) that the card keeps across restarts:
/// <c>card-state.json</c>, whether the card is collapsed. A file that is missing or does not read gives null;
/// a failure is a warning in the log, not an error of the card.
/// </summary>
internal sealed class InstanceFile<T>(string folder, string fileName, IWidgetLogger logger) where T : class
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private string Path => System.IO.Path.Combine(folder, fileName);

    public T? Read()
    {
        if (!File.Exists(Path))
            return null;
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(Path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.Log(WidgetLogLevel.Warning, $"{fileName} was not read from {folder}.", exception);
            return null;
        }
    }

    /// <summary>Through a temporary file: a crash in the middle leaves the previous content whole.</summary>
    public void Write(T value)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var temporary = Path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, Json));
            File.Move(temporary, Path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.Log(WidgetLogLevel.Warning, $"{fileName} was not written to {folder}.", exception);
        }
    }
}

/// <summary>What the card remembers of itself, <c>card-state.json</c>; not a setting, so not in the JSON of the instance.</summary>
internal sealed class CardState
{
    public bool Collapsed { get; set; }
}
