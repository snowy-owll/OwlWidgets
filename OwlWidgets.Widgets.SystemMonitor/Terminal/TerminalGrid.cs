using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace OwlWidgets.Widgets.SystemMonitor.Terminal;

/// <summary>
/// Lines of <see cref="Segment"/> on a grid of cells of a monospaced font. A run of ordinary characters is drawn
/// at once; any other character (box drawing, blocks, braille, emoji) is drawn in the middle of its own cell,
/// so a glyph taken from a fallback font of another width does not move the columns. Colours are those of
/// One Half Dark. A segment with a tooltip shows it under the mouse.
/// </summary>
internal sealed class TerminalGrid : FrameworkElement
{
    private const double FontSize = 13;
    private const double LineHeight = 1.3;
    /// <summary>Characters below it are drawn in runs: Latin, Cyrillic, °, ·.</summary>
    private const int RunLimit = 0x2000;

    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Courier New");
    private static readonly Typeface Regular = new(Mono, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface Bold = new(Mono, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    private static readonly Brush Foreground = Frozen(0xDC, 0xDF, 0xE4);
    private static readonly Brush Bright = Frozen(0xFF, 0xFF, 0xFF);
    private static readonly Brush Inverse = Frozen(0x28, 0x2C, 0x34);
    private static readonly Dictionary<Ink, Brush> Palette = new()
    {
        [Ink.Fg] = Foreground,
        [Ink.Dim] = Frozen(0x7F, 0x84, 0x8E),
        [Ink.Red] = Frozen(0xE0, 0x6C, 0x75),
        [Ink.Green] = Frozen(0x98, 0xC3, 0x79),
        [Ink.Yellow] = Frozen(0xE5, 0xC0, 0x7B),
        [Ink.Blue] = Frozen(0x61, 0xAF, 0xEF),
        [Ink.Magenta] = Frozen(0xC6, 0x78, 0xDD),
        [Ink.Cyan] = Frozen(0x56, 0xB6, 0xC2)
    };

    private readonly List<(Rect Area, string Text)> _tips = [];
    private IReadOnlyList<IReadOnlyList<Segment>> _lines = [];
    private int _columns;
    private double _cell;
    private double _baseline;

    private static double Row => FontSize * LineHeight;

    /// <summary>One cell of the grid: what an element laid over a run of cells is placed by.</summary>
    public Size Cell
    {
        get
        {
            EnsureMetrics();
            return new Size(_cell, Row);
        }
    }

    /// <summary>The size changes only when the number of lines or columns does.</summary>
    public void Show(IReadOnlyList<IReadOnlyList<Segment>> lines, int columns)
    {
        if (lines.Count != _lines.Count || columns != _columns)
            InvalidateMeasure();
        _lines = lines;
        _columns = columns;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureMetrics();
        return new Size(Math.Ceiling(_columns * _cell), Math.Ceiling(_lines.Count * Row));
    }

    protected override void OnRender(DrawingContext dc)
    {
        EnsureMetrics();
        // Transparent over the whole grid, so the tooltips are found between the characters too.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        _tips.Clear();
        for (var row = 0; row < _lines.Count; row++)
        {
            var top = row * Row;
            var column = 0;
            foreach (var segment in _lines[row])
            {
                var width = Cells.Width(segment.Text);
                var area = new Rect(column * _cell, top, width * _cell, Row);
                if (segment.Back is { } back)
                    dc.DrawRectangle(Palette[back], null, area);
                var brush = segment.Back != null ? Inverse : segment.Bold && segment.Ink == Ink.Fg ? Bright : Palette[segment.Ink];
                Draw(dc, segment.Text, column, top, segment.Bold ? Bold : Regular, brush);
                if (segment.Underline)
                {
                    var y = Math.Round(top + _baseline + 3) + 0.5;
                    dc.DrawLine(new Pen(Palette[Ink.Dim], 1), new Point(area.Left, y), new Point(area.Right, y));
                }

                if (segment.ToolTip != null)
                    _tips.Add((area, segment.ToolTip));
                column += width;
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var point = e.GetPosition(this);
        var tip = _tips.FirstOrDefault(t => t.Area.Contains(point)).Text;
        if (!Equals(ToolTip, tip))
            ToolTip = tip;
    }

    private void Draw(DrawingContext dc, string text, int column, double top, Typeface typeface, Brush brush)
    {
        var run = new StringBuilder();
        var runStart = column;
        void Flush()
        {
            if (run.Length > 0)
                Put(dc, Format(run.ToString(), typeface, brush), runStart * _cell, top);
            run.Clear();
        }

        var runes = text.EnumerateRunes().ToList();
        for (var i = 0; i < runes.Count; i++)
        {
            var rune = runes[i];
            if (rune.Value < RunLimit)
            {
                if (run.Length == 0)
                    runStart = column;
                run.Append(rune.ToString());
                column++;
                continue;
            }

            Flush();
            var cluster = rune.ToString();
            while (i + 1 < runes.Count && Cells.Width(runes[i + 1]) == 0)
                cluster += runes[++i].ToString();
            var cells = Cells.Width(rune);
            var glyph = Format(cluster, typeface, brush);
            Put(dc, glyph, column * _cell + (cells * _cell - glyph.WidthIncludingTrailingWhitespace) / 2, top);
            column += cells;
        }

        Flush();
    }

    /// <summary>On the baseline of the grid font, whatever font drew the glyph.</summary>
    private void Put(DrawingContext dc, FormattedText text, double x, double top) =>
        dc.DrawText(text, new Point(x, top + _baseline - text.Baseline));

    private FormattedText Format(string text, Typeface typeface, Brush brush) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, FontSize, brush, null,
            TextFormattingMode.Display, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private void EnsureMetrics()
    {
        if (_cell > 0)
            return;
        var sample = Format("0000000000", Regular, Foreground);
        _cell = sample.WidthIncludingTrailingWhitespace / 10;
        _baseline = (Row - sample.Height) / 2 + sample.Baseline;
    }

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
