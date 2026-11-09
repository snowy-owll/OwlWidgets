using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OwlWidgets.Contracts;
using OwlWidgets.Widgets.Weather.Properties;

namespace OwlWidgets.Widgets.Weather;

internal sealed class SettingsEditor : IWidgetSettingsEditor
{
    private const double SecondaryOpacity = 0.6;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly ComboBox _provider = new()
    {
        DisplayMemberPath = nameof(KeyValuePair<WeatherProviderKind, string>.Value),
        SelectedValuePath = nameof(KeyValuePair<WeatherProviderKind, string>.Key),
        ItemsSource = new Dictionary<WeatherProviderKind, string>
        {
            [WeatherProviderKind.OpenWeatherMap] = Strings.Provider_OpenWeatherMap,
            [WeatherProviderKind.OpenMeteo] = Strings.Provider_OpenMeteo
        }
    };
    private readonly TextBlock _apiKeyLabel = Label(Strings.Settings_ApiKey);
    private readonly TextBox _apiKey = new();
    private readonly TextBlock _placeName = new() { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _search = new();
    private readonly TextBlock _searchStatus = new() { Opacity = SecondaryOpacity, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 6, 10, 6) };
    private readonly ListBox _results = new();
    // The state of the search and the found places drop down under the field: the fields below stay in place.
    private readonly Popup _searchPopup = new() { Placement = PlacementMode.Bottom, VerticalOffset = 2, StaysOpen = false };
    private readonly TextBox _lang = new();
    private readonly TextBox _interval = new();
    private readonly CheckBox _showForecast = new() { Content = Strings.Settings_ShowForecast, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock _iconsName = new() { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly WrapPanel _iconsPreview = new() { Margin = new Thickness(0, 4, 0, 0) };
    private readonly Button _importZip = new() { Content = Strings.Icons_ImportZip, Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button _importFolder = new() { Content = Strings.Icons_ImportFolder, Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button _useBuiltIn = new() { Content = Strings.Icons_UseBuiltIn };
    private readonly TextBlock _iconsStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock _error = new() { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _view = new();
    // The search starts when the typing pauses, not on every letter.
    private readonly DispatcherTimer _searchDelay = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private CancellationTokenSource? _searchCancellation;
    // The names of the chosen place are asked when the typing of the language or the key pauses.
    private readonly DispatcherTimer _namesDelay = new() { Interval = TimeSpan.FromMilliseconds(400) };
    // Languages already asked for the chosen place: a name the geocoders do not know is not asked again on every pause.
    private readonly HashSet<string> _namesAsked = [];
    private CancellationTokenSource? _namesCancellation;
    // The settings are being put into the fields: that is no edit, the names stay as they were saved.
    private bool _replacing;
    private WeatherPlace? _place;
    private Config _accepted = new();
    // Files of the instance: the imported icon packs.
    private readonly string _dataFolder;
    private string? _iconPack;
    private string? _iconPackSource;
    private bool _importing;
    private bool _disposed;

    public SettingsEditor(string? settings, string instanceDataFolder)
    {
        _dataFolder = instanceDataFolder;
        _view.Children.Add(Label(Strings.Settings_Provider));
        _view.Children.Add(_provider);
        // Above the place: OpenWeatherMap searches with the key.
        _view.Children.Add(_apiKeyLabel);
        _view.Children.Add(_apiKey);
        _view.Children.Add(Label(Strings.Settings_Location));
        _view.Children.Add(_placeName);
        _view.Children.Add(Label(Strings.Settings_PlaceSearch));
        _view.Children.Add(_search);
        _view.Children.Add(_searchPopup);
        _view.Children.Add(Label(Strings.Settings_Language));
        _view.Children.Add(_lang);
        _view.Children.Add(Label(Strings.Settings_Interval));
        _view.Children.Add(_interval);
        _view.Children.Add(_showForecast);
        _view.Children.Add(Label(Strings.Settings_Icons));
        _view.Children.Add(_iconsName);
        _view.Children.Add(_iconsPreview);
        _view.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 6, 0, 0),
            Children = { _importZip, _importFolder, _useBuiltIn }
        });
        _view.Children.Add(_iconsStatus);
        _view.Children.Add(new TextBlock
        {
            Text = Strings.Icons_Hint,
            Opacity = SecondaryOpacity,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        });
        _view.Children.Add(_error);
        _importZip.Click += (_, _) => ChooseZip();
        _importFolder.Click += (_, _) => ChooseFolder();
        _useBuiltIn.Click += (_, _) => UseBuiltInIcons();

        ScrollViewer.SetHorizontalScrollBarVisibility(_results, ScrollBarVisibility.Disabled);
        _results.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && _results.SelectedItem is ListBoxItem { Tag: WeatherPlace place })
                Choose(place);
            else if (e.Key == Key.Escape)
                CloseSearchPopup();
        };
        _search.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && _searchPopup.IsOpen)
            {
                _searchPopup.IsOpen = false;
                e.Handled = true;
            }
            else if (e.Key == Key.Down && _searchPopup.IsOpen && _results.Items.Count > 0)
            {
                _results.SelectedIndex = 0;
                (_results.Items[0] as ListBoxItem)?.Focus();
                e.Handled = true;
            }
        };
        _searchPopup.PlacementTarget = _search;
        _searchPopup.Child = SearchPopupContent();
        _searchDelay.Tick += (_, _) => Search();
        _search.TextChanged += (_, _) => ScheduleSearch();

        foreach (var box in new[] { _apiKey, _lang, _interval })
            box.TextChanged += (_, _) => HasChangesChanged?.Invoke(this, EventArgs.Empty);
        // The key and the language change the answer of the geocoder.
        foreach (var box in new[] { _apiKey, _lang })
            box.TextChanged += (_, _) => ScheduleSearch();
        // The chosen place is shown with its name in the new language when it is known; a missing one is asked after the pause.
        // The key lets OpenWeatherMap answer for a place without an Open-Meteo id.
        _lang.TextChanged += (_, _) => ShowPlace();
        // With another key the names not given before may come.
        _apiKey.TextChanged += (_, _) => _namesAsked.Clear();
        foreach (var box in new[] { _apiKey, _lang })
            box.TextChanged += (_, _) => ScheduleNames();
        _namesDelay.Tick += (_, _) =>
        {
            _namesDelay.Stop();
            AddNames();
        };
        _showForecast.Click += (_, _) => HasChangesChanged?.Invoke(this, EventArgs.Empty);
        _provider.SelectionChanged += (_, _) =>
        {
            ShowProviderFields();
            ScheduleSearch();
            HasChangesChanged?.Invoke(this, EventArgs.Empty);
        };

        ReplaceSettings(settings);
        _accepted = ReadCurrentOrDefault();
    }

    public FrameworkElement View => _view;

    public bool HasChanges => !Same(_accepted, ReadCurrentOrNull());

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

    /// <summary>The saved settings become the snapshot; packs they do not name are deleted.</summary>
    public void Accept()
    {
        if (TryRead(out var config, out _))
            _accepted = config;
        IconPacks.Prune(_dataFolder, _accepted.IconPack);
        HasChangesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ReplaceSettings(string? settings)
    {
        _replacing = true;
        try
        {
            Replace(Read(settings));
        }
        finally
        {
            _replacing = false;
        }
    }

    private void Replace(Config config)
    {
        CancelNames();
        _provider.SelectedValue = config.Provider;
        _place = config.Place;
        ShowPlace();
        _search.Text = string.Empty;
        ClearSearch();
        _apiKey.Text = config.ApiKey ?? string.Empty;
        _lang.Text = config.Lang ?? string.Empty;
        _interval.Text = config.UpdateInterval.ToString();
        _showForecast.IsChecked = config.ShowForecast;
        _iconPack = config.IconPack;
        _iconPackSource = config.IconPackSource;
        _iconsStatus.Text = string.Empty;
        _error.Text = string.Empty;
        _accepted = config;
        // An import that was not saved goes, as do packs left by an earlier screen.
        IconPacks.Prune(_dataFolder, config.IconPack);
        ShowIcons();
        HasChangesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>An import that was not saved is deleted: only the pack of the saved settings stays.</summary>
    public void Dispose()
    {
        _disposed = true;
        CancelSearch();
        CancelNames();
        _searchPopup.IsOpen = false;
        IconPacks.Prune(_dataFolder, _accepted.IconPack);
    }

    private Config ReadCurrentOrDefault() => TryRead(out var config, out _) ? config : new Config();

    private Config? ReadCurrentOrNull() => TryRead(out var config, out _) ? config : null;

    private bool TryRead(out Config config, out string error)
    {
        config = new Config
        {
            Provider = SelectedProvider,
            Place = _place,
            ApiKey = _apiKey.Text,
            Lang = string.IsNullOrWhiteSpace(_lang.Text) ? null : _lang.Text.Trim(),
            ShowForecast = _showForecast.IsChecked == true,
            IconPack = _iconPack,
            IconPackSource = _iconPackSource
        };
        if (!int.TryParse(_interval.Text, out var interval) || interval <= 0)
        {
            error = Strings.Error_Interval;
            config = new Config();
            return false;
        }

        config.UpdateInterval = interval;
        error = string.Empty;
        return true;
    }

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

    private static bool Same(Config accepted, Config? current) =>
        current != null
        && accepted.Provider == current.Provider
        && accepted.Place == current.Place
        && accepted.ApiKey == current.ApiKey
        && accepted.Lang == current.Lang
        && accepted.UpdateInterval == current.UpdateInterval
        && accepted.ShowForecast == current.ShowForecast
        && accepted.IconPack == current.IconPack
        && accepted.IconPackSource == current.IconPackSource;

    private WeatherProviderKind SelectedProvider =>
        _provider.SelectedValue is WeatherProviderKind provider ? provider : WeatherProviderKind.OpenWeatherMap;

    /// <summary>Language of the names in the search: the one of the settings, else the language of the application.</summary>
    private string SearchLang =>
        string.IsNullOrWhiteSpace(_lang.Text) ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName : _lang.Text.Trim();

    /// <summary>The key is asked only for the provider that needs it; the value stays, so switching back keeps it.</summary>
    private void ShowProviderFields()
    {
        var visibility = SelectedProvider == WeatherProviderKind.OpenWeatherMap ? Visibility.Visible : Visibility.Collapsed;
        _apiKeyLabel.Visibility = visibility;
        _apiKey.Visibility = visibility;
    }

    private void ShowPlace()
    {
        _placeName.Text = _place?.FullNameIn(SearchLang) ?? Strings.Place_None;
        _placeName.Opacity = _place != null ? 1 : SecondaryOpacity;
    }

    private void Choose(WeatherPlace place)
    {
        _place = place;
        ShowPlace();
        _search.Text = string.Empty;
        ClearSearch();
        HasChangesChanged?.Invoke(this, EventArgs.Empty);
        CancelNames();
        AddNames();
    }

    private void ScheduleNames()
    {
        if (_replacing)
            return;
        _namesDelay.Stop();
        _namesDelay.Start();
    }

    private void CancelNames()
    {
        _namesDelay.Stop();
        _namesCancellation?.Cancel();
        _namesCancellation = null;
        _namesAsked.Clear();
    }

    /// <summary>
    /// Brings the names of the chosen place to <see cref="WeatherPlace.NameLanguages"/>: those of the application, so the
    /// card keeps its name when that language changes, and the one of the settings. A name in a language no longer needed
    /// is dropped; a missing one is asked with <see cref="PlaceSearch.GetNameAsync"/>, the same way for every provider.
    /// A name nobody gives leaves the name of the search, the card falls back to it.
    /// </summary>
    private async void AddNames()
    {
        _namesCancellation?.Cancel();
        if (_place == null)
            return;
        var languages = WeatherPlace.NameLanguages(SearchLang);
        var changed = false;
        var kept = _place.KeepNames(languages);
        if (!ReferenceEquals(kept, _place))
        {
            _place = kept;
            changed = true;
        }

        var missing = languages.Where(lang => !_place.HasName(lang) && !_namesAsked.Contains(lang)).ToList();
        var cancellation = _namesCancellation = new CancellationTokenSource();
        var place = _place;
        try
        {
            foreach (var lang in missing)
            {
                var name = await PlaceSearch.GetNameAsync(place, lang, _apiKey.Text, cancellation.Token);
                // Another place was chosen meanwhile, or the settings were replaced.
                if (cancellation.IsCancellationRequested || !SamePlace(_place, place))
                    return;
                _namesAsked.Add(lang);
                if (name != null)
                {
                    _place = _place!.WithName(lang, name);
                    changed = true;
                }
            }
        }
        catch (Exception)
        {
            if (cancellation.IsCancellationRequested)
                return;
        }

        if (!changed)
            return;
        ShowPlace();
        HasChangesChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool SamePlace(WeatherPlace? current, WeatherPlace asked) =>
        current != null && current.Latitude == asked.Latitude && current.Longitude == asked.Longitude;

    private void ScheduleSearch()
    {
        _searchDelay.Stop();
        _searchDelay.Start();
    }

    /// <summary>Drops the pending and the running search.</summary>
    private void CancelSearch()
    {
        _searchDelay.Stop();
        _searchCancellation?.Cancel();
        _searchCancellation = null;
    }

    /// <summary>Drops the search with its results and closes the list.</summary>
    private void ClearSearch()
    {
        CancelSearch();
        ShowSearch(string.Empty, []);
    }

    private void CloseSearchPopup()
    {
        _searchPopup.IsOpen = false;
        _search.Focus();
    }

    private async void Search()
    {
        CancelSearch();
        var query = _search.Text.Trim();
        if (query.Length < PlaceSearch.MinQueryLength)
        {
            ShowSearch(string.Empty, []);
            return;
        }

        var provider = SelectedProvider;
        var apiKey = _apiKey.Text.Trim();
        if (provider == WeatherProviderKind.OpenWeatherMap && apiKey.Length == 0)
        {
            ShowSearch(Strings.Search_NeedsKey, []);
            return;
        }

        var cancellation = _searchCancellation = new CancellationTokenSource();
        // The places of the previous query stay until the answer: the list does not blink on every letter.
        ShowSearchStatus(Strings.Search_Searching);
        try
        {
            var places = await PlaceSearch.FindAsync(provider, query, apiKey, SearchLang, cancellation.Token);
            if (cancellation.IsCancellationRequested)
                return;
            ShowSearch(places.Count == 0 ? Strings.Search_NothingFound : string.Empty, places);
        }
        catch (Exception exception)
        {
            // A newer search or a chosen place already replaced this one.
            if (cancellation.IsCancellationRequested)
                return;
            ShowSearch(
                exception is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized }
                    ? Strings.Search_KeyRejected
                    : Strings.Search_Failed,
                []);
        }
    }

    /// <summary>
    /// The drop-down list: the background of the theme under a faint outline, the state of the search
    /// above the places. The brushes come from the theme of the manager by key, the descriptor has no reference to it.
    /// </summary>
    private Grid SearchPopupContent()
    {
        var background = new Border();
        background.SetResourceReference(Border.BackgroundProperty, "MaterialDesign.Brush.Background");
        var outline = new Border { BorderThickness = new Thickness(1), Opacity = 0.25, IsHitTestVisible = false };
        outline.SetResourceReference(Border.BorderBrushProperty, "MaterialDesign.Brush.Foreground");
        var content = new StackPanel { Margin = new Thickness(1), Children = { _searchStatus, _results } };
        return new Grid { Children = { background, content, outline } };
    }

    private void ShowSearch(string status, IReadOnlyList<WeatherPlace> places)
    {
        ShowResults(places);
        ShowSearchStatus(status);
    }

    /// <summary>The list is open while it has something to show: a state or places.</summary>
    private void ShowSearchStatus(string status)
    {
        _searchStatus.Text = status;
        _searchStatus.Visibility = status.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        var open = status.Length > 0 || _results.Items.Count > 0;
        if (open && _searchPopup.Child is FrameworkElement content)
            content.Width = _search.ActualWidth;
        _searchPopup.IsOpen = open;
    }

    private void ShowResults(IReadOnlyList<WeatherPlace> places)
    {
        _results.Items.Clear();
        foreach (var place in places)
        {
            var details = new TextBlock
            {
                Text = place.Details,
                Opacity = SecondaryOpacity,
                Margin = new Thickness(8, 0, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var name = new TextBlock { Text = place.Name, FontWeight = FontWeights.SemiBold };
            DockPanel.SetDock(name, Dock.Left);
            var item = new ListBoxItem
            {
                Content = new DockPanel { Children = { name, details } },
                Tag = place,
                ToolTip = place.FullName
            };
            item.PreviewMouseLeftButtonUp += (_, _) => Choose(place);
            _results.Items.Add(item);
        }

        _results.Visibility = places.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ChooseZip()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = Strings.Icons_ZipFilter, CheckFileExists = true };
        if (ShowDialog(dialog))
            Import(dialog.FileName);
    }

    private void ChooseFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog();
        if (ShowDialog(dialog))
            Import(dialog.FolderName);
    }

    private bool ShowDialog(Microsoft.Win32.CommonDialog dialog) =>
        (Window.GetWindow(_view) is { } owner ? dialog.ShowDialog(owner) : dialog.ShowDialog()) == true;

    /// <summary>
    /// Copies the icons into a new pack off the UI thread. The pack is not saved yet: a previous import that was
    /// not saved goes at once, this one goes when the screen is closed or reverted without saving.
    /// </summary>
    private async void Import(string source)
    {
        if (_importing)
            return;
        _importing = true;
        _iconsStatus.Text = Strings.Icons_Importing;
        ShowIcons();
        try
        {
            var dataFolder = _dataFolder;
            var result = await Task.Run(() => IconPacks.Import(source, dataFolder));
            if (_disposed)
            {
                IconPacks.Prune(_dataFolder, _accepted.IconPack);
                return;
            }

            _iconPack = result.Pack;
            _iconPackSource = Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            IconPacks.Prune(_dataFolder, _accepted.IconPack, _iconPack);
            _iconsStatus.Text = ImportReport(result);
            HasChangesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (IconPackException exception)
        {
            _iconsStatus.Text = JoinSentences(Strings.Icons_NoneFound, Rejected(exception.Rejected));
        }
        catch (Exception exception)
        {
            _iconsStatus.Text = ImportError(exception);
        }
        finally
        {
            _importing = false;
            if (!_disposed)
                ShowIcons();
        }
    }

    private void UseBuiltInIcons()
    {
        _iconPack = null;
        _iconPackSource = null;
        IconPacks.Prune(_dataFolder, _accepted.IconPack);
        _iconsStatus.Text = string.Empty;
        ShowIcons();
        HasChangesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A plain reason instead of the text of the system, which is technical and may be in another language.</summary>
    private static string ImportError(Exception exception) => exception switch
    {
        InvalidDataException => Strings.Icons_BadArchive,
        FileNotFoundException or DirectoryNotFoundException => Strings.Icons_SourceMissing,
        UnauthorizedAccessException => Strings.Icons_NoAccess,
        IOException => Strings.Icons_ReadWriteFailed,
        _ => Strings.Icons_Failed
    };

    private static string ImportReport(IconPackImport result)
    {
        var missing = IconPacks.Codes.Except(result.Codes).ToList();
        return JoinSentences(
            string.Format(CultureInfo.CurrentCulture, Strings.Icons_Imported, result.Codes.Count, IconPacks.Codes.Count),
            missing.Count > 0 ? string.Format(CultureInfo.CurrentCulture, Strings.Icons_Missing, string.Join(", ", missing)) : null,
            Rejected(result.Rejected));
    }

    private static string? Rejected(IReadOnlyList<string> files) =>
        files.Count > 0 ? string.Format(CultureInfo.CurrentCulture, Strings.Icons_Rejected, string.Join(", ", files)) : null;

    private static string JoinSentences(params string?[] sentences) =>
        string.Join(" ", sentences.Where(sentence => !string.IsNullOrEmpty(sentence)));

    /// <summary>The pack and its icons in a row; a code it lacks shows as text, the card takes the built-in icon for it.</summary>
    private void ShowIcons()
    {
        _importZip.IsEnabled = !_importing;
        _importFolder.IsEnabled = !_importing;
        _useBuiltIn.IsEnabled = !_importing && _iconPack != null;
        _iconsPreview.Children.Clear();
        var folder = IconPacks.FolderPath(_dataFolder, _iconPack);
        if (folder == null)
        {
            _iconsName.Text = Strings.Icons_BuiltIn;
            _iconsPreview.Visibility = Visibility.Collapsed;
            return;
        }

        var codes = IconPacks.CodesIn(folder);
        _iconsName.Text = string.Format(CultureInfo.CurrentCulture, Strings.Icons_Pack,
            _iconPackSource ?? _iconPack, codes.Count, IconPacks.Codes.Count);
        _iconsPreview.Visibility = Visibility.Visible;
        foreach (var code in IconPacks.Codes)
        {
            var image = codes.Contains(code) ? LoadImage(Path.Combine(folder, IconPacks.IconFileName(code))) : null;
            _iconsPreview.Children.Add(image != null
                ? new Image { Source = image, Width = 32, Height = 32, ToolTip = code }
                : new TextBlock
                {
                    Text = code,
                    Width = 32,
                    Opacity = SecondaryOpacity,
                    FontSize = 10,
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });
        }
    }

    /// <summary>Read at once and released: the file is not locked while the screen is open.</summary>
    private static ImageSource? LoadImage(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or UnauthorizedAccessException
                                              or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static TextBlock Label(string text) => new() { Text = text, Margin = new Thickness(0, 8, 0, 2) };
}
