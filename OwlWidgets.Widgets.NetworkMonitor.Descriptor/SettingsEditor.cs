using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using OwlWidgets.Contracts;
using OwlWidgets.Widgets.NetworkMonitor.Properties;

namespace OwlWidgets.Widgets.NetworkMonitor;

internal sealed class SettingsEditor : IWidgetSettingsEditor
{
    private const double SecondaryOpacity = 0.7;

    /// <summary>The key of the automatic choice in the list of adapters.</summary>
    private const string Automatic = "";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly ComboBox _adapter = new()
    {
        HorizontalAlignment = HorizontalAlignment.Left,
        MinWidth = 260,
        MaxWidth = 520,
        DisplayMemberPath = nameof(AdapterItem.Text),
        SelectedValuePath = nameof(AdapterItem.Id)
    };
    private readonly ComboBox _unit = new()
    {
        HorizontalAlignment = HorizontalAlignment.Left,
        MinWidth = 260,
        DisplayMemberPath = nameof(KeyValuePair<SpeedUnit, string>.Value),
        SelectedValuePath = nameof(KeyValuePair<SpeedUnit, string>.Key),
        ItemsSource = new Dictionary<SpeedUnit, string>
        {
            [SpeedUnit.Bits] = Strings.Settings_UnitBits,
            [SpeedUnit.Bytes] = Strings.Settings_UnitBytes
        }
    };
    private readonly StackPanel _view = new();
    private string _accepted = string.Empty;

    public SettingsEditor(string? settings)
    {
        _view.Children.Add(Label(Strings.Settings_Adapter));
        _view.Children.Add(_adapter);
        var note = Note();
        note.Text = Strings.Settings_AdapterNote;
        _view.Children.Add(note);
        _view.Children.Add(Label(Strings.Settings_Unit));
        _view.Children.Add(_unit);

        _adapter.SelectionChanged += (_, _) => Changed();
        _unit.SelectionChanged += (_, _) => Changed();
        ReplaceSettings(settings);
    }

    public FrameworkElement View => _view;

    public bool HasChanges => Serialize(Read()) != _accepted;

    public event EventHandler? HasChangesChanged;

    public bool TrySerialize(out string settings)
    {
        settings = Serialize(Read());
        return true;
    }

    public void Accept()
    {
        _accepted = Serialize(Read());
        HasChangesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ReplaceSettings(string? settings)
    {
        var config = Parse(settings);
        _adapter.ItemsSource = Items(config);
        _adapter.SelectedValue = config.AdapterId ?? Automatic;
        _unit.SelectedValue = config.Unit;
        _accepted = Serialize(config);
        Changed();
    }

    public void Dispose()
    {
    }

    private void Changed() => HasChangesChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Automatic, then the adapters of Windows that are up, then the others; the chosen adapter stays in the list
    /// under its saved name while Windows does not have it, so opening the screen does not change the choice.
    /// </summary>
    private static List<AdapterItem> Items(Config config)
    {
        var items = new List<AdapterItem> { new(Automatic, Strings.Settings_AdapterAuto, null) };
        var adapters = NetworkAdapters.List()
            .OrderBy(adapter => adapter.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up)
            .ThenBy(adapter => adapter.Name, StringComparer.CurrentCultureIgnoreCase);
        foreach (var adapter in adapters)
        {
            var name = NetworkAdapters.DisplayName(adapter);
            items.Add(new AdapterItem(adapter.Id,
                adapter.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up ? name : string.Format(Strings.Settings_AdapterNotConnected, name),
                adapter.Name));
        }

        if (config.AdapterId != null && items.All(item => item.Id != config.AdapterId))
            items.Add(new AdapterItem(config.AdapterId,
                string.Format(Strings.Settings_AdapterNotConnected, config.AdapterName ?? config.AdapterId), config.AdapterName));
        return items;
    }

    private Config Read()
    {
        var config = new Config();
        if (_adapter.SelectedItem is AdapterItem { Id: not Automatic } adapter)
        {
            config.AdapterId = adapter.Id;
            config.AdapterName = adapter.Name;
        }

        if (_unit.SelectedValue is SpeedUnit unit)
            config.Unit = unit;
        return config;
    }

    private static string Serialize(Config config) => JsonSerializer.Serialize(config, Json);

    private static Config Parse(string? settings)
    {
        if (string.IsNullOrWhiteSpace(settings))
            return new Config();
        try
        {
            return JsonSerializer.Deserialize<Config>(settings) ?? new Config();
        }
        catch (JsonException)
        {
            return new Config();
        }
    }

    private static TextBlock Label(string text) => new() { Text = text, Margin = new Thickness(0, 8, 0, 2), TextWrapping = TextWrapping.Wrap };

    private static TextBlock Note() => new() { Opacity = SecondaryOpacity, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left };

    /// <param name="Name">The name of the connection that is saved with the choice; null for the automatic one.</param>
    private sealed record AdapterItem(string Id, string Text, string? Name);
}
