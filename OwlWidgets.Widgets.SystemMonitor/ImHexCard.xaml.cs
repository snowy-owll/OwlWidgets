using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using OwlWidgets.Widgets.SystemMonitor.ViewModels;

namespace OwlWidgets.Widgets.SystemMonitor;

/// <summary>
/// The card in the style of ImHex: the widgets of Dear ImGui drawn with WPF. The plot of the processor load is drawn
/// here from the values of the last 40 seconds; the model sets the clock after every reading, so a value is taken then.
/// </summary>
public partial class ImHexCard : UserControl
{
    private const int HistoryLength = 40;

    private readonly List<double> _cpu = [];
    private readonly CollapseAnimation _collapse;
    private ContentViewModel? _model;

    public ImHexCard()
    {
        InitializeComponent();
        _collapse = new CollapseAnimation(FullCard, CollapsedCard);
        // Numbers by the regional settings: a binding formats with the language of the element, en-US by default.
        Language = XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);
        DataContextChanged += OnDataContextChanged;
        Plot.SizeChanged += (_, _) => DrawPlot();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_model != null)
            _model.PropertyChanged -= OnModelChanged;
        _model = DataContext as ContentViewModel;
        _cpu.Clear();
        if (_model != null)
        {
            _model.PropertyChanged += OnModelChanged;
            _collapse.Apply(_model.IsCollapsed, _model.ButtonsAtTop, animate: false);
        }

        DrawPlot();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_model == null)
            return;
        if (e.PropertyName == nameof(ContentViewModel.IsCollapsed))
            _collapse.Apply(_model.IsCollapsed, _model.ButtonsAtTop, animate: IsLoaded);
        else if (e.PropertyName == nameof(ContentViewModel.ButtonCorner))
            _collapse.Apply(_model.IsCollapsed, _model.ButtonsAtTop, animate: false);
        if (e.PropertyName != nameof(ContentViewModel.CurrentTime))
            return;
        _cpu.Add(_model.CpuUsagePercentage);
        if (_cpu.Count > HistoryLength)
            _cpu.RemoveAt(0);
        DrawPlot();
    }

    /// <summary>0…100 % from the bottom to the top; the newest value at the right edge.</summary>
    private void DrawPlot()
    {
        var points = new PointCollection();
        double width = Plot.ActualWidth, height = Plot.ActualHeight;
        if (_cpu.Count > 1 && width > 0)
        {
            var step = width / (HistoryLength - 1);
            var left = width - (_cpu.Count - 1) * step;
            for (var i = 0; i < _cpu.Count; i++)
                points.Add(new Point(left + i * step, 1 + (height - 2) * (1 - Math.Clamp(_cpu[i], 0, 100) / 100)));
        }

        PlotLine.Points = points;
    }
}
