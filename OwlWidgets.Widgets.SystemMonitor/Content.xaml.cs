using System.ComponentModel;
using System.Windows.Controls;
using OwlWidgets.Widgets.SystemMonitor.ViewModels;

namespace OwlWidgets.Widgets.SystemMonitor;

/// <summary>The card in the classic style; only the animation between the whole card and the collapsed one is here.</summary>
public partial class Content : UserControl
{
    private readonly CollapseAnimation _collapse;
    private ContentViewModel? _model;

    public Content()
    {
        InitializeComponent();
        _collapse = new CollapseAnimation(FullCard, CollapsedCard);
        DataContextChanged += (_, _) =>
        {
            if (_model != null)
                _model.PropertyChanged -= OnModelChanged;
            _model = DataContext as ContentViewModel;
            if (_model == null)
                return;
            _model.PropertyChanged += OnModelChanged;
            _collapse.Apply(_model.IsCollapsed, _model.ButtonsAtTop, animate: false);
        };
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_model == null)
            return;
        if (e.PropertyName == nameof(ContentViewModel.IsCollapsed))
            _collapse.Apply(_model.IsCollapsed, _model.ButtonsAtTop, animate: IsLoaded);
        else if (e.PropertyName == nameof(ContentViewModel.ButtonCorner))
            _collapse.Apply(_model.IsCollapsed, _model.ButtonsAtTop, animate: false);
    }
}
