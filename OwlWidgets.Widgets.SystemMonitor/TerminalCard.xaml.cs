using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using OwlWidgets.Widgets.SystemMonitor.Terminal;
using OwlWidgets.Widgets.SystemMonitor.ViewModels;

namespace OwlWidgets.Widgets.SystemMonitor;

/// <summary>
/// The card in one of the terminal styles: the model drawn as lines of characters by <see cref="TerminalLayouts"/>,
/// the whole card and the collapsed one. The model sets the clock after every reading, so the card is drawn once
/// a second, whatever changed. The recent values for the sparklines are kept here, since the card started.
/// </summary>
public partial class TerminalCard : UserControl
{
    private const int Columns = 40;
    private const int HistoryLength = 40;

    private readonly Dictionary<string, List<double>> _history = [];
    private readonly CollapseAnimation _collapse;
    private ContentViewModel? _model;

    public TerminalCard()
    {
        InitializeComponent();
        _collapse = new CollapseAnimation(FullCard, CollapsedCard);
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>Any style but <see cref="CardView.Classic"/>; set once, before the card is shown.</summary>
    internal CardView Layout { get; init; } = CardView.Report;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_model != null)
            _model.PropertyChanged -= OnModelChanged;
        _model = DataContext as ContentViewModel;
        _history.Clear();
        if (_model == null)
            return;
        _model.PropertyChanged += OnModelChanged;
        Draw(record: false);
        _collapse.Apply(_model.IsCollapsed, _model.ButtonsAtTop, animate: false);
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_model is not { } model)
            return;
        switch (e.PropertyName)
        {
            case nameof(ContentViewModel.CurrentTime):
                Draw(record: true);
                break;
            // The place of the buttons moves with their corner; the collapsed card shows its values at once.
            case nameof(ContentViewModel.ButtonCorner):
                Draw(record: false);
                _collapse.Apply(model.IsCollapsed, model.ButtonsAtTop, animate: false);
                break;
            case nameof(ContentViewModel.IsCollapsed):
                Draw(record: false);
                _collapse.Apply(model.IsCollapsed, model.ButtonsAtTop, animate: IsLoaded);
                break;
        }
    }

    private void Draw(bool record)
    {
        if (_model is not { } model)
            return;

        if (record)
        {
            Record("cpu", model.CpuUsagePercentage);
            foreach (var tile in model.Temperatures)
            {
                if (tile.Temperature is { } value)
                    Record("t:" + tile.Key, value);
            }
        }

        var card = new CardSnapshot(
            model.CurrentTime ?? string.Empty,
            model.CurrentDate ?? string.Empty,
            Environment.MachineName,
            Environment.UserName.ToLowerInvariant(),
            model.ShowDrives,
            model.ShowSystem,
            model.ShowTemperatures,
            model.Drives.Select(d => new DriveRow(d.Name, d.UsedSpace, d.TotalSpace, d.UsagePercentage)).ToList(),
            model.CpuUsagePercentage,
            History("cpu"),
            model.UsedRam,
            model.TotalRam,
            model.RamUsagePercentage,
            model.GpuUsage.Select(g => new GpuRow(g.Name, g.UsagePercentage)).ToList(),
            model.Temperatures.Select(t => new TemperatureRow(t.Name, t.IsCpu, t.Temperature, t.TemperatureMin, t.TemperatureMax,
                t.Hint, t.IsAsleep, History("t:" + t.Key))).ToList());
        var buttons = new ButtonPlace(model.ButtonsAtTop, model.ButtonsAtLeft);
        Show(FullGrid, FullButtons, TerminalLayouts.Render(Layout, card, Columns, buttons));
        var values = model.CollapsedValues.Select(v => new CollapsedRow(v.Label, v.IsTemperature, v.Value, v.IsAsleep, v.ToolTip)).ToList();
        Show(CollapsedGrid, CollapsedButtons, TerminalLayouts.RenderCollapsed(Layout, values, Columns, buttons));
    }

    /// <summary>The lines on the grid, and the buttons over the cells of their slot.</summary>
    private static void Show(TerminalGrid grid, FrameworkElement buttons, List<List<Segment>> lines)
    {
        grid.Show(lines, Columns);
        var cell = grid.Cell;
        for (var row = 0; row < lines.Count; row++)
        {
            var column = 0;
            foreach (var segment in lines[row])
            {
                if (segment.Slot)
                {
                    buttons.Margin = new Thickness(column * cell.Width, row * cell.Height, 0, 0);
                    buttons.Width = Cells.Width(segment.Text) * cell.Width;
                    buttons.Height = cell.Height;
                    return;
                }

                column += Cells.Width(segment.Text);
            }
        }
    }

    private void Record(string key, double value)
    {
        if (!_history.TryGetValue(key, out var values))
            _history[key] = values = [];
        values.Add(value);
        if (values.Count > HistoryLength)
            values.RemoveAt(0);
    }

    private IReadOnlyList<double> History(string key) => _history.TryGetValue(key, out var values) ? values.ToArray() : [];
}
