using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using OwlWidgets.Contracts;
using OwlWidgets.UI.Common;
using OwlWidgets.Widgets.SystemMonitor.Properties;

namespace OwlWidgets.Widgets.SystemMonitor;

internal sealed class SettingsEditor : IWidgetSettingsEditor
{
    private const double SecondaryOpacity = 0.7;
    /// <summary>The options of a section stand under its box, shifted right.</summary>
    private static readonly Thickness Nested = new(24, 0, 0, 0);
    /// <summary>How often the open screen looks for drives connected or removed.</summary>
    private static readonly TimeSpan DriveRefresh = TimeSpan.FromSeconds(2);

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly CheckBox _showDrives = Check(Strings.Settings_ShowDrives);
    private readonly CheckBox _networkDrives = Check(Strings.Settings_NetworkDrives);
    private readonly CheckBox _externalDrives = Check(Strings.Settings_ExternalDrives);
    private readonly WrapPanel _drives = new();
    private readonly StackPanel _driveOptions = new() { Margin = Nested };
    private readonly CheckBox _showSystem = Check(Strings.Settings_ShowSystem);
    private readonly CheckBox _showTemperatures = Check(Strings.Settings_ShowTemperatures);
    /// <summary>The program serves the System and Temperature sections; it means nothing while both are off.</summary>
    private readonly StackPanel _providerOptions = new();
    private readonly ComboBox _provider = new()
    {
        HorizontalAlignment = HorizontalAlignment.Left,
        MinWidth = 260,
        DisplayMemberPath = nameof(KeyValuePair<SensorProviderKind, string>.Value),
        SelectedValuePath = nameof(KeyValuePair<SensorProviderKind, string>.Key),
        ItemsSource = new Dictionary<SensorProviderKind, string>
        {
            [SensorProviderKind.Auto] = Strings.Provider_Auto,
            [SensorProviderKind.HWiNFO] = Strings.Provider_HWiNFO,
            [SensorProviderKind.Afterburner] = Strings.Provider_Afterburner,
            [SensorProviderKind.LibreHardwareMonitor] = Strings.Provider_LibreHardwareMonitor,
            [SensorProviderKind.Windows] = Strings.Provider_Windows
        }
    };
    private readonly TextBlock _hwinfoNote = Note(Strings.Settings_HWiNFONote);
    private readonly TextBlock _afterburnerNote = Note(Strings.Settings_AfterburnerNote);
    /// <summary>The web server of LibreHardwareMonitor: shown while it is the program or the program is automatic.</summary>
    private readonly StackPanel _lhmOptions = new();
    private readonly TextBox _lhmAddress = new() { HorizontalAlignment = HorizontalAlignment.Left, Width = 260 };
    private readonly TextBox _lhmUserName = new() { HorizontalAlignment = HorizontalAlignment.Left, Width = 260 };
    private readonly PasswordBox _lhmPassword = new() { HorizontalAlignment = HorizontalAlignment.Left, Width = 260 };
    private readonly TextBlock _lhmError = new() { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly ComboBox _style = new()
    {
        HorizontalAlignment = HorizontalAlignment.Left,
        MinWidth = 260,
        MaxWidth = 520,
        SelectedValuePath = nameof(StyleOption.Value),
        ItemTemplate = StyleTemplate(),
        ItemsSource = new StyleOption[]
        {
            new(CardView.Classic, Strings.Style_Classic, Strings.Style_Classic_Note),
            new(CardView.Report, Strings.Style_Report, Strings.Style_Report_Note),
            new(CardView.Htop, Strings.Style_Htop, Strings.Style_Htop_Note),
            new(CardView.Prompt, Strings.Style_Prompt, Strings.Style_Prompt_Note),
            new(CardView.Btop, Strings.Style_Btop, Strings.Style_Btop_Note),
            new(CardView.Board, Strings.Style_Board, Strings.Style_Board_Note),
            new(CardView.ImHex, Strings.Style_ImHex, Strings.Style_ImHex_Note)
        }
    };
    private readonly Slider _background = new()
    {
        Width = 260,
        Minimum = 0,
        Maximum = 100,
        SmallChange = 5,
        LargeChange = 10,
        TickFrequency = 5,
        IsSnapToTickEnabled = true,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly TextBlock _backgroundValue = new() { MinWidth = 44, Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _backgroundReset = new() { Content = Strings.Settings_BackgroundReset, VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _buttonsAtAnchor = Check(Strings.Settings_ButtonsAtAnchor);
    private readonly CornerPicker _buttonCorner = new() { Corner = CardCorner.TopRight, Margin = new Thickness(0, 6, 0, 0) };
    private readonly CheckBox _collapsedCpuLoad = Check(Strings.Collapsed_CpuLoad);
    private readonly CheckBox _collapsedMemory = Check(Strings.Collapsed_Memory);
    private readonly CheckBox _collapsedGpuLoad = Check(Strings.Collapsed_GpuLoad);
    private readonly CheckBox _collapsedCpuTemperature = Check(Strings.Collapsed_CpuTemperature);
    private readonly CheckBox _collapsedGpuTemperature = Check(Strings.Collapsed_GpuTemperature);
    private readonly CheckBox _collapsedDrives = Check(Strings.Collapsed_Drives);
    private readonly StackPanel _view = new();
    private readonly DispatcherTimer _driveTimer = new() { Interval = DriveRefresh };

    /// <summary>The drives turned off on the screen now, by <see cref="DriveKinds.Id"/>, with their names.</summary>
    private readonly Dictionary<string, string> _hidden = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The drives of the computer at the last look; null until the first one ends.</summary>
    private IReadOnlyList<PresentDrive>? _present;
    /// <summary>The letters, volumes and kinds of <see cref="_present"/>: the labels are read again only when it changes.</summary>
    private string? _presentKey;
    private bool _refreshing;
    private bool _disposed;
    private string _accepted = string.Empty;
    /// <summary>
    /// The password as saved, and as typed: DPAPI gives a new text for the same password every time,
    /// so the saved one is kept while the password is not changed, and the screen has no changes.
    /// </summary>
    private string _savedPassword = string.Empty;
    private string _savedProtectedPassword = string.Empty;
    /// <summary>The style of <see cref="_accepted"/>: the card shows it until it starts again.</summary>
    private CardView _acceptedStyle;

    public SettingsEditor(string? settings)
    {
        _view.Children.Add(new TextBlock { Text = Strings.Settings_Style, Margin = new Thickness(0, 8, 0, 2) });
        _view.Children.Add(_style);
        _view.Children.Add(Note(Strings.Settings_StyleNote));
        _view.Children.Add(new TextBlock { Text = Strings.Settings_Background, Margin = new Thickness(0, 14, 0, 2) });
        _view.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { _background, _backgroundValue, _backgroundReset }
        });
        _view.Children.Add(Note(Strings.Settings_BackgroundNote));
        _view.Children.Add(new TextBlock { Text = Strings.Settings_Sections, Margin = new Thickness(0, 14, 0, 2) });
        _view.Children.Add(_showDrives);
        _driveOptions.Children.Add(_networkDrives);
        _driveOptions.Children.Add(_externalDrives);
        _driveOptions.Children.Add(new TextBlock { Text = Strings.Settings_Drives, Margin = new Thickness(0, 6, 0, 0) });
        _driveOptions.Children.Add(_drives);
        _driveOptions.Children.Add(Note(Strings.Settings_DrivesNote, nested: true));
        _view.Children.Add(_driveOptions);
        _view.Children.Add(_showSystem);
        _view.Children.Add(_showTemperatures);
        _view.Children.Add(Note(Strings.Settings_SectionsNote));
        _providerOptions.Children.Add(new TextBlock { Text = Strings.Settings_Provider, Margin = new Thickness(0, 14, 0, 2) });
        _providerOptions.Children.Add(_provider);
        _providerOptions.Children.Add(Note(Strings.Settings_ProviderNote));
        _providerOptions.Children.Add(_hwinfoNote);
        _providerOptions.Children.Add(_afterburnerNote);
        _lhmOptions.Children.Add(Note(Strings.Settings_LibreHardwareMonitorNote));
        _lhmOptions.Children.Add(new TextBlock { Text = Strings.Settings_LibreHardwareMonitorAddress, Margin = new Thickness(0, 8, 0, 2) });
        _lhmOptions.Children.Add(_lhmAddress);
        _lhmOptions.Children.Add(_lhmError);
        _lhmOptions.Children.Add(new TextBlock { Text = Strings.Settings_LibreHardwareMonitorUserName, Margin = new Thickness(0, 8, 0, 2) });
        _lhmOptions.Children.Add(_lhmUserName);
        _lhmOptions.Children.Add(new TextBlock { Text = Strings.Settings_LibreHardwareMonitorPassword, Margin = new Thickness(0, 8, 0, 2) });
        _lhmOptions.Children.Add(_lhmPassword);
        _providerOptions.Children.Add(_lhmOptions);
        _view.Children.Add(_providerOptions);
        _view.Children.Add(new TextBlock { Text = Strings.Settings_ButtonCorner, Margin = new Thickness(0, 14, 0, 2) });
        _view.Children.Add(_buttonsAtAnchor);
        _view.Children.Add(_buttonCorner);
        _view.Children.Add(Note(Strings.Settings_ButtonsNote));
        _view.Children.Add(new TextBlock { Text = Strings.Settings_Collapsed, Margin = new Thickness(0, 14, 0, 2) });
        foreach (var box in CollapsedBoxes)
            _view.Children.Add(box);
        _view.Children.Add(Note(Strings.Settings_CollapsedNote));

        foreach (var box in new[] { _showDrives, _networkDrives, _externalDrives, _showSystem, _showTemperatures, _buttonsAtAnchor }
            .Concat(CollapsedBoxes))
        {
            box.Checked += (_, _) => Changed();
            box.Unchecked += (_, _) => Changed();
        }

        _buttonCorner.CornerChanged += (_, _) => Changed();
        _background.ValueChanged += (_, _) => Changed();
        _backgroundReset.Click += (_, _) => _background.Value = Config.DefaultBackgroundOpacity;

        _provider.SelectionChanged += (_, _) => Changed();
        _style.SelectionChanged += (_, _) => Changed();
        _lhmAddress.TextChanged += (_, _) =>
        {
            ShowLhmError(null);
            Changed();
        };
        _lhmUserName.TextChanged += (_, _) => Changed();
        _lhmPassword.PasswordChanged += (_, _) => Changed();
        // Only while the screen is shown: a drive plugged in while it is open appears in a couple of seconds.
        _driveTimer.Tick += (_, _) => RefreshDrives();
        _view.IsVisibleChanged += (_, _) =>
        {
            if (!_view.IsVisible)
            {
                _driveTimer.Stop();
                return;
            }

            _driveTimer.Start();
            RefreshDrives();
        };
        ReplaceSettings(settings);
        RefreshDrives();
    }

    public FrameworkElement View => _view;

    private CheckBox[] CollapsedBoxes =>
        [_collapsedCpuLoad, _collapsedMemory, _collapsedGpuLoad, _collapsedCpuTemperature, _collapsedGpuTemperature, _collapsedDrives];

    public bool HasChanges => Serialize(Current()) != _accepted;

    public event EventHandler? HasChangesChanged;

    public bool RequiresRestart => Current().Style != _acceptedStyle;

    public bool TrySerialize(out string settings)
    {
        var current = Current();
        // The address is checked only while it is used; otherwise it is kept as typed.
        if (UsesLibreHardwareMonitor(current.Provider) && LibreHardwareMonitorServer.ParseAddress(current.LibreHardwareMonitor.Address) == null)
        {
            ShowLhmError(Strings.Settings_LibreHardwareMonitorAddressInvalid);
            settings = string.Empty;
            return false;
        }

        ShowLhmError(null);
        settings = Serialize(current);
        return true;
    }

    public void Accept()
    {
        var current = Current();
        _accepted = Serialize(current);
        _acceptedStyle = current.Style;
        _savedPassword = _lhmPassword.Password;
        _savedProtectedPassword = current.LibreHardwareMonitor.ProtectedPassword;
        HasChangesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ReplaceSettings(string? settings)
    {
        var config = Read(settings);
        _style.SelectedValue = config.Style;
        _acceptedStyle = config.Style;
        _provider.SelectedValue = config.Provider;
        var server = config.LibreHardwareMonitor ?? new LibreHardwareMonitorServer();
        _lhmAddress.Text = server.Address;
        _lhmUserName.Text = server.UserName;
        try
        {
            _savedPassword = server.ReadPassword();
            _savedProtectedPassword = server.ProtectedPassword;
        }
        catch (Exception exception) when (exception is System.Security.Cryptography.CryptographicException or FormatException)
        {
            // Saved by another Windows user: the box is empty, and the password is entered again.
            _savedPassword = string.Empty;
            _savedProtectedPassword = string.Empty;
        }

        _lhmPassword.Password = _savedPassword;
        ShowLhmError(null);
        _showDrives.IsChecked = config.ShowDrives;
        _networkDrives.IsChecked = config.ShowNetworkDrives;
        _externalDrives.IsChecked = config.ShowExternalDrives;
        _showSystem.IsChecked = config.ShowSystem;
        _showTemperatures.IsChecked = config.ShowTemperatures;
        _hidden.Clear();
        foreach (var drive in config.HiddenDrives.Where(d => !string.IsNullOrEmpty(d.Id)))
            _hidden[drive.Id] = string.IsNullOrEmpty(drive.Name) ? drive.Id : drive.Name;
        _buttonsAtAnchor.IsChecked = config.ButtonCorner == null;
        // The picker keeps a corner to start from when the box is cleared.
        _buttonCorner.Corner = config.ButtonCorner ?? CardCorner.TopRight;
        var collapsed = config.Collapsed ?? new CollapsedValues();
        _collapsedCpuLoad.IsChecked = collapsed.CpuLoad;
        _collapsedMemory.IsChecked = collapsed.Memory;
        _collapsedGpuLoad.IsChecked = collapsed.GpuLoad;
        _collapsedCpuTemperature.IsChecked = collapsed.CpuTemperature;
        _collapsedGpuTemperature.IsChecked = collapsed.GpuTemperature;
        _collapsedDrives.IsChecked = collapsed.Drives;
        _background.Value = Math.Clamp(config.BackgroundOpacity, 0, 100);
        ShowDrives();
        // From the screen, not the text: drives turned off in another order are not a change.
        _accepted = Serialize(Current());
        Changed();
    }

    public void Dispose()
    {
        _disposed = true;
        _driveTimer.Stop();
    }

    private Config Current() => new()
    {
        Style = _style.SelectedValue is CardView style ? style : default,
        Provider = _provider.SelectedValue is SensorProviderKind provider ? provider : default,
        LibreHardwareMonitor = new LibreHardwareMonitorServer
        {
            Address = _lhmAddress.Text.Trim(),
            UserName = _lhmUserName.Text.Trim(),
            ProtectedPassword = _lhmPassword.Password == _savedPassword
                ? _savedProtectedPassword
                : LibreHardwareMonitorServer.ProtectPassword(_lhmPassword.Password)
        },
        ShowDrives = _showDrives.IsChecked == true,
        ShowNetworkDrives = _networkDrives.IsChecked == true,
        ShowExternalDrives = _externalDrives.IsChecked == true,
        ShowSystem = _showSystem.IsChecked == true,
        ShowTemperatures = _showTemperatures.IsChecked == true,
        HiddenDrives = _hidden
            .OrderBy(d => d.Key, StringComparer.OrdinalIgnoreCase)
            .Select(d => new HiddenDrive { Id = d.Key, Name = d.Value })
            .ToList(),
        ButtonCorner = _buttonsAtAnchor.IsChecked == true ? null : _buttonCorner.Corner,
        Collapsed = new CollapsedValues
        {
            CpuLoad = _collapsedCpuLoad.IsChecked == true,
            Memory = _collapsedMemory.IsChecked == true,
            GpuLoad = _collapsedGpuLoad.IsChecked == true,
            CpuTemperature = _collapsedCpuTemperature.IsChecked == true,
            GpuTemperature = _collapsedGpuTemperature.IsChecked == true,
            Drives = _collapsedDrives.IsChecked == true
        },
        BackgroundOpacity = (int)Math.Round(_background.Value)
    };

    private void Changed()
    {
        _driveOptions.IsEnabled = _showDrives.IsChecked == true;
        _buttonCorner.IsEnabled = _buttonsAtAnchor.IsChecked != true;
        var background = (int)Math.Round(_background.Value);
        _backgroundValue.Text = string.Format(CultureInfo.CurrentCulture, Strings.Settings_BackgroundValue, background);
        _backgroundReset.IsEnabled = background != Config.DefaultBackgroundOpacity;
        // A value of a section turned off is not read, so the collapsed card cannot show it.
        _collapsedCpuLoad.IsEnabled = _collapsedMemory.IsEnabled = _collapsedGpuLoad.IsEnabled = _showSystem.IsChecked == true;
        _collapsedCpuTemperature.IsEnabled = _collapsedGpuTemperature.IsEnabled = _showTemperatures.IsChecked == true;
        _collapsedDrives.IsEnabled = _showDrives.IsChecked == true;
        _providerOptions.IsEnabled = _showSystem.IsChecked == true || _showTemperatures.IsChecked == true;
        var provider = Current().Provider;
        _hwinfoNote.Visibility = provider is SensorProviderKind.Auto or SensorProviderKind.HWiNFO ? Visibility.Visible : Visibility.Collapsed;
        _afterburnerNote.Visibility = provider is SensorProviderKind.Auto or SensorProviderKind.Afterburner ? Visibility.Visible : Visibility.Collapsed;
        _lhmOptions.Visibility = UsesLibreHardwareMonitor(provider) ? Visibility.Visible : Visibility.Collapsed;
        // The box of an external or network drive means nothing while all of its kind are off.
        foreach (var box in _drives.Children.OfType<CheckBox>())
        {
            box.IsEnabled = box.Tag switch
            {
                DriveKind.External => _externalDrives.IsChecked == true,
                DriveKind.Network => _networkDrives.IsChecked == true,
                _ => true
            };
        }

        HasChangesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Looks for the drives off the UI thread: a disk that is plugged in may take a moment to answer.
    /// The result goes back through the dispatcher of the view, whatever context the call started in.
    /// </summary>
    private void RefreshDrives()
    {
        if (_refreshing || _disposed)
            return;
        _refreshing = true;
        var known = _presentKey;
        var dispatcher = _view.Dispatcher;
        Task.Run(() =>
        {
            (string Key, List<PresentDrive> Drives)? found;
            try
            {
                found = ReadDrives(known);
            }
            catch (Exception)
            {
                // The list stays as it was; the next look tries again. An exception here would bring down the manager.
                found = null;
            }

            dispatcher.InvokeAsync(() =>
            {
                _refreshing = false;
                if (_disposed || found is not { } drives)
                    return;
                (_presentKey, _present) = drives;
                ShowDrives();
                Changed();
            });
        });
    }

    /// <summary>
    /// A box for every drive of the computer, and for a drive turned off that is not there now, so it can be turned on again.
    /// The boxes are made anew from <see cref="_hidden"/>, so what was changed on the screen stays.
    /// </summary>
    private void ShowDrives()
    {
        _drives.Children.Clear();
        if (_present == null)
            return;

        foreach (var drive in _present)
        {
            var name = string.IsNullOrEmpty(drive.Label) ? drive.Root
                : string.Format(CultureInfo.CurrentCulture, Strings.Format_DriveLabel, drive.Root, drive.Label);
            AddDrive(drive.Id, name, name, drive.Kind);
        }

        foreach (var (id, name) in _hidden.Where(h => !_present.Any(d => string.Equals(d.Id, h.Key, StringComparison.OrdinalIgnoreCase))))
            AddDrive(id, name, string.Format(CultureInfo.CurrentCulture, Strings.Format_DriveMissing, name), null);
    }

    /// <param name="name">Kept with the drive when it is turned off.</param>
    /// <param name="kind">Null: not connected now.</param>
    private void AddDrive(string id, string name, string text, DriveKind? kind)
    {
        var box = Check(text);
        box.Tag = kind;
        box.Margin = new Thickness(0, 4, 16, 0);
        box.IsChecked = !_hidden.ContainsKey(id);
        box.Checked += (_, _) =>
        {
            _hidden.Remove(id);
            Changed();
        };
        box.Unchecked += (_, _) =>
        {
            _hidden[id] = name;
            Changed();
        };
        _drives.Children.Add(box);
    }

    /// <returns>Null: the same drives as <paramref name="known"/>, so their labels are not read again.</returns>
    private static (string Key, List<PresentDrive> Drives)? ReadDrives(string? known)
    {
        var drives = new List<(DriveInfo Drive, string Id, DriveKind Kind)>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (DriveKinds.Of(drive) is { } kind)
                    drives.Add((drive, DriveKinds.Id(drive), kind));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        var key = string.Join("|", drives.Select(d => $"{d.Drive.Name} {d.Id} {d.Kind}"));
        if (key == known)
            return null;
        return (key, drives.Select(d => new PresentDrive(d.Id, d.Drive.Name, Label(d.Drive, d.Kind), d.Kind)).ToList());
    }

    /// <summary>A network drive is not asked for its label: the server may be away, and the call would wait for it.</summary>
    private static string? Label(DriveInfo drive, DriveKind kind)
    {
        if (kind == DriveKind.Network)
            return null;
        try
        {
            return drive.IsReady ? drive.VolumeLabel : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>An empty text block still takes a line, so it is hidden while there is no error.</summary>
    private void ShowLhmError(string? error)
    {
        _lhmError.Text = error ?? string.Empty;
        _lhmError.Visibility = error == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private static bool UsesLibreHardwareMonitor(SensorProviderKind provider) =>
        provider is SensorProviderKind.Auto or SensorProviderKind.LibreHardwareMonitor;

    private static string Serialize(Config config) => JsonSerializer.Serialize(config, Json);

    private static Config Read(string? settings)
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

    private static CheckBox Check(string text) => new() { Content = text, Margin = new Thickness(0, 6, 0, 0) };

    /// <param name="nested">Under a section box: as much narrower as it is shifted, so it ends where the others end.</param>
    private static TextBlock Note(string text, bool nested = false) => new()
    {
        Text = text,
        Opacity = SecondaryOpacity,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 6, 0, 0),
        MaxWidth = 520 - (nested ? Nested.Left : 0),
        HorizontalAlignment = HorizontalAlignment.Left
    };

    /// <summary>A style in the list and in the closed box: its name and, under it, what it looks like.</summary>
    private static DataTemplate StyleTemplate()
    {
        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(StyleOption.Name)));
        var note = new FrameworkElementFactory(typeof(TextBlock));
        note.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(StyleOption.Note)));
        note.SetValue(UIElement.OpacityProperty, SecondaryOpacity);
        note.SetValue(TextBlock.FontSizeProperty, 12.0);
        note.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        note.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 2, 0, 2));
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.AppendChild(name);
        panel.AppendChild(note);
        return new DataTemplate { VisualTree = panel };
    }

    private sealed record StyleOption(CardView Value, string Name, string Note);

    /// <param name="Label">Null: none, or not read (a network drive).</param>
    private sealed record PresentDrive(string Id, string Root, string? Label, DriveKind Kind);
}
