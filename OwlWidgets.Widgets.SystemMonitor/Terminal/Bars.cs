namespace OwlWidgets.Widgets.SystemMonitor.Terminal;

/// <summary>Bars drawn with characters; a filled part takes the colour it is given, the rest is dim.</summary>
internal static class Bars
{
    private static readonly string[] Eighths = ["", "▏", "▎", "▍", "▌", "▋", "▊", "▉", "█"];
    private const string Levels = "▁▂▃▄▅▆▇█";

    /// <summary>The load: below 50 % green, below 80 % yellow, then red, as on the classic card.</summary>
    public static Ink Usage(double percent) => percent < 50 ? Ink.Green : percent < 80 ? Ink.Yellow : Ink.Red;

    /// <summary>A temperature in °C: below 70 green, below 85 yellow, then red.</summary>
    public static Ink Heat(double celsius) => celsius < 70 ? Ink.Green : celsius < 85 ? Ink.Yellow : Ink.Red;

    public static List<Segment> Plain(double percent, int width, char full, char empty, Ink ink)
    {
        var filled = Filled(percent, width);
        return [new(new string(full, filled), ink), new(new string(empty, width - filled), Ink.Dim)];
    }

    /// <summary>Full blocks and one partial block in eighths: <c>███▍░░░</c>.</summary>
    public static List<Segment> Smooth(double percent, int width, Ink ink)
    {
        var exact = Math.Clamp(percent, 0, 100) / 100 * width;
        var full = (int)Math.Floor(exact);
        var part = (int)Math.Round((exact - full) * 8);
        var text = new string('█', full);
        if (part > 0 && full < width)
        {
            text += Eighths[part];
            full++;
        }

        return [new(text, ink), new(new string('░', width - full), Ink.Dim)];
    }

    /// <summary>Every cell in the colour of the load at its place, as btop does: green, then yellow, then red.</summary>
    public static List<Segment> Gradient(double percent, int width)
    {
        var filled = Filled(percent, width);
        var cells = new List<Segment>();
        for (var i = 0; i < width; i++)
            cells.Add(new("■", i < filled ? Usage((i + 0.5) / width * 100) : Ink.Dim));
        return Cells.Merge(cells);
    }

    /// <summary>htop: bars of <c>|</c> with the value over their right end.</summary>
    public static List<Segment> Htop(double percent, int width, string text, Ink ink)
    {
        var filled = Filled(percent, width);
        var runes = Cells.Fit(text, Math.Min(Cells.Width(text), width)).EnumerateRunes().Select(r => r.ToString()).ToList();
        var from = width - runes.Count;
        var cells = new List<Segment>();
        for (var i = 0; i < width; i++)
        {
            if (i >= from)
                cells.Add(new(runes[i - from], Ink.Dim, true));
            else
                cells.Add(i < filled ? new("|", ink) : new(" "));
        }

        return Cells.Merge(cells);
    }

    /// <summary>The last <paramref name="width"/> values between <paramref name="low"/> and <paramref name="high"/>; dots before the history starts.</summary>
    public static List<Segment> Spark(IReadOnlyList<double> values, int width, double low, double high, Func<double, Ink> ink)
    {
        var cells = new List<Segment>();
        for (var i = Math.Max(0, values.Count - width); i < values.Count; i++)
        {
            var share = high > low ? Math.Clamp((values[i] - low) / (high - low), 0, 1) : 0;
            cells.Add(new(Levels[(int)Math.Round(share * 7)].ToString(), ink(values[i])));
        }

        while (cells.Count < width)
            cells.Insert(0, new("·", Ink.Dim));
        return Cells.Merge(cells);
    }

    /// <summary>
    /// 20…100 °C: the range seen since the card started <c>━</c> in the colours of its temperatures,
    /// the current value <c>●</c>, the rest <c>─</c>. Without a value the range is dimmed and has no dot.
    /// </summary>
    public static List<Segment> Range(double? value, double min, double max, int width)
    {
        int At(double celsius) => Math.Clamp((int)Math.Floor((celsius - 20) / 80 * width), 0, width - 1);
        int current = value is { } v ? At(v) : -1, from = At(min), to = At(max);
        var cells = new List<Segment>();
        for (var i = 0; i < width; i++)
        {
            if (i == current)
                cells.Add(new("●", Ink.Fg, true));
            else if (i >= from && i <= to)
                cells.Add(new("━", value == null ? Ink.Dim : Heat(20 + (i + 0.5) / width * 80)));
            else
                cells.Add(new("─", Ink.Dim));
        }

        return Cells.Merge(cells);
    }

    private static int Filled(double percent, int width) => (int)Math.Round(Math.Clamp(percent, 0, 100) / 100 * width);
}
