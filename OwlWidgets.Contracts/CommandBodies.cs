namespace OwlWidgets.Contracts;

public sealed class MoveModeBody
{
    public bool Active { get; set; }

    public bool PositionSaved { get; set; }
}

/// <summary>
/// The position of the anchor corner of the card (see <see cref="CardCorner"/>), not of the window.
/// </summary>
public sealed class PositionBody
{
    public double X { get; set; }

    public double Y { get; set; }
}

/// <summary>The anchor corner of the instance: the corner the position is given for and the wrapper keeps in place.</summary>
public sealed class AnchorBody
{
    public CardCorner Anchor { get; set; }
}

/// <summary>
/// The card above all windows, games included, instead of between the desktop icons and regular windows.
/// </summary>
public sealed class TopmostBody
{
    public bool Topmost { get; set; }
}

public sealed class PlaceBody
{
    public string Hwnd { get; set; } = "0";
}

/// <summary>
/// The light or dark theme of the instance, whether chosen for it or taken from the application.
/// </summary>
public sealed class ThemeBody
{
    public bool Dark { get; set; }
}

/// <summary>
/// The UI language of the application as a two-letter code, for example "en" or "ru".
/// <see cref="System.Globalization.CultureInfo.CurrentUICulture"/> of the wrapper is already set to it.
/// </summary>
public sealed class LanguageBody
{
    public string Language { get; set; } = "en";
}

public sealed class NotificationBody
{
    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}
