using System.Text;

namespace OwlWidgets.Widgets.SystemMonitor.Terminal;

/// <summary>The colours of the terminal palette; the grid gives each a brush.</summary>
internal enum Ink
{
    Fg,
    Dim,
    Red,
    Green,
    Yellow,
    Blue,
    Magenta,
    Cyan
}

/// <summary>
/// Text of one look on the character grid. Bold text of the plain colour is drawn brighter, as terminals do;
/// text on a <see cref="Back"/> colour is drawn in the background colour of the palette.
/// </summary>
internal sealed record Segment(string Text, Ink Ink = Ink.Fg, bool Bold = false)
{
    public Ink? Back { get; init; }

    public string? ToolTip { get; init; }

    public bool Underline { get; init; }

    /// <summary>The place of the card buttons: blank cells the grid leaves to the buttons laid over it.</summary>
    public bool Slot { get; init; }

    /// <summary>The three card buttons, two cells each.</summary>
    public static Segment ButtonSlot() => new(new string(' ', 6)) { Slot = true };
}

/// <summary>Widths in cells of the grid: a character takes one, an emoji two, a variation selector none.</summary>
internal static class Cells
{
    public static int Width(Rune rune) => rune.Value switch
    {
        0xFE0F or 0x200D => 0,
        >= 0x1F000 or 0x26A1 or 0x26AA or 0x26AB or 0x231A or 0x2B50 => 2,
        _ => 1
    };

    public static int Width(string text)
    {
        var width = 0;
        foreach (var rune in text.EnumerateRunes())
            width += Width(rune);
        return width;
    }

    public static int Width(IEnumerable<Segment> line) => line.Sum(s => Width(s.Text));

    /// <summary>Exactly <paramref name="width"/> cells: padded on the right, or cut with an ellipsis.</summary>
    public static string Fit(string text, int width)
    {
        if (width <= 0)
            return string.Empty;
        var current = Width(text);
        if (current <= width)
            return text + new string(' ', width - current);

        var result = new StringBuilder();
        var used = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var cells = Width(rune);
            if (used + cells > width - 1)
                break;
            result.Append(rune.ToString());
            used += cells;
        }

        return result.Append('…').Append(' ', width - 1 - used).ToString();
    }

    /// <summary>Exactly <paramref name="width"/> cells, padded on the left.</summary>
    public static string RFit(string text, int width)
    {
        var current = Width(text);
        return current > width ? Fit(text, width) : new string(' ', width - current) + text;
    }

    /// <summary>Words into lines of at most <paramref name="width"/> cells; a longer word stands on a line of its own.</summary>
    public static List<string> Wrap(string text, int width)
    {
        var lines = new List<string>();
        var line = string.Empty;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && Width(line) + 1 + Width(word) > width)
            {
                lines.Add(line);
                line = word;
            }
            else
            {
                line = line.Length > 0 ? line + " " + word : word;
            }
        }

        if (line.Length > 0)
            lines.Add(line);
        return lines;
    }

    /// <summary>Joins neighbours of the same look, so the grid draws fewer pieces.</summary>
    public static List<Segment> Merge(IEnumerable<Segment> segments)
    {
        var merged = new List<Segment>();
        foreach (var segment in segments)
        {
            if (merged.Count > 0 && merged[^1] is var last
                && last.Ink == segment.Ink && last.Bold == segment.Bold && last.Back == segment.Back
                && last.Underline == segment.Underline && last.ToolTip == null && segment.ToolTip == null
                && !last.Slot && !segment.Slot)
                merged[^1] = last with { Text = last.Text + segment.Text };
            else
                merged.Add(segment);
        }

        return merged;
    }
}
