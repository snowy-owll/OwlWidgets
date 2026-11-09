using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OwlWidgets.UI.Common;

/// <summary>
/// Lets a text box take only a number: digits, a leading minus when <see cref="AllowNegativeProperty"/> is set,
/// and one decimal separator of the current culture when <see cref="AllowFractionProperty"/> is set.
/// Typed and pasted text is checked against what the box would hold after it; an incomplete number
/// such as "-" or "12," stays possible while typing, so the owner still parses the text.
/// </summary>
public static class NumericInput
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(NumericInput), new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty AllowNegativeProperty = DependencyProperty.RegisterAttached(
        "AllowNegative", typeof(bool), typeof(NumericInput), new PropertyMetadata(true));

    public static readonly DependencyProperty AllowFractionProperty = DependencyProperty.RegisterAttached(
        "AllowFraction", typeof(bool), typeof(NumericInput), new PropertyMetadata(true));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static bool GetAllowNegative(DependencyObject element) => (bool)element.GetValue(AllowNegativeProperty);

    public static void SetAllowNegative(DependencyObject element, bool value) => element.SetValue(AllowNegativeProperty, value);

    public static bool GetAllowFraction(DependencyObject element) => (bool)element.GetValue(AllowFractionProperty);

    public static void SetAllowFraction(DependencyObject element, bool value) => element.SetValue(AllowFractionProperty, value);

    private static void OnIsEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not TextBox box)
            return;

        if ((bool)e.NewValue)
        {
            box.PreviewTextInput += OnPreviewTextInput;
            box.PreviewKeyDown += OnPreviewKeyDown;
            DataObject.AddPastingHandler(box, OnPasting);
        }
        else
        {
            box.PreviewTextInput -= OnPreviewTextInput;
            box.PreviewKeyDown -= OnPreviewKeyDown;
            DataObject.RemovePastingHandler(box, OnPasting);
        }
    }

    private static void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var box = (TextBox)sender;
        if (!IsAllowed(box, Compose(box, e.Text)))
            e.Handled = true;
    }

    /// <summary>A space does not come through text input.</summary>
    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
            e.Handled = true;
    }

    private static void OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        var box = (TextBox)sender;
        if (e.DataObject.GetData(DataFormats.UnicodeText) is not string text
            || !IsAllowed(box, Compose(box, text.Trim())))
            e.CancelCommand();
    }

    /// <summary>The text the box would hold with the input in place of the selection.</summary>
    private static string Compose(TextBox box, string input) =>
        box.Text.Remove(box.SelectionStart, box.SelectionLength).Insert(box.SelectionStart, input);

    private static bool IsAllowed(TextBox box, string text)
    {
        var format = CultureInfo.CurrentCulture.NumberFormat;
        var minus = GetAllowNegative(box) ? $"(?:{Regex.Escape(format.NegativeSign)})?" : string.Empty;
        // [0-9], not \d: \d also takes digits of other scripts.
        var fraction = GetAllowFraction(box) ? $"(?:{Regex.Escape(format.NumberDecimalSeparator)}[0-9]*)?" : string.Empty;
        return Regex.IsMatch(text, $"^{minus}[0-9]*{fraction}$");
    }
}
