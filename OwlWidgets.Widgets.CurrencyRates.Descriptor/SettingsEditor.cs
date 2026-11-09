using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using OwlWidgets.Contracts;
using OwlWidgets.Widgets.CurrencyRates.Properties;

namespace OwlWidgets.Widgets.CurrencyRates;

internal sealed class SettingsEditor : IWidgetSettingsEditor
{
    private const double CurrencyWidth = 260;
    private const double SwapWidth = 40;
    private const double AmountWidth = 90;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly DataTemplate CurrencyTemplate = CreateCurrencyTemplate();

    private readonly TextBox _interval = new() { Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly StackPanel _pairs = new();
    private readonly Button _add = new() { Content = Strings.Settings_AddPair, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock _error = new() { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _view = new();
    private readonly List<PairRow> _rows = [];
    private string _accepted = string.Empty;

    public SettingsEditor(string? settings)
    {
        _view.Children.Add(Label(Strings.Settings_Interval));
        _view.Children.Add(_interval);
        _view.Children.Add(Label(string.Format(CultureInfo.CurrentCulture, Strings.Settings_Pairs, Config.MaxPairs)));
        _view.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Opacity = 0.7,
            Children =
            {
                new TextBlock { Text = Strings.Settings_From, Width = CurrencyWidth + 8 + SwapWidth + 8 },
                new TextBlock { Text = Strings.Settings_To, Width = CurrencyWidth + 8 },
                new TextBlock { Text = Strings.Settings_Amount, Width = AmountWidth }
            }
        });
        _view.Children.Add(_pairs);
        _add.Click += (_, _) =>
        {
            AddRow(new Config.CurrencyPair());
            Changed();
        };
        _view.Children.Add(_add);
        _view.Children.Add(_error);
        _interval.TextChanged += (_, _) => Changed();
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
        _interval.Text = config.UpdateInterval.ToString(CultureInfo.CurrentCulture);
        _pairs.Children.Clear();
        _rows.Clear();
        foreach (var pair in config.Pairs ?? [])
            AddRow(pair);
        _error.Text = string.Empty;
        _accepted = Serialize(config);
        Changed();
    }

    public void Dispose()
    {
    }

    private void Changed()
    {
        _add.IsEnabled = _rows.Count < Config.MaxPairs;
        HasChangesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void AddRow(Config.CurrencyPair pair)
    {
        var row = new PairRow(pair);
        row.Changed += (_, _) => Changed();
        row.RemoveRequested += (_, _) =>
        {
            _rows.Remove(row);
            _pairs.Children.Remove(row.View);
            Changed();
        };
        _rows.Add(row);
        _pairs.Children.Add(row.View);
    }

    private bool TryRead(out Config config, out string error)
    {
        config = new Config();
        if (!int.TryParse(_interval.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var interval)
            || interval < Config.MinUpdateInterval)
        {
            error = string.Format(CultureInfo.CurrentCulture, Strings.Error_Interval, Config.MinUpdateInterval);
            return false;
        }

        if (_rows.Count > Config.MaxPairs)
        {
            error = string.Format(CultureInfo.CurrentCulture, Strings.Error_TooManyPairs, Config.MaxPairs);
            return false;
        }

        config.UpdateInterval = interval;
        for (var i = 0; i < _rows.Count; i++)
        {
            if (!_rows[i].TryRead(out var pair, out var rowError))
            {
                error = string.Format(CultureInfo.CurrentCulture, rowError, i + 1);
                return false;
            }

            config.Pairs.Add(pair);
        }

        error = string.Empty;
        return true;
    }

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

    private static TextBlock Label(string text) => new() { Text = text, Margin = new Thickness(0, 8, 0, 2) };

    /// <summary>A round flag, or nothing for a currency without one, and the code with the name.</summary>
    private static DataTemplate CreateCurrencyTemplate()
    {
        var flag = new FrameworkElementFactory(typeof(Image));
        flag.SetValue(FrameworkElement.WidthProperty, 20.0);
        flag.SetValue(FrameworkElement.HeightProperty, 20.0);
        flag.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
        flag.SetValue(UIElement.ClipProperty, new EllipseGeometry(new Point(10, 10), 10, 10));
        flag.SetValue(RenderOptions.BitmapScalingModeProperty, BitmapScalingMode.HighQuality);
        flag.SetBinding(Image.SourceProperty, new Binding(nameof(Currency.Flag)));

        var title = new FrameworkElementFactory(typeof(TextBlock));
        title.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        title.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        title.SetBinding(TextBlock.TextProperty, new Binding(nameof(Currency.Title)));

        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        panel.AppendChild(flag);
        panel.AppendChild(title);

        var template = new DataTemplate { VisualTree = panel };
        template.Seal();
        return template;
    }

    private sealed class PairRow
    {
        private readonly ComboBox _base = CreateCurrencyBox();
        private readonly ComboBox _quote = CreateCurrencyBox();
        private readonly TextBox _amount = new() { Width = AmountWidth, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };

        public PairRow(Config.CurrencyPair pair)
        {
            Select(_base, string.IsNullOrWhiteSpace(pair.Base) ? new Config.CurrencyPair().Base : pair.Base);
            Select(_quote, string.IsNullOrWhiteSpace(pair.Quote) ? new Config.CurrencyPair().Quote : pair.Quote);
            _amount.Text = pair.Amount.ToString(CultureInfo.CurrentCulture);

            // MinWidth and an explicit padding: with a fixed width the manager's button style clipped the glyph.
            var swap = new Button
            {
                Content = "⇄",
                MinWidth = SwapWidth,
                Padding = new Thickness(8, 0, 8, 0),
                Margin = new Thickness(0, 0, 8, 0),
                ToolTip = Strings.Settings_Swap
            };
            swap.Click += (_, _) =>
            {
                var from = _base.SelectedItem;
                _base.SelectedItem = _quote.SelectedItem;
                _quote.SelectedItem = from;
            };
            var remove = new Button
            {
                Content = "✕",
                MinWidth = 28,
                Padding = new Thickness(8, 0, 8, 0),
                ToolTip = Strings.Settings_RemovePair
            };
            remove.Click += (_, _) => RemoveRequested?.Invoke(this, EventArgs.Empty);
            _base.SelectionChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
            _quote.SelectionChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
            _amount.TextChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
            View = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 4),
                Children = { _base, swap, _quote, _amount, remove }
            };
        }

        public StackPanel View { get; }

        public event EventHandler? Changed;

        public event EventHandler? RemoveRequested;

        /// <param name="error">A format with the number of the pair as {0}.</param>
        public bool TryRead(out Config.CurrencyPair pair, out string error)
        {
            pair = new Config.CurrencyPair();
            if (_base.SelectedItem is not Currency from || _quote.SelectedItem is not Currency to
                || string.Equals(from.Code, to.Code, StringComparison.OrdinalIgnoreCase))
            {
                error = Strings.Error_SameCurrency;
                return false;
            }

            if (!decimal.TryParse(_amount.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount) || amount <= 0)
            {
                error = Strings.Error_Amount;
                return false;
            }

            pair.Base = from.Code;
            pair.Quote = to.Code;
            pair.Amount = amount;
            error = string.Empty;
            return true;
        }

        private static ComboBox CreateCurrencyBox()
        {
            var box = new ComboBox
            {
                Width = CurrencyWidth,
                Margin = new Thickness(0, 0, 8, 0),
                ItemTemplate = CurrencyTemplate,
                ItemsSource = CurrencyCatalog.All
            };
            box.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
            // Typing a code jumps to the currency, whatever the order of names.
            TextSearch.SetTextPath(box, nameof(Currency.Code));
            return box;
        }

        /// <summary>A code the catalog does not know is kept: it is added to this box only.</summary>
        private static void Select(ComboBox box, string code)
        {
            if (!CurrencyCatalog.Contains(code))
                box.ItemsSource = CurrencyCatalog.All.Prepend(CurrencyCatalog.Find(code)).ToArray();
            box.SelectedItem = ((IEnumerable<Currency>)box.ItemsSource)
                .First(currency => string.Equals(currency.Code, code, StringComparison.OrdinalIgnoreCase));
        }
    }
}
