using System.ComponentModel;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using OwlWidgets.Common;
using OwlWidgets.Contracts;
using OwlWidgets.Hosting;
using OwlWidgets.Properties;
using OwlWidgets.Services;
using OwlWidgets.UI.Common;

namespace OwlWidgets.ViewModels;

internal sealed record InstanceThemeOption(InstanceTheme Value, string Title);

/// <summary>
/// One instance on the widgets screen. The state lives in <see cref="InstanceSession"/>:
/// the properties pass through to it, and its notifications are passed on.
/// </summary>
internal sealed class InstanceViewModel : ViewModelBase
{
    private static readonly InstanceThemeOption[] AllThemeOptions =
    [
        new(InstanceTheme.Application, Strings.Widgets_ThemeApplication),
        new(InstanceTheme.Light, Strings.Theme_Light),
        new(InstanceTheme.Dark, Strings.Theme_Dark)
    ];

    private static readonly JsonSerializerOptions IndentedJson = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IUiDispatcher _ui;
    private readonly ISettingsEditorFactory _editors;
    private ISettingsEditorSession? _editor;

    public InstanceViewModel(InstanceSession session, IUiDispatcher ui, ISettingsEditorFactory editors)
    {
        Session = session;
        _ui = ui;
        _editors = editors;
        StartCommand = new RelayCommand(() => session.Start(), () => session.CanStart);
        StopCommand = new RelayCommand(() => _ = session.Stop(), () => session.CanStop);
        MoveCommand = new RelayCommand(() => session.EnterMoveMode(), () => session.CanMove);
        SaveCommand = new RelayCommand(SaveSettings, () => CanSave);
        RevertCommand = new RelayCommand(RevertSettings, () => CanSave);
        ApplyPositionCommand = new RelayCommand(ApplyPosition, CanApplyPosition);
        RevertPositionCommand = new RelayCommand(ResetPositionDraft, () => PositionChanged);
        ResetPositionDraft();
        session.PropertyChanged += OnSessionPropertyChanged;
        session.SettingsPersisted += OnSettingsPersisted;
    }

    public InstanceSession Session { get; }

    public RelayCommand StartCommand { get; }

    public RelayCommand StopCommand { get; }

    public RelayCommand MoveCommand { get; }

    public RelayCommand SaveCommand { get; }

    public RelayCommand RevertCommand { get; }

    public IReadOnlyList<InstanceThemeOption> ThemeOptions => AllThemeOptions;

    public string Name => Session.Name;

    public string? Tag => Session.Tag;

    public string Title => Session.Title;

    public string Caption => Session.Caption;

    public ImageSource? Icon => Session.Icon;

    public bool HasIcon => Session.Icon != null;

    /// <summary>The type name is shown only under a tag, and a widget may have no description.</summary>
    public string MetaText => string.Join('\n', new[]
    {
        Session.Tag == null ? null : Session.Name,
        Session.Description,
        $"{Session.Version} · {Session.Author}",
    }.Where(line => !string.IsNullOrWhiteSpace(line)));

    public string InstanceId => Session.InstanceId.ToString();

    public string WidgetId => Session.WidgetId.ToString();

    public InstanceState State => Session.State;

    public string StateText => Session.StateText;

    public bool StartWithManager
    {
        get => Session.StartWithManager;
        set => Session.StartWithManager = value;
    }

    public bool RestartOnCrash
    {
        get => Session.RestartOnCrash;
        set => Session.RestartOnCrash = value;
    }

    /// <summary>Above all windows; the widget may switch it too, the box follows.</summary>
    public bool Topmost
    {
        get => Session.Topmost;
        set => Session.Topmost = value;
    }

    public bool InheritHostTheme
    {
        get => Session.InheritHostTheme;
        set => Session.InheritHostTheme = value;
    }

    public bool CanChangeInheritHostTheme => Session.CanChangeInheritHostTheme;

    public string InheritThemeHint => Session.CanChangeInheritHostTheme
        ? Strings.Widgets_InheritThemeHint
        : Strings.Widgets_InheritThemeRequired;

    public InstanceTheme Theme
    {
        get => Session.Theme;
        set => Session.Theme = value;
    }

    /// <summary>
    /// The X field: a draft until <see cref="ApplyPositionCommand"/>. A whole number; the sign follows
    /// the Windows regional settings.
    /// </summary>
    public string? PositionX
    {
        get => GetValue(() => PositionX);
        set
        {
            SetValue(() => PositionX, value);
            OnPositionDraftChanged();
        }
    }

    /// <summary>The Y field, see <see cref="PositionX"/>.</summary>
    public string? PositionY
    {
        get => GetValue(() => PositionY);
        set
        {
            SetValue(() => PositionY, value);
            OnPositionDraftChanged();
        }
    }

    /// <summary>The fields differ from the stored position.</summary>
    public bool PositionChanged =>
        PositionX != FormatCoordinate(Session.X) || PositionY != FormatCoordinate(Session.Y);

    /// <summary>Stores the position from the fields and moves the card.</summary>
    public RelayCommand ApplyPositionCommand { get; }

    /// <summary>Puts the stored position back into the fields.</summary>
    public RelayCommand RevertPositionCommand { get; }

    public bool CanEditPosition => Session.CanEditPosition;

    /// <summary>
    /// The corner the position is given for, applied at once: the card stays in place and the fields
    /// get the position of the new corner. A draft in the fields is replaced by it.
    /// </summary>
    public CardCorner Anchor
    {
        get => Session.Anchor;
        set => Session.SetAnchor(value);
    }

    /// <summary>The widget has its own settings screen; known once <see cref="EnsureEditor"/> has run.</summary>
    public bool HasSettings => _editor != null;

    /// <summary>The widget's own settings screen, shown as it is; null when there is none.</summary>
    public object? SettingsContent => _editor?.Content;

    public bool CanSave => _editor?.HasChanges == true;

    /// <summary>The settings file, indented for reading; text that is not valid JSON is shown as it is.</summary>
    public string JsonText => HasJson ? FormatJson(Session.SettingsJson!) : string.Empty;

    public bool HasJson => !string.IsNullOrWhiteSpace(Session.SettingsJson);

    /// <summary>
    /// Makes the settings screen the first time the instance is selected: widgets build it on demand.
    /// A widget without a screen, or one that failed to make it, is asked again next time.
    /// </summary>
    public void EnsureEditor()
    {
        if (_editor != null)
            return;
        _editor = _editors.Create(Session.Package, Session.InstanceId, Session.ReadSettings(), Session.Log);
        if (_editor == null)
            return;
        _editor.HasChangesChanged += OnEditorHasChangesChanged;
        OnPropertyChanged(nameof(HasSettings));
        OnPropertyChanged(nameof(SettingsContent));
        OnCanSaveChanged();
    }

    /// <summary>The instance left the screen: its notifications are no longer needed and its settings screen is released.</summary>
    public void Detach()
    {
        Session.PropertyChanged -= OnSessionPropertyChanged;
        Session.SettingsPersisted -= OnSettingsPersisted;
        var editor = _editor;
        if (editor == null)
            return;
        _editor = null;
        editor.HasChangesChanged -= OnEditorHasChangesChanged;
        OnPropertyChanged(nameof(SettingsContent));
        OnPropertyChanged(nameof(HasSettings));
        editor.Dispose();
    }

    /// <summary>The file is written first; the screen takes the settings as its snapshot only after that.</summary>
    private void SaveSettings()
    {
        if (_editor == null || !_editor.TryGetSettings(out var settings))
            return;
        Session.SaveSettings(settings, _editor.RequiresRestart);
        _editor.Accept();
        OnCanSaveChanged();
    }

    /// <summary>Returns the settings screen to the JSON that is in the file now.</summary>
    private void RevertSettings()
    {
        if (_editor == null || !_editor.HasChanges)
            return;
        _editor.Replace(Session.ReadSettings());
        Session.Log.LogInformation("Unsaved changes on the settings screen discarded");
        OnCanSaveChanged();
    }

    private void OnSettingsPersisted(string settings) => _ui.Invoke(() =>
    {
        if (_editor == null)
            return;
        _editor.Replace(settings);
        OnCanSaveChanged();
    });

    private void OnEditorHasChangesChanged(object? sender, EventArgs e) => _ui.Invoke(OnCanSaveChanged);

    private void OnCanSaveChanged()
    {
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.RaiseCanExecuteChanged();
        RevertCommand.RaiseCanExecuteChanged();
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e) => _ui.Invoke(() =>
    {
        OnPropertyChanged(e.PropertyName);
        switch (e.PropertyName)
        {
            case nameof(InstanceSession.Tag):
                OnPropertyChanged(nameof(MetaText));
                break;
            case nameof(InstanceSession.SettingsJson):
                OnPropertyChanged(nameof(HasJson));
                OnPropertyChanged(nameof(JsonText));
                break;
            case nameof(InstanceSession.CanStart):
                StartCommand.RaiseCanExecuteChanged();
                break;
            case nameof(InstanceSession.CanStop):
                StopCommand.RaiseCanExecuteChanged();
                break;
            case nameof(InstanceSession.CanMove):
                MoveCommand.RaiseCanExecuteChanged();
                break;
            // The position moved: by the move mode, or from these fields.
            case nameof(InstanceSession.X):
            case nameof(InstanceSession.Y):
                ResetPositionDraft();
                break;
            case nameof(InstanceSession.Anchor):
                OnPropertyChanged(nameof(Anchor));
                ResetPositionDraft();
                break;
            case nameof(InstanceSession.CanEditPosition):
                ApplyPositionCommand.RaiseCanExecuteChanged();
                break;
        }
    });

    private bool CanApplyPosition() =>
        Session.CanEditPosition && PositionChanged
        && TryParseCoordinate(PositionX, out _) && TryParseCoordinate(PositionY, out _);

    private void ApplyPosition()
    {
        if (!TryParseCoordinate(PositionX, out var x) || !TryParseCoordinate(PositionY, out var y))
            return;
        Session.SetPosition(x, y);
        ResetPositionDraft();
    }

    private void ResetPositionDraft()
    {
        SetValue(() => PositionX, FormatCoordinate(Session.X));
        SetValue(() => PositionY, FormatCoordinate(Session.Y));
        OnPositionDraftChanged();
    }

    private void OnPositionDraftChanged()
    {
        OnPropertyChanged(nameof(PositionChanged));
        ApplyPositionCommand.RaiseCanExecuteChanged();
        RevertPositionCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Whole units: a position stored with a fraction before rounding shows rounded.</summary>
    private static string FormatCoordinate(double value) =>
        Math.Round(value, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.CurrentCulture);

    private static bool TryParseCoordinate(string? text, out double value)
    {
        var parsed = int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.CurrentCulture, out var whole);
        value = whole;
        return parsed;
    }

    private static string FormatJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(document.RootElement, IndentedJson);
        }
        catch (JsonException)
        {
            return json;
        }
    }
}
