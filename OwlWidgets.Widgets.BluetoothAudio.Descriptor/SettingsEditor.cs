using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using OwlWidgets.Common.Bluetooth;
using OwlWidgets.Contracts;
using OwlWidgets.Widgets.BluetoothAudio.Properties;

namespace OwlWidgets.Widgets.BluetoothAudio;

internal sealed class SettingsEditor : IWidgetSettingsEditor
{
    private const double FieldWidth = 360;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly ComboBox _device = new() { Width = FieldWidth, Margin = new Thickness(0, 0, 8, 0), ItemTemplate = CreateDeviceTemplate() };
    private readonly Button _refresh = new() { Content = Strings.Settings_Refresh };
    private readonly TextBlock _status = new() { Opacity = 0.7, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBox _title = new() { Width = FieldWidth, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly StackPanel _view = new();
    private IReadOnlyList<BluetoothAudioDevice> _found = [];
    private string? _address;
    private string _accepted = string.Empty;
    private bool _disposed;

    public SettingsEditor(string? settings)
    {
        _view.Children.Add(Label(Strings.Settings_Device));
        _view.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { _device, _refresh } });
        _view.Children.Add(_status);
        _view.Children.Add(Label(Strings.Settings_Title));
        _view.Children.Add(_title);
        _view.Children.Add(new TextBlock { Text = Strings.Settings_TitleHint, Opacity = 0.7, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });

        _device.SelectionChanged += (_, _) =>
        {
            if (_device.SelectedItem is DeviceItem item)
                _address = item.Address;
            Changed();
        };
        _title.TextChanged += (_, _) => Changed();
        _refresh.Click += (_, _) => _ = LoadDevicesAsync();
        ReplaceSettings(settings);
        // Through the dispatcher: the awaits in it come back to the UI thread wherever the editor is created.
        _view.Dispatcher.InvokeAsync(LoadDevicesAsync);
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
        _address = string.IsNullOrEmpty(config.Address) ? null : BluetoothAddress.Normalize(config.Address);
        _title.Text = config.Title ?? string.Empty;
        ShowDevices();
        _accepted = Serialize(Read());
        Changed();
    }

    public void Dispose()
    {
        _disposed = true;
    }

    private void Changed() => HasChangesChanged?.Invoke(this, EventArgs.Empty);

    private Config Read() => new()
    {
        Address = _address,
        Title = string.IsNullOrWhiteSpace(_title.Text) ? null : _title.Text.Trim()
    };

    /// <summary>Core Audio is asked on the thread pool; the list is filled back on the UI thread.</summary>
    private async Task LoadDevicesAsync()
    {
        if (_disposed)
            return;
        _refresh.IsEnabled = false;
        _status.Text = Strings.Settings_Searching;
        try
        {
            _found = await Task.Run(BluetoothAudioDevices.GetAll);
            if (_disposed)
                return;
            _status.Text = _found.Count == 0 ? Strings.Settings_NoDevices : Strings.Settings_DevicesHint;
        }
        catch (Exception exception)
        {
            if (_disposed)
                return;
            _found = [];
            _status.Text = string.Format(CultureInfo.CurrentCulture, Strings.Settings_SearchFailed, exception.Message);
        }
        finally
        {
            _refresh.IsEnabled = true;
        }

        ShowDevices();
    }

    /// <summary>A chosen device that is not found now stays in the list, marked, so that the settings keep it.</summary>
    private void ShowDevices()
    {
        var items = _found.Select(device => new DeviceItem(device.Address, device.Name, Detail(device))).ToList();
        if (_address != null && items.All(item => item.Address != _address))
            items.Insert(0, new DeviceItem(_address, BluetoothAddress.Format(_address), Strings.Settings_NotFound));

        var address = _address;
        _device.ItemsSource = items;
        _device.SelectedItem = items.FirstOrDefault(item => item.Address == address);
        _address = address;
    }

    private static string Detail(BluetoothAudioDevice device) =>
        $"{BluetoothAddress.Format(device.Address)} · {(device.IsConnected ? Strings.State_Connected : Strings.State_Disconnected)}";

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

    private static TextBlock Label(string text) => new() { Text = text, Margin = new Thickness(0, 8, 0, 2) };

    /// <summary>The name and, dimmed, the address with the state.</summary>
    private static DataTemplate CreateDeviceTemplate()
    {
        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        name.SetBinding(TextBlock.TextProperty, new Binding(nameof(DeviceItem.Name)));

        var detail = new FrameworkElementFactory(typeof(TextBlock));
        detail.SetValue(UIElement.OpacityProperty, 0.6);
        detail.SetValue(TextBlock.FontSizeProperty, 11.0);
        detail.SetBinding(TextBlock.TextProperty, new Binding(nameof(DeviceItem.Detail)));

        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.AppendChild(name);
        panel.AppendChild(detail);

        var template = new DataTemplate { VisualTree = panel };
        template.Seal();
        return template;
    }

    private sealed record DeviceItem(string Address, string Name, string Detail);
}
