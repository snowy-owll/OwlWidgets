using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using OwlWidgets.Common.Bluetooth;
using OwlWidgets.Contracts;
using OwlWidgets.Widgets.DeviceBatteryInfo.Properties;

namespace OwlWidgets.Widgets.DeviceBatteryInfo;

internal sealed class SettingsEditor : IWidgetSettingsEditor
{
    private const double DeviceWidth = 240;
    private const double IconWidth = 220;
    private const string BluetoothGlyph = "";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _dataFolder;
    private readonly TextBox _interval = new();
    private readonly StackPanel _devices = new();
    private readonly ComboBox _available = new() { Width = DeviceWidth, Margin = new Thickness(0, 0, 8, 0), ItemTemplate = CreateDeviceTemplate() };
    private readonly Button _add = new() { Content = Strings.Settings_Add, Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button _refresh = new() { Content = Strings.Settings_Refresh };
    private readonly TextBlock _status = new() { Opacity = 0.7, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBlock _userIconsLabel = Label(Strings.Settings_UserIcons);
    private readonly WrapPanel _userIconsPanel = new();
    private readonly TextBlock _iconsStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBlock _error = new() { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _view = new();
    private readonly List<DeviceRow> _rows = [];

    /// <summary>What the icon lists of the rows offer: the Bluetooth sign, the built-in icons, then the user icons.</summary>
    private readonly ObservableCollection<IconItem> _icons = [];

    /// <summary>Files of the user icons, as they will be saved.</summary>
    private readonly List<string> _userIcons = [];

    /// <summary>Files of the user icons in the saved settings: they stay when the screen closes without saving.</summary>
    private List<string> _acceptedUserIcons = [];

    /// <summary>Devices that report a battery level, as of the last search.</summary>
    private IReadOnlyList<BluetoothDevice> _found = [];

    /// <summary>The devices were listed at least once: a device that is not among them is not found.</summary>
    private bool _loaded;

    private bool _searching;
    private bool _importing;
    private string? _failure;
    private string? _accepted;
    private bool _disposed;

    public SettingsEditor(string? settings, string dataFolder)
    {
        _dataFolder = dataFolder;
        _icons.Add(new IconItem(null, Strings.Icon_None, null, BluetoothGlyph));
        foreach (var key in DeviceIcons.BuiltIn)
            _icons.Add(new IconItem(key, DeviceIcons.DisplayName(key), LoadIcon(key), string.Empty));

        _view.Children.Add(Label(Strings.Settings_Interval));
        _view.Children.Add(_interval);
        _view.Children.Add(Label(Strings.Settings_Devices));
        _view.Children.Add(_devices);
        _view.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 4, 0, 0),
            Children = { _available, _add, _refresh }
        });
        _view.Children.Add(_status);
        _view.Children.Add(_userIconsLabel);
        _view.Children.Add(_userIconsPanel);
        _view.Children.Add(_iconsStatus);
        _view.Children.Add(_error);

        _interval.TextChanged += (_, _) => Changed();
        _add.Click += (_, _) =>
        {
            if (_available.SelectedItem is not DeviceItem item)
                return;
            AddRow(new Config.Device { Address = item.Address, Name = item.Name });
            ShowDevices();
            Changed();
        };
        _refresh.Click += (_, _) => _ = LoadDevicesAsync();
        ReplaceSettings(settings);
        // Through the dispatcher: the awaits in it come back to the UI thread wherever the editor is created.
        _view.Dispatcher.InvokeAsync(LoadDevicesAsync);
    }

    public FrameworkElement View => _view;

    public bool HasChanges => Current() != _accepted;

    public event EventHandler? HasChangesChanged;

    public bool TrySerialize(out string settings)
    {
        if (!TryRead(out var config, out var error))
        {
            _error.Text = error;
            settings = string.Empty;
            return false;
        }

        _error.Text = string.Empty;
        settings = JsonSerializer.Serialize(config, Json);
        return true;
    }

    /// <summary>The user icons that are no longer listed are deleted: the card has applied the saved settings.</summary>
    public void Accept()
    {
        _accepted = Current();
        _acceptedUserIcons = [.. _userIcons];
        DeviceIcons.Prune(_dataFolder, _acceptedUserIcons);
        Changed();
    }

    public void ReplaceSettings(string? settings)
    {
        var config = Parse(settings);
        _interval.Text = config.UpdateInterval.ToString(CultureInfo.InvariantCulture);
        _userIcons.Clear();
        foreach (var file in config.UserIcons ?? [])
        {
            if (DeviceIcons.UserFileName(DeviceIcons.UserIcon(file)) != null && !_userIcons.Contains(file, StringComparer.OrdinalIgnoreCase))
                _userIcons.Add(file);
        }

        ShowUserIcons();
        _devices.Children.Clear();
        _rows.Clear();
        foreach (var device in config.Devices ?? [])
            AddRow(device);
        _error.Text = string.Empty;
        _iconsStatus.Text = string.Empty;
        _accepted = Current();
        _acceptedUserIcons = [.. _userIcons];
        DeviceIcons.Prune(_dataFolder, _acceptedUserIcons);
        ShowDevices();
        Changed();
    }

    /// <summary>User icons added and not saved are deleted.</summary>
    public void Dispose()
    {
        _disposed = true;
        DeviceIcons.Prune(_dataFolder, _acceptedUserIcons);
    }

    private void Changed() => HasChangesChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>A device without an address or a second row of one device is skipped.</summary>
    private void AddRow(Config.Device device)
    {
        var address = BluetoothAddress.Normalize(device.Address);
        if (address.Length == 0 || _rows.Any(existing => existing.Address == address))
            return;
        var row = new DeviceRow(address, device.Name, device.Icon, _icons);
        row.Changed += (_, _) => Changed();
        row.ChooseIconRequested += (_, _) => ChooseIcon(row);
        row.RemoveRequested += (_, _) =>
        {
            _rows.Remove(row);
            _devices.Children.Remove(row.View);
            ShowDevices();
            Changed();
        };
        _rows.Add(row);
        _devices.Children.Add(row.View);
    }

    /// <summary>PnP is asked on the thread pool; the lists are filled back on the UI thread.</summary>
    private async Task LoadDevicesAsync()
    {
        if (_disposed || _searching)
            return;
        _searching = true;
        _refresh.IsEnabled = false;
        ShowDevices();
        try
        {
            var found = await Task.Run(() => BluetoothDevices.GetAll().Where(device => device.BatteryLevel != null).ToList());
            if (_disposed)
                return;
            _found = found;
            _loaded = true;
            _failure = null;
        }
        catch (Exception exception)
        {
            if (_disposed)
                return;
            _failure = string.Format(CultureInfo.CurrentCulture, Strings.Settings_SearchFailed, exception.Message);
        }
        finally
        {
            _searching = false;
            _refresh.IsEnabled = true;
        }

        ShowDevices();
    }

    /// <summary>Copies a picture the user chose into the user icons and gives it to the row. Not saved yet.</summary>
    private async void ChooseIcon(DeviceRow row)
    {
        if (_importing)
            return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = Strings.Settings_IconFilter, CheckFileExists = true };
        var owner = Window.GetWindow(_view);
        if ((owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog()) != true)
            return;

        _importing = true;
        _iconsStatus.Text = string.Empty;
        try
        {
            var source = dialog.FileName;
            var dataFolder = _dataFolder;
            var taken = _userIcons.ToList();
            var file = await Task.Run(() => DeviceIcons.Import(source, dataFolder, taken));
            if (_disposed)
            {
                DeviceIcons.Prune(_dataFolder, _acceptedUserIcons);
                return;
            }

            row.Icon = AddUserIcon(file);
            Changed();
        }
        catch (Exception exception)
        {
            if (!_disposed)
                _iconsStatus.Text = ImportError(exception);
        }
        finally
        {
            _importing = false;
        }
    }

    /// <returns>The icon of the file in the settings.</returns>
    private string AddUserIcon(string file)
    {
        _userIcons.Add(file);
        ShowUserIcons();
        return DeviceIcons.UserIcon(file);
    }

    /// <summary>The rows with this icon get the Bluetooth sign. The file stays until the settings are saved.</summary>
    private void RemoveUserIcon(string file)
    {
        var icon = DeviceIcons.UserIcon(file);
        foreach (var row in _rows.Where(row => row.Icon == icon))
            row.Icon = null;
        _userIcons.Remove(file);
        ShowUserIcons();
        Changed();
    }

    /// <summary>The user icons in the lists of the rows and, each with a button that removes it, under the devices.</summary>
    private void ShowUserIcons()
    {
        var builtIn = 1 + DeviceIcons.BuiltIn.Count;
        var items = _userIcons.Select(file =>
        {
            var icon = DeviceIcons.UserIcon(file);
            return _icons.Skip(builtIn).FirstOrDefault(item => item.Key == icon)
                   ?? new IconItem(icon, DeviceIcons.DisplayName(icon), LoadIcon(icon), string.Empty);
        }).ToList();
        // Removed one by one: a row whose item stays keeps it selected.
        for (var index = _icons.Count - 1; index >= builtIn; index--)
        {
            if (!items.Contains(_icons[index]))
                _icons.RemoveAt(index);
        }

        foreach (var item in items.Where(item => !_icons.Contains(item)))
            _icons.Add(item);

        _userIconsPanel.Children.Clear();
        foreach (var file in _userIcons)
        {
            var item = items.First(item => item.Key == DeviceIcons.UserIcon(file));
            var remove = new Button
            {
                Content = "✕",
                MinWidth = 28,
                Padding = new Thickness(8, 0, 8, 0),
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = Strings.Settings_RemoveUserIcon
            };
            remove.Click += (_, _) => RemoveUserIcon(file);
            _userIconsPanel.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 16, 4),
                Children =
                {
                    new Image { Source = item.Image, Width = 32, Height = 32, ToolTip = file },
                    new TextBlock { Text = item.Name, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center },
                    remove
                }
            });
        }

        var visibility = _userIcons.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _userIconsLabel.Visibility = visibility;
        _userIconsPanel.Visibility = visibility;
    }

    /// <summary>
    /// The rows show what is known of their devices; the list offers the devices that are not added yet.
    /// When none is left, nothing can be added.
    /// </summary>
    private void ShowDevices()
    {
        foreach (var row in _rows)
            row.Show(_found.FirstOrDefault(device => device.Address == row.Address), _loaded);

        var items = _found
            .Where(device => _rows.All(row => row.Address != device.Address))
            .Select(device => new DeviceItem(device.Address, device.Name, Detail(device)))
            .ToList();
        _available.ItemsSource = items;
        _available.SelectedIndex = items.Count == 0 ? -1 : 0;
        _available.IsEnabled = items.Count > 0;
        _add.IsEnabled = items.Count > 0;

        _status.Text = _searching ? Strings.Settings_Searching
            : _failure ?? (!_loaded ? string.Empty
                : _found.Count == 0 ? Strings.Settings_NoDevices
                : items.Count == 0 ? Strings.Settings_AllAdded
                : Strings.Settings_DevicesHint);
    }

    private bool TryRead(out Config config, out string error)
    {
        config = new Config();
        if (!int.TryParse(_interval.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var interval) || interval <= 0)
        {
            error = Strings.Error_Interval;
            return false;
        }

        config.UpdateInterval = interval;
        config.Devices = _rows.Select(row => row.ToDevice()).ToList();
        config.UserIcons = [.. _userIcons];
        error = string.Empty;
        return true;
    }

    /// <returns>null: the fields do not make settings.</returns>
    private string? Current() => TryRead(out var config, out _) ? JsonSerializer.Serialize(config, Json) : null;

    /// <summary>null when it cannot be read: the item shows only its name.</summary>
    private ImageSource? LoadIcon(string icon)
    {
        try
        {
            return DeviceIcons.Load(icon, _dataFolder);
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or UnauthorizedAccessException
                                              or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>A plain reason instead of the text of the system, which is technical and may be in another language.</summary>
    private static string ImportError(Exception exception) => exception switch
    {
        DeviceIconException { TooLarge: true } => Strings.Settings_IconTooLarge,
        DeviceIconException => Strings.Settings_IconNotImage,
        FileNotFoundException or DirectoryNotFoundException => Strings.Settings_IconMissing,
        UnauthorizedAccessException => Strings.Settings_IconNoAccess,
        _ => Strings.Settings_IconFailed
    };

    private static string Detail(BluetoothDevice device) =>
        device.IsConnected == false
            ? $"{BluetoothAddress.Format(device.Address)} · {device.BatteryLevel}% · {Strings.State_Disconnected}"
            : $"{BluetoothAddress.Format(device.Address)} · {device.BatteryLevel}%";

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

    /// <summary>The name and, dimmed, the address with the level.</summary>
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

    /// <summary>The picture, or the glyph of the Bluetooth sign, and the name.</summary>
    private static DataTemplate CreateIconTemplate()
    {
        var image = new FrameworkElementFactory(typeof(Image));
        image.SetBinding(Image.SourceProperty, new Binding(nameof(IconItem.Image)));

        var glyph = new FrameworkElementFactory(typeof(TextBlock));
        glyph.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe MDL2 Assets"));
        glyph.SetValue(TextBlock.FontSizeProperty, 14.0);
        glyph.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        glyph.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        glyph.SetBinding(TextBlock.TextProperty, new Binding(nameof(IconItem.Glyph)));

        var picture = new FrameworkElementFactory(typeof(Grid));
        picture.SetValue(FrameworkElement.WidthProperty, 24.0);
        picture.SetValue(FrameworkElement.HeightProperty, 24.0);
        picture.AppendChild(image);
        picture.AppendChild(glyph);

        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0));
        name.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        name.SetBinding(TextBlock.TextProperty, new Binding(nameof(IconItem.Name)));

        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        panel.AppendChild(picture);
        panel.AppendChild(name);

        var template = new DataTemplate { VisualTree = panel };
        template.Seal();
        return template;
    }

    private sealed record DeviceItem(string Address, string Name, string Detail);

    /// <param name="Key">The icon in the settings; null: the Bluetooth sign.</param>
    /// <param name="Glyph">Drawn when there is no picture.</param>
    private sealed record IconItem(string? Key, string Name, ImageSource? Image, string Glyph);

    /// <summary>A device on the card: what it is, its icon and the buttons that change the icon and remove the device.</summary>
    private sealed class DeviceRow
    {
        private static readonly DataTemplate IconTemplate = CreateIconTemplate();

        private readonly string? _savedName;
        private readonly TextBlock _name = new() { TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly TextBlock _detail = new() { Opacity = 0.6, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly ComboBox _icon = new() { Width = IconWidth, Margin = new Thickness(0, 0, 8, 0), ToolTip = Strings.Settings_Icon };
        private readonly IReadOnlyList<IconItem> _icons;
        private string? _iconKey;
        private string? _foundName;
        private bool _selecting;

        public DeviceRow(string address, string? name, string? icon, ObservableCollection<IconItem> icons)
        {
            Address = address;
            _savedName = string.IsNullOrWhiteSpace(name) ? null : name;
            _icons = icons;
            _icon.ItemTemplate = IconTemplate;
            _icon.ItemsSource = icons;
            Icon = icon;
            // A removed item leaves no selection: the icon of the row stays until the screen changes it.
            _icon.SelectionChanged += (_, _) =>
            {
                if (_selecting || _icon.SelectedItem is not IconItem item)
                    return;
                _iconKey = item.Key;
                Changed?.Invoke(this, EventArgs.Empty);
            };
            // MinWidth and an explicit padding: with a fixed width the manager's button style clipped the glyph.
            var choose = new Button
            {
                Content = "…",
                MinWidth = 28,
                Padding = new Thickness(8, 0, 8, 0),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = Strings.Settings_ChooseIcon
            };
            choose.Click += (_, _) => ChooseIconRequested?.Invoke(this, EventArgs.Empty);
            var remove = new Button
            {
                Content = "✕",
                MinWidth = 28,
                Padding = new Thickness(8, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = Strings.Settings_Remove
            };
            remove.Click += (_, _) => RemoveRequested?.Invoke(this, EventArgs.Empty);
            View = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 4),
                Children =
                {
                    new StackPanel
                    {
                        Width = DeviceWidth,
                        Margin = new Thickness(0, 0, 8, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        Children = { _name, _detail }
                    },
                    _icon,
                    choose,
                    remove
                }
            };
            Show(null, false);
        }

        public string Address { get; }

        public StackPanel View { get; }

        /// <summary>An icon the list does not offer becomes the Bluetooth sign.</summary>
        public string? Icon
        {
            get => _iconKey;
            set
            {
                var item = _icons.FirstOrDefault(item => item.Key == value) ?? _icons[0];
                _iconKey = item.Key;
                _selecting = true;
                _icon.SelectedItem = item;
                _selecting = false;
            }
        }

        public event EventHandler? Changed;

        public event EventHandler? ChooseIconRequested;

        public event EventHandler? RemoveRequested;

        /// <param name="device">null: the device is not among the found ones.</param>
        /// <param name="loaded">false: the devices were not listed yet, so nothing is known.</param>
        public void Show(BluetoothDevice? device, bool loaded)
        {
            _foundName = device?.Name ?? _foundName;
            _name.Text = _foundName ?? _savedName ?? BluetoothAddress.Format(Address);
            _detail.Text = device != null ? Detail(device)
                : loaded ? $"{BluetoothAddress.Format(Address)} · {Strings.Settings_NotFound}"
                : BluetoothAddress.Format(Address);
        }

        /// <summary>The name stays as it was saved: a row that was not touched makes no change.</summary>
        public Config.Device ToDevice() => new()
        {
            Address = Address,
            Name = _savedName,
            Icon = _iconKey
        };
    }
}
