using System.Windows;
using System.Windows.Controls;
using OwlWidgets.Contracts;

namespace OwlWidgets.UI.Common;

/// <summary>
/// Picks a corner of a card: a rectangle with a radio button on each corner. <see cref="Corner"/> binds two-way
/// by default; <see cref="CornerChanged"/> serves a screen built in code. The radio buttons share the parent grid,
/// so they form a group of their own without a group name, and two pickers on one screen do not interfere.
/// </summary>
public partial class CornerPicker : UserControl
{
    public static readonly DependencyProperty CornerProperty = DependencyProperty.Register(
        nameof(Corner), typeof(CardCorner), typeof(CornerPicker),
        new FrameworkPropertyMetadata(CardCorner.TopLeft, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnCornerChanged));

    public CornerPicker()
    {
        InitializeComponent();
        Show(Corner);
    }

    public CardCorner Corner
    {
        get => (CardCorner)GetValue(CornerProperty);
        set => SetValue(CornerProperty, value);
    }

    public event EventHandler? CornerChanged;

    private static void OnCornerChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        var picker = (CornerPicker)element;
        picker.Show((CardCorner)e.NewValue);
        picker.CornerChanged?.Invoke(picker, EventArgs.Empty);
    }

    private void Show(CardCorner corner)
    {
        TopLeft.IsChecked = corner == CardCorner.TopLeft;
        TopRight.IsChecked = corner == CardCorner.TopRight;
        BottomLeft.IsChecked = corner == CardCorner.BottomLeft;
        BottomRight.IsChecked = corner == CardCorner.BottomRight;
    }

    private void OnChecked(object sender, RoutedEventArgs e)
    {
        Corner = sender == TopRight ? CardCorner.TopRight
            : sender == BottomLeft ? CardCorner.BottomLeft
            : sender == BottomRight ? CardCorner.BottomRight
            : CardCorner.TopLeft;
    }
}
