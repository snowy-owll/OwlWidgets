using System.Globalization;
using OwlWidgets.Widgets.SystemMonitor.Properties;

namespace OwlWidgets.Widgets.SystemMonitor.Terminal;

/// <summary>What the card shows at one moment, with the recent values for the sparklines.</summary>
internal sealed record CardSnapshot(
    string Time,
    string Date,
    string Host,
    string User,
    bool ShowDrives,
    bool ShowSystem,
    bool ShowTemperatures,
    IReadOnlyList<DriveRow> Drives,
    double Cpu,
    IReadOnlyList<double> CpuHistory,
    double RamUsed,
    double RamTotal,
    double RamPercent,
    IReadOnlyList<GpuRow> Gpus,
    IReadOnlyList<TemperatureRow> Temperatures);

internal sealed record DriveRow(string Name, double Used, double Total, double Percent);

internal sealed record GpuRow(string Name, double Percent);

/// <param name="Value">null: no value, <paramref name="Hint"/> says why; a card <paramref name="Asleep"/> is shown as idle.</param>
internal sealed record TemperatureRow(
    string Name,
    bool IsCpu,
    double? Value,
    double? Min,
    double? Max,
    string? Hint,
    bool Asleep,
    IReadOnlyList<double> History);

/// <summary>A value of the collapsed card: a load in percent or a temperature in °C.</summary>
/// <param name="Value">null: no value; a card <paramref name="Asleep"/> is shown as idle.</param>
internal sealed record CollapsedRow(string Label, bool IsTemperature, double? Value, bool Asleep, string? Tip);

/// <summary>The corner of the card buttons.</summary>
internal readonly record struct ButtonPlace(bool Top, bool Left);

/// <summary>
/// The terminal styles of the card: lines of a grid of <c>columns</c> characters. Every line is exactly as wide,
/// so the columns of all rows stand in line. The command lines (<c>sysmon --watch</c>, <c>user@host</c>) are
/// decoration and are not translated. The card buttons take a <see cref="Segment.ButtonSlot"/> in the first line
/// of the header in a top corner, a line of their own in a bottom one (in the frame of the last box of btop).
/// </summary>
internal static class TerminalLayouts
{
    private static CultureInfo Culture => CultureInfo.CurrentCulture;

    public static List<List<Segment>> Render(CardView view, CardSnapshot card, int columns, ButtonPlace buttons)
    {
        var lines = view switch
        {
            CardView.Htop => Htop(card, columns, buttons),
            CardView.Prompt => Prompt(card, columns, buttons),
            CardView.Btop => Btop(card, columns, buttons),
            CardView.Board => Board(card, columns, buttons),
            _ => Report(card, columns, buttons)
        };
        if (buttons.Top)
            return lines;

        var last = lines.Count > 0 ? lines[^1] : null;
        if (view == CardView.Btop && last is [{ Text: var frame }, ..] && frame.StartsWith('╰'))
        {
            var dash = new Segment(new string('─', columns - 11), Ink.Dim);
            lines[^1] = buttons.Left
                ? [new("╰─ ", Ink.Dim), Segment.ButtonSlot(), new(" ", Ink.Dim), dash, new("╯", Ink.Dim)]
                : [new("╰", Ink.Dim), dash, new(" "), Segment.ButtonSlot(), new(" ─╯", Ink.Dim)];
        }
        else
        {
            lines.Add(buttons.Left ? [Segment.ButtonSlot()] : [new(new string(' ', columns - 6)), Segment.ButtonSlot()]);
        }

        return lines;
    }

    /// <summary>
    /// The values in lines as long as they go; the line of the buttons is shorter by them. In a bottom corner the lines
    /// are filled from the last one, so the short one is the last.
    /// </summary>
    public static List<List<Segment>> RenderCollapsed(CardView view, IReadOnlyList<CollapsedRow> values, int columns, ButtonPlace buttons)
    {
        var (separator, item) = CollapsedItems(view);
        var items = values.Select(item).ToList();
        if (!buttons.Top)
            items.Reverse();

        var packed = new List<List<List<Segment>>>();
        var line = new List<List<Segment>>();
        var used = 0;
        foreach (var value in items)
        {
            var width = Cells.Width(value) + (line.Count > 0 ? Cells.Width(separator) : 0);
            var room = packed.Count == 0 ? columns - 7 : columns;
            if (line.Count > 0 && used + width > room)
            {
                packed.Add(line);
                line = [];
                used = 0;
                width = Cells.Width(value);
            }

            line.Add(value);
            used += width;
        }

        packed.Add(line);
        if (!buttons.Top)
        {
            packed.ForEach(l => l.Reverse());
            packed.Reverse();
        }

        var lines = packed.Select(l => l.SelectMany((value, i) => i > 0 ? separator.Concat(value) : value).ToList()).ToList();
        var at = buttons.Top ? 0 : lines.Count - 1;
        lines[at] = buttons.Left ? [Segment.ButtonSlot(), new(" "), .. lines[at]] : Spread(lines[at], [new(" "), Segment.ButtonSlot()], columns);
        return lines;
    }

    /// <summary>A value in the manner of each style and what stands between two values.</summary>
    private static (List<Segment> Separator, Func<CollapsedRow, List<Segment>> Item) CollapsedItems(CardView view)
    {
        static Ink InkOf(CollapsedRow row) => row.Value is not { } value ? Ink.Dim : row.IsTemperature ? Bars.Heat(value) : Bars.Usage(value);
        static string Text(CollapsedRow row, bool unit = true) => row.Value is { } value
            ? row.IsTemperature ? unit ? Degrees(value) : $"{Math.Round(value):0}°" : Percent(value)
            : row.Asleep ? Strings.Term_Idle.ToLower(Culture) : "—";
        static Segment Value(CollapsedRow row) => new(Text(row), InkOf(row), row.Value != null);

        return view switch
        {
            CardView.Htop => ([new(" ")], row =>
                [new(row.IsTemperature ? row.Label + "°" : row.Label, row.IsTemperature ? Ink.Yellow : Ink.Cyan) { ToolTip = row.Tip },
                    new("[", Ink.Fg, true), .. Bars.Htop(row.Value ?? 0, 6, Text(row, unit: false), InkOf(row)), new("]", Ink.Fg, true)]),
            CardView.Prompt => ([new("  ")], row =>
                [.. row.IsTemperature ? [row.Value != null ? new Segment("●", InkOf(row)) : new Segment("○", Ink.Dim), new(" ")] : new List<Segment>(),
                    new(row.Label.ToLower(Culture), Ink.Fg) { ToolTip = row.Tip }, new(" "), Value(row)]),
            CardView.Btop => ([new("  ")], row => row.IsTemperature
                ? [new(row.Label + "°", Ink.Fg, true) { ToolTip = row.Tip }, new(" "), Value(row)]
                : [new(row.Label, Ink.Fg, true) { ToolTip = row.Tip }, new(" "), .. Bars.Gradient(row.Value ?? 0, 4), new(" "), Value(row)]),
            CardView.Board => ([new("   ")], row => [new(row.Label, Ink.Dim) { ToolTip = row.Tip }, new(" "), Value(row)]),
            _ => ([new(" │ ", Ink.Dim)], row => [new(row.Label, Ink.Fg, true) { ToolTip = row.Tip }, new(" "), Value(row)])
        };
    }

    /// <summary>The first line of a header: the buttons go to its end in a top corner.</summary>
    private static List<Segment> HeaderLine(List<Segment> left, List<Segment> right, int columns, ButtonPlace buttons)
    {
        if (!buttons.Top)
            return Spread(left, right, columns);
        return buttons.Left
            ? Spread([Segment.ButtonSlot(), new(" "), .. left], right, columns)
            : Spread(left, [.. right, new(" "), Segment.ButtonSlot()], columns);
    }

    /// <summary>A command line tool's report: a header between two lines, a tree of rows, dotted bars, status badges.</summary>
    private static List<List<Segment>> Report(CardSnapshot card, int columns, ButtonPlace buttons)
    {
        var lines = new List<List<Segment>>();
        List<Segment> Rule() => [new(new string('─', columns), Ink.Dim)];
        void Head(string title)
        {
            lines.Add([]);
            lines.Add([new(title, Ink.Blue, true)]);
        }

        List<Segment> Branch(bool last) => [new(last ? "└─ " : "├─ ", Ink.Dim)];
        List<Segment> Dotted(double percent, int width, Ink ink) => Bars.Plain(percent, width, '⣿', '⣀', ink);

        lines.Add(Rule());
        lines.Add(HeaderLine([new(Strings.Name.ToUpper(Culture), Ink.Fg, true)], [new(card.Time, Ink.Fg, true)], columns, buttons));
        lines.Add(Spread([new(card.Host, Ink.Dim)], [new(card.Date, Ink.Dim)], columns));
        lines.Add(Rule());

        if (card.ShowDrives)
        {
            Head(Strings.Term_Disks);
            for (var i = 0; i < card.Drives.Count; i++)
            {
                var drive = card.Drives[i];
                lines.Add(Meter(columns, Branch(i == card.Drives.Count - 1), DriveName(drive.Name), 3, drive.Percent, Dotted,
                    Size(drive.Used, drive.Total), 13, drive.Name));
            }
        }

        if (card.ShowSystem)
        {
            Head(Strings.Term_System);
            var rows = new List<(string Label, double Percent, string Right, string? Tip)>
            {
                (Strings.Term_Cpu, card.Cpu, string.Empty, null),
                (Strings.Term_Ram, card.RamPercent, Size(card.RamUsed, card.RamTotal), null)
            };
            rows.AddRange(card.Gpus.Select(g => (Strings.Term_Gpu, g.Percent, Tag(g.Name), (string?)g.Name)));
            for (var i = 0; i < rows.Count; i++)
                lines.Add(Meter(columns, Branch(i == rows.Count - 1), rows[i].Label, 3, rows[i].Percent, Dotted, rows[i].Right, 13, rows[i].Tip));
        }

        if (card.ShowTemperatures)
        {
            Head(Strings.Term_Temperature);
            var badge = new[] { Strings.Term_Ok, Strings.Term_Warm, Strings.Term_Hot, Strings.Term_Idle, Strings.Term_NoValue }
                .Max(Cells.Width) + 4;
            var nameWidth = columns - 3 - badge - 1 - 1 - 5;
            for (var i = 0; i < card.Temperatures.Count; i++)
            {
                var temperature = card.Temperatures[i];
                var last = i == card.Temperatures.Count - 1;
                var stem = new Segment(last ? "   " : "│  ", Ink.Dim);
                Segment status;
                if (temperature.Value is { } value)
                {
                    var heat = Bars.Heat(value);
                    var (mark, text) = heat switch
                    {
                        Ink.Green => ("✓", Strings.Term_Ok),
                        Ink.Yellow => ("▲", Strings.Term_Warm),
                        _ => ("✖", Strings.Term_Hot)
                    };
                    status = new Segment(Cells.Fit($" {mark} {text}", badge), Ink.Fg, true) { Back = heat };
                }
                else
                {
                    status = temperature.Asleep
                        ? new Segment(Cells.Fit($" ○ {Strings.Term_Idle}", badge), Ink.Fg, true) { Back = Ink.Dim }
                        : new Segment(Cells.Fit($" ✖ {Strings.Term_NoValue}", badge), Ink.Fg, true) { Back = Ink.Red };
                }

                lines.Add([.. Branch(last), status, new(" "),
                    new(Cells.Fit(Short(temperature.Name), nameWidth), Ink.Fg, true) { ToolTip = temperature.Name }, new(" "),
                    Value(temperature, 5)]);
                if (HasRange(temperature))
                {
                    lines.Add([stem, new(new string(' ', badge + 1)),
                        .. Bars.Range(temperature.Value, temperature.Min!.Value, temperature.Max!.Value, columns - 3 - badge - 1 - 1 - 8),
                        new(" "), new(Cells.RFit(MinMax(temperature), 8), Ink.Dim)]);
                }

                foreach (var hint in Hints(temperature, columns - 6))
                    lines.Add([stem, new("   " + hint, Ink.Dim)]);
            }
        }

        return lines;
    }

    /// <summary>htop: meters in brackets with the value over the bar, no section titles.</summary>
    private static List<List<Segment>> Htop(CardSnapshot card, int columns, ButtonPlace buttons)
    {
        List<List<Segment>> lines =
        [
            HeaderLine([new(card.User, Ink.Green, true), new("@", Ink.Dim), new(card.Host, Ink.Blue, true)], [new(card.Time, Ink.Fg, true)], columns, buttons),
            Spread([new(card.Date, Ink.Dim)], [new(Strings.Name, Ink.Dim)], columns),
            []
        ];
        List<Segment> Meter(string label, Ink labelInk, double percent, string text, Ink ink, string? tip) =>
            [new(Cells.Fit(label, 5), labelInk) { ToolTip = tip }, new("[", Ink.Fg, true), .. Bars.Htop(percent, columns - 7, text, ink), new("]", Ink.Fg, true)];

        if (card.ShowDrives)
        {
            foreach (var drive in card.Drives)
                lines.Add(Meter(DriveName(drive.Name), Ink.Magenta, drive.Percent, Size(drive.Used, drive.Total), Bars.Usage(drive.Percent), drive.Name));
        }

        if (card.ShowSystem)
        {
            lines.Add(Meter(Strings.Term_Cpu, Ink.Cyan, card.Cpu, Percent(card.Cpu), Bars.Usage(card.Cpu), null));
            lines.Add(Meter(Strings.Term_Ram, Ink.Cyan, card.RamPercent, Size(card.RamUsed, card.RamTotal), Bars.Usage(card.RamPercent), null));
            for (var i = 0; i < card.Gpus.Count; i++)
            {
                var gpu = card.Gpus[i];
                var label = card.Gpus.Count > 1 ? Strings.Term_Gpu + (i + 1) : Strings.Term_Gpu;
                lines.Add(Meter(label, Ink.Cyan, gpu.Percent, $"{Tag(gpu.Name)} {Percent(gpu.Percent)}", Bars.Usage(gpu.Percent), gpu.Name));
            }
        }

        if (card.ShowTemperatures)
        {
            var cards = card.Temperatures.Count(t => !t.IsCpu);
            var number = 0;
            foreach (var temperature in card.Temperatures)
            {
                var label = (temperature.IsCpu ? Strings.Term_Cpu : cards > 1 ? Strings.Term_Gpu + ++number : Strings.Term_Gpu) + "°";
                var text = temperature.Value is { } value
                    ? HasRange(temperature) ? $"{MinMax(temperature)}  {Degrees(value)}" : Degrees(value)
                    : Missing(temperature).ToLower(Culture);
                lines.Add(Meter(label, Ink.Yellow, temperature.Value ?? 0, text,
                    temperature.Value is { } heat ? Bars.Heat(heat) : Ink.Dim, temperature.Name));
                foreach (var hint in Hints(temperature, columns - 6))
                    lines.Add([new("      " + hint, Ink.Dim)]);
            }
        }

        return lines;
    }

    /// <summary>A command line for the header, section icons, smooth bars of blocks, temperatures with a dotted leader.</summary>
    private static List<List<Segment>> Prompt(CardSnapshot card, int columns, ButtonPlace buttons)
    {
        List<List<Segment>> lines =
        [
            HeaderLine([new("❯ ", Ink.Magenta, true), new("sysmon", Ink.Green, true), new(" --watch", Ink.Dim)], [new(card.Time, Ink.Fg, true)], columns, buttons),
            [new("  " + card.Date, Ink.Dim)]
        ];
        void Head(string icon, string title) => lines.Add([new(icon), new(" "), new(title.ToLower(Culture), Ink.Fg, true)]);
        List<Segment> Indent() => [new("   ")];
        string Label(string text) => text.ToLower(Culture);

        if (card.ShowDrives)
        {
            Head("💾", Strings.Term_Disks);
            foreach (var drive in card.Drives)
                lines.Add(Meter(columns, Indent(), DriveName(drive.Name), 3, drive.Percent, Bars.Smooth, Size(drive.Used, drive.Total), 13, drive.Name));
        }

        if (card.ShowSystem)
        {
            Head("🧠", Strings.Term_System);
            lines.Add(Meter(columns, Indent(), Label(Strings.Term_Cpu), 3, card.Cpu, Bars.Smooth, string.Empty, 13));
            lines.Add(Meter(columns, Indent(), Label(Strings.Term_Ram), 3, card.RamPercent, Bars.Smooth, Size(card.RamUsed, card.RamTotal), 13));
            foreach (var gpu in card.Gpus)
                lines.Add(Meter(columns, Indent(), Label(Strings.Term_Gpu), 3, gpu.Percent, Bars.Smooth, Tag(gpu.Name), 13, gpu.Name));
        }

        if (card.ShowTemperatures)
        {
            Head("🌡️", Strings.Term_Temperature);
            foreach (var temperature in card.Temperatures)
            {
                var range = HasRange(temperature) ? MinMax(temperature) + " " : string.Empty;
                var name = Short(temperature.Name);
                name = Cells.Fit(name, Math.Min(Cells.Width(name), columns - 13 - Cells.Width(range))).TrimEnd() + " ";
                var dot = temperature.Value is { } value ? new Segment("●", Bars.Heat(value)) : new Segment("○", Ink.Dim);
                var text = temperature.Value is { } degrees ? Degrees(degrees) : Missing(temperature).ToLower(Culture);
                lines.Add([new("   "), dot, new(" "), new(name, Ink.Fg) { ToolTip = temperature.Name },
                    new(new string('·', Math.Max(1, columns - 5 - Cells.Width(name) - 6 - Cells.Width(range))), Ink.Dim),
                    new(range.Length > 0 ? " " + range.TrimEnd() : string.Empty, Ink.Dim),
                    new(Cells.RFit(text, 6), temperature.Value is { } heat ? Bars.Heat(heat) : Ink.Dim, temperature.Value != null)]);
                foreach (var hint in Hints(temperature, columns - 5))
                    lines.Add([new("     " + hint, Ink.Dim)]);
            }
        }

        return lines;
    }

    /// <summary>btop: sections in rounded frames with the title on the line, gradient bars, recent values as sparklines.</summary>
    private static List<List<Segment>> Btop(CardSnapshot card, int columns, ButtonPlace buttons)
    {
        var inner = columns - 4;
        var lines = new List<List<Segment>>
        {
            HeaderLine([new("▌", Ink.Magenta), new("sysmon", Ink.Fg, true), new("  " + card.Date, Ink.Dim)], [new(card.Time, Ink.Fg, true)], columns, buttons)
        };
        void Box(string title, List<List<Segment>> rows)
        {
            title = title.ToLower(Culture);
            lines.Add([new("╭─", Ink.Dim), new(title, Ink.Cyan, true), new(new string('─', Math.Max(0, columns - 3 - Cells.Width(title))), Ink.Dim), new("╮", Ink.Dim)]);
            foreach (var row in rows)
                lines.Add([new("│ ", Ink.Dim), .. row, new(new string(' ', Math.Max(0, inner - Cells.Width(row)))), new(" │", Ink.Dim)]);
            lines.Add([new("╰" + new string('─', columns - 2) + "╯", Ink.Dim)]);
        }

        List<Segment> Row(string label, string? tip, double percent, List<Segment> right) =>
            [new(Cells.Fit(label, 3), Ink.Fg, true) { ToolTip = tip }, new(" "), .. Bars.Gradient(percent, inner - 20), new(" "),
                new(Percent4(percent), Bars.Usage(percent), true), new(" "), .. right];

        if (card.ShowDrives)
            Box(Strings.Term_Disks, card.Drives.Select(d => Row(DriveName(d.Name), d.Name, d.Percent, [new(Cells.RFit(Size(d.Used, d.Total), 10), Ink.Dim)])).ToList());

        if (card.ShowSystem)
        {
            var rows = new List<List<Segment>>
            {
                Row(Strings.Term_Cpu, null, card.Cpu, Bars.Spark(card.CpuHistory, 10, 0, 100, Bars.Usage)),
                Row(Strings.Term_Ram, null, card.RamPercent, [new(Cells.RFit(Size(card.RamUsed, card.RamTotal), 10), Ink.Dim)])
            };
            rows.AddRange(card.Gpus.Select(g => Row(Strings.Term_Gpu, g.Name, g.Percent, [new(Cells.RFit(Tag(g.Name), 10), Ink.Dim)])));
            Box(Strings.Term_System, rows);
        }

        if (card.ShowTemperatures)
        {
            var rows = new List<List<Segment>>();
            foreach (var temperature in card.Temperatures)
            {
                var value = Value(temperature, 5);
                // Without a value the line seen so far stays, dimmed.
                List<Segment> Spark(int width) => temperature.History.Count == 0
                    ? [new(new string('·', width), Ink.Dim)]
                    : Bars.Spark(temperature.History, width, temperature.History.Min() - 1, temperature.History.Max() + 1,
                        temperature.Value == null ? _ => Ink.Dim : Bars.Heat);
                if (HasRange(temperature))
                {
                    rows.Add([new(Cells.Fit(Short(temperature.Name), inner - 6), Ink.Fg) { ToolTip = temperature.Name }, new(" "), value]);
                    rows.Add([new("  "), .. Spark(inner - 11), new(" "), new(Cells.RFit(MinMax(temperature), 8), Ink.Dim)]);
                }
                else
                {
                    rows.Add([new(Cells.Fit(Short(temperature.Name), inner - 15), Ink.Fg) { ToolTip = temperature.Name }, new(" "), .. Spark(8), new(" "), value]);
                }

                foreach (var hint in Hints(temperature, inner - 2))
                    rows.Add([new("  " + hint, Ink.Dim)]);
            }

            Box(Strings.Term_Temperature, rows);
        }

        return lines;
    }

    /// <summary>A departure board: a device with its load and temperature in one row, the drives in a table of their own.</summary>
    private static List<List<Segment>> Board(CardSnapshot card, int columns, ButtonPlace buttons)
    {
        var nameWidth = columns - 23;
        var lines = new List<List<Segment>>
        {
            HeaderLine([new(Strings.Name.ToUpper(Culture), Ink.Cyan, true)], [new(card.Time, Ink.Fg, true)], columns, buttons),
            Spread([new(card.Host, Ink.Dim)], [new(card.Date.ToUpper(Culture), Ink.Dim)], columns)
        };
        Segment Heading(string text, int width, bool right = false) =>
            new(right ? Cells.RFit(text, width) : Cells.Fit(text, width), Ink.Dim, true) { Underline = text.Length > 0 };
        void Header(string device, string load, string last)
        {
            lines.Add([]);
            lines.Add([Heading(device, nameWidth), new(" "), Heading(load, 11), new(" "), Heading(last, 10, right: true)]);
        }

        void Row(string name, string? tip, double? percent, List<Segment> last)
        {
            var line = new List<Segment> { new(Cells.Fit(name, nameWidth), Ink.Fg) { ToolTip = tip }, new(" ") };
            if (percent is { } value)
                line.AddRange([.. Bars.Plain(value, 6, '▰', '▱', Bars.Usage(value)), new(" "), new(Percent4(value), Bars.Usage(value), true)]);
            else
                line.Add(new(new string(' ', 11)));
            line.Add(new(" "));
            line.AddRange(last);
            lines.Add(line);
        }

        List<Segment> Text(string text, Ink ink = Ink.Dim) => [new(Cells.RFit(text, 10), ink)];
        var notes = new List<string>();
        List<Segment> Temperature(TemperatureRow? temperature)
        {
            if (temperature == null)
                return Text(string.Empty);
            if (temperature.Value is not { } value)
            {
                if (temperature.Asleep)
                    return Text(Strings.Term_Idle.ToLower(Culture));
                notes.Add(temperature.Hint ?? Strings.Term_NoValue);
                return Text("—" + new string('*', notes.Count));
            }

            if (HasRange(temperature))
            {
                return [new(Cells.RFit($"{Math.Round(value):0}°", 4), Bars.Heat(value), true),
                    new(Cells.RFit($"{Math.Round(temperature.Min!.Value):0}‥{Math.Round(temperature.Max!.Value):0}", 6), Ink.Dim)];
            }

            return Text($"{Math.Round(value):0}°", Bars.Heat(value));
        }

        if (card.ShowSystem || card.ShowTemperatures)
        {
            Header(Strings.Term_Device, card.ShowSystem ? Strings.Term_Load : string.Empty, card.ShowTemperatures ? Strings.Term_Temp : string.Empty);
            var cpu = card.Temperatures.FirstOrDefault(t => t.IsCpu);
            Row(cpu != null ? Short(cpu.Name) : Strings.Cpu, cpu?.Name, card.ShowSystem ? card.Cpu : null,
                card.ShowTemperatures ? Temperature(cpu) : Text(string.Empty));
            var cards = card.Gpus.Select(g => g.Name)
                .Concat(card.Temperatures.Where(t => !t.IsCpu).Select(t => t.Name))
                .Distinct();
            foreach (var name in cards)
            {
                var load = card.ShowSystem ? card.Gpus.FirstOrDefault(g => g.Name == name)?.Percent : null;
                var temperature = card.Temperatures.FirstOrDefault(t => !t.IsCpu && t.Name == name);
                Row(Tag(name), name, load, card.ShowTemperatures ? Temperature(temperature) : Text(string.Empty));
            }

            if (card.ShowSystem)
                Row(Strings.Term_Memory, null, card.RamPercent, Text(Size(card.RamUsed, card.RamTotal)));
        }

        if (card.ShowDrives)
        {
            Header(Strings.Term_Disk, Strings.Term_Used, Strings.Term_Size);
            foreach (var drive in card.Drives)
                Row(DriveName(drive.Name), drive.Name, drive.Percent, Text(Size(drive.Used, drive.Total)));
        }

        for (var i = 0; i < notes.Count; i++)
        {
            var wrapped = Cells.Wrap(new string('*', i + 1) + " " + notes[i], columns - 2);
            for (var j = 0; j < wrapped.Count; j++)
                lines.Add([new((j > 0 ? "  " : string.Empty) + wrapped[j], Ink.Dim)]);
        }

        return lines;
    }

    /// <summary>The left and the right part of a line, apart by the rest of the width; the left one is cut when both do not fit.</summary>
    private static List<Segment> Spread(List<Segment> left, List<Segment> right, int columns)
    {
        var gap = columns - Cells.Width(left) - Cells.Width(right);
        if (gap < 1 && left.Count > 0)
        {
            var last = left[^1];
            left = [.. left.Take(left.Count - 1), last with { Text = Cells.Fit(last.Text, Math.Max(0, Cells.Width(last.Text) + gap - 1)) }];
            gap = columns - Cells.Width(left) - Cells.Width(right);
        }

        return [.. left, new(new string(' ', Math.Max(1, gap))), .. right];
    }

    /// <summary>A label, a bar, the percent and a right column; the bars of all such rows end in one column.</summary>
    private static List<Segment> Meter(int columns, List<Segment> prefix, string label, int labelWidth, double percent,
        Func<double, int, Ink, List<Segment>> bar, string right, int rightWidth, string? tip = null)
    {
        var ink = Bars.Usage(percent);
        var width = Math.Max(3, columns - Cells.Width(prefix) - labelWidth - 1 - 1 - 4 - (rightWidth > 0 ? rightWidth + 1 : 0));
        var line = new List<Segment>(prefix) { new(Cells.Fit(label, labelWidth), Ink.Fg, true) { ToolTip = tip }, new(" ") };
        line.AddRange(bar(percent, width, ink));
        line.Add(new(" "));
        line.Add(new(Percent4(percent), ink, true));
        if (rightWidth > 0)
        {
            line.Add(new(" "));
            line.Add(new(Cells.RFit(right, rightWidth), Ink.Dim));
        }

        return line;
    }

    private static Segment Value(TemperatureRow temperature, int width) => temperature.Value is { } value
        ? new(Cells.RFit(Degrees(value), width), Bars.Heat(value), true)
        : new(Cells.RFit(temperature.Asleep ? Strings.Term_Idle.ToLower(Culture) : "—", width), Ink.Dim);

    /// <summary>Why there is no temperature, under the row; a card asleep says it by its badge.</summary>
    private static List<string> Hints(TemperatureRow temperature, int width) =>
        temperature.Value == null && !temperature.Asleep && temperature.Hint != null ? Cells.Wrap(temperature.Hint, width) : [];

    private static string Missing(TemperatureRow temperature) => temperature.Asleep ? Strings.Term_Idle : Strings.Term_NoValue;

    /// <summary>
    /// A temperature has been seen since the card started. Its range stays when the value goes (a card asleep,
    /// a sensor that did not answer), dimmed, so the rows under it do not jump up and down.
    /// </summary>
    private static bool HasRange(TemperatureRow temperature) => temperature is { Min: not null, Max: not null };

    private static string MinMax(TemperatureRow temperature) =>
        $"↓{Math.Round(temperature.Min!.Value):0} ↑{Math.Round(temperature.Max!.Value):0}";

    private static string Degrees(double celsius) => $"{Math.Round(celsius):0}°C";

    private static string Percent(double percent) => $"{Math.Round(percent):0}%";

    private static string Percent4(double percent) => Cells.RFit(Percent(percent), 4);

    private static string Gigabytes(double value) => value >= 100 ? value.ToString("0", Culture) : value.ToString("0.0", Culture);

    private static string Size(double used, double total) => string.Format(Culture, Strings.Format_TermSize, Gigabytes(used), Gigabytes(total));

    private static string DriveName(string name) => DeviceNames.Drive(name);

    private static string Short(string name) => DeviceNames.Short(name);

    private static string Tag(string name) => DeviceNames.Tag(name);
}
