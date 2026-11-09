using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OwlWidgets.Contracts;
using OwlWidgets.UI.Common;
using OwlWidgets.Widgets.InternetSpeed.Properties;

namespace OwlWidgets.Widgets.InternetSpeed;

internal sealed class SettingsEditor : IWidgetSettingsEditor
{
    private const double SecondaryOpacity = 0.7;
    private const double NumberWidth = 120;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly ComboBox _provider = new()
    {
        HorizontalAlignment = HorizontalAlignment.Left,
        MinWidth = 260,
        DisplayMemberPath = nameof(KeyValuePair<SpeedTestProviderKind, string>.Value),
        SelectedValuePath = nameof(KeyValuePair<SpeedTestProviderKind, string>.Key),
        ItemsSource = new Dictionary<SpeedTestProviderKind, string>
        {
            [SpeedTestProviderKind.Cloudflare] = Strings.Provider_Cloudflare,
            [SpeedTestProviderKind.MLab] = Strings.Provider_MLab
        }
    };
    private readonly TextBlock _providerNote = Note();
    private readonly StackPanel _cloudflareFields = new();
    private readonly TextBox _streams = NumberBox();
    private readonly TextBox _duration = NumberBox();
    private readonly TextBox _autoInterval = NumberBox();
    private readonly CheckBox _buttonsAtAnchor = new() { Content = Strings.Settings_ButtonsAtAnchor, Margin = new Thickness(0, 0, 0, 4) };
    private readonly CornerPicker _buttonCorner = new() { Corner = CardCorner.TopRight };
    private readonly TextBlock _error = new() { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly StackPanel _view = new();
    private string _accepted = string.Empty;

    public SettingsEditor(string? settings)
    {
        _view.Children.Add(Label(Strings.Settings_Provider));
        _view.Children.Add(_provider);
        _view.Children.Add(_providerNote);
        // NDT7 fixes both: one connection, ten seconds per direction.
        _cloudflareFields.Children.Add(Label(string.Format(CultureInfo.CurrentCulture, Strings.Settings_Streams, Config.MinStreams, Config.MaxStreams)));
        _cloudflareFields.Children.Add(_streams);
        _cloudflareFields.Children.Add(Label(string.Format(CultureInfo.CurrentCulture, Strings.Settings_Duration, Config.MinDuration, Config.MaxDuration)));
        _cloudflareFields.Children.Add(_duration);
        _view.Children.Add(_cloudflareFields);
        _view.Children.Add(Label(string.Format(CultureInfo.CurrentCulture, Strings.Settings_AutoInterval, Config.MinAutoInterval)));
        _view.Children.Add(_autoInterval);
        var traffic = Note();
        traffic.Text = Strings.Settings_TrafficNote;
        _view.Children.Add(traffic);
        _view.Children.Add(Label(Strings.Settings_ButtonCorner));
        _view.Children.Add(_buttonsAtAnchor);
        _view.Children.Add(_buttonCorner);
        _view.Children.Add(_error);

        _provider.SelectionChanged += (_, _) =>
        {
            ShowProvider();
            Changed();
        };
        _streams.TextChanged += (_, _) => Changed();
        _duration.TextChanged += (_, _) => Changed();
        _autoInterval.TextChanged += (_, _) => Changed();
        _buttonsAtAnchor.Checked += (_, _) => ShowButtonCorner();
        _buttonsAtAnchor.Unchecked += (_, _) => ShowButtonCorner();
        _buttonCorner.CornerChanged += (_, _) => Changed();
        ReplaceSettings(settings);
    }

    public FrameworkElement View => _view;

    public bool HasChanges => !TryRead(out var config, out _) || Serialize(config) != _accepted;

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
        settings = Serialize(config);
        return true;
    }

    public void Accept()
    {
        if (TryRead(out var config, out _))
            _accepted = Serialize(config);
        HasChangesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ReplaceSettings(string? settings)
    {
        var config = Read(settings);
        _provider.SelectedValue = config.Provider;
        _streams.Text = config.Streams.ToString(CultureInfo.CurrentCulture);
        _duration.Text = config.Duration.ToString(CultureInfo.CurrentCulture);
        _autoInterval.Text = config.AutoInterval.ToString(CultureInfo.CurrentCulture);
        _buttonsAtAnchor.IsChecked = config.ButtonCorner == null;
        // The picker keeps a corner to start from when the box is cleared.
        _buttonCorner.Corner = config.ButtonCorner ?? CardCorner.TopRight;
        _buttonCorner.IsEnabled = config.ButtonCorner != null;
        _error.Text = string.Empty;
        _accepted = Serialize(config);
        ShowProvider();
        Changed();
    }

    public void Dispose()
    {
    }

    private void Changed() => HasChangesChanged?.Invoke(this, EventArgs.Empty);

    private void ShowButtonCorner()
    {
        _buttonCorner.IsEnabled = _buttonsAtAnchor.IsChecked != true;
        Changed();
    }

    private void ShowProvider()
    {
        var cloudflare = _provider.SelectedValue is not SpeedTestProviderKind.MLab;
        _providerNote.Text = cloudflare ? Strings.Settings_ProviderNote_Cloudflare : Strings.Settings_ProviderNote_MLab;
        _cloudflareFields.Visibility = cloudflare ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// The fields of Cloudflare are checked only while it is chosen; for M-Lab they keep the saved values,
    /// so switching back finds them as they were.
    /// </summary>
    private bool TryRead(out Config config, out string error)
    {
        config = new Config();
        if (_provider.SelectedValue is SpeedTestProviderKind provider)
            config.Provider = provider;
        var cloudflare = config.Provider == SpeedTestProviderKind.Cloudflare;

        if (!TryParse(_streams, out var streams) || (cloudflare && (streams < Config.MinStreams || streams > Config.MaxStreams)))
        {
            if (cloudflare)
            {
                error = string.Format(CultureInfo.CurrentCulture, Strings.Error_Streams, Config.MinStreams, Config.MaxStreams);
                return false;
            }

            streams = Config.DefaultStreams;
        }

        if (!TryParse(_duration, out var duration) || (cloudflare && (duration < Config.MinDuration || duration > Config.MaxDuration)))
        {
            if (cloudflare)
            {
                error = string.Format(CultureInfo.CurrentCulture, Strings.Error_Duration, Config.MinDuration, Config.MaxDuration);
                return false;
            }

            duration = Config.DefaultDuration;
        }

        if (!TryParse(_autoInterval, out var interval) || (interval != 0 && interval < Config.MinAutoInterval))
        {
            error = string.Format(CultureInfo.CurrentCulture, Strings.Error_AutoInterval, Config.MinAutoInterval);
            return false;
        }

        config.Streams = streams;
        config.Duration = duration;
        config.AutoInterval = interval;
        config.ButtonCorner = _buttonsAtAnchor.IsChecked == true ? null : _buttonCorner.Corner;
        error = string.Empty;
        return true;
    }

    private static bool TryParse(TextBox box, out int value) =>
        int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value) && value >= 0;

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

    private static TextBlock Label(string text) => new() { Text = text, Margin = new Thickness(0, 8, 0, 2), TextWrapping = TextWrapping.Wrap };

    private static TextBlock Note() => new() { Opacity = SecondaryOpacity, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left };

    private static TextBox NumberBox()
    {
        var box = new TextBox { Width = NumberWidth, HorizontalAlignment = HorizontalAlignment.Left };
        NumericInput.SetAllowNegative(box, false);
        NumericInput.SetAllowFraction(box, false);
        NumericInput.SetIsEnabled(box, true);
        return box;
    }
}
