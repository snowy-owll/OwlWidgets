using System.Text.Json.Serialization;

namespace OwlWidgets.Contracts;

/// <summary>
/// A corner of a card. As the anchor of an instance it is the point the position is given for: the wrapper keeps
/// that corner in place when the card grows or shrinks, so a card at the bottom of the screen grows upwards.
/// A widget may use it too, e.g. for buttons that should stay in place.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<CardCorner>))]
public enum CardCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

public static class CardCorners
{
    public static bool IsRight(this CardCorner corner) => corner is CardCorner.TopRight or CardCorner.BottomRight;

    public static bool IsBottom(this CardCorner corner) => corner is CardCorner.BottomLeft or CardCorner.BottomRight;
}
