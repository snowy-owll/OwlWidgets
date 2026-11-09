using System.Globalization;
using System.Windows.Media;
using OwlWidgets.Common;

namespace OwlWidgets.Widgets.CurrencyRates.ViewModels;

/// <summary>One pair on the card: the price of <see cref="Config.CurrencyPair.Amount"/> units of the base currency.</summary>
internal sealed class RateViewModel : ViewModelBase
{
    private const string NoValue = "—";

    public RateViewModel(Config.CurrencyPair pair)
    {
        Pair = pair;
        var from = CurrencyCatalog.Find(pair.Base);
        var to = CurrencyCatalog.Find(pair.Quote);
        Title = pair.Amount == 1 ? from.Code : $"{pair.Amount.ToString("0.##", CultureInfo.CurrentCulture)} {from.Code}";
        QuoteCode = to.Code;
        Name = from.Name;
        Flag = from.Flag;
        Symbol = from.Symbol;
        _quoteSymbol = to.Symbol;
        Value = NoValue;
    }

    public Config.CurrencyPair Pair { get; }

    /// <summary>The base code, with the amount when it is not 1.</summary>
    public string Title { get; }

    public string QuoteCode { get; }

    /// <summary>Name of the base currency.</summary>
    public string Name { get; }

    /// <summary>Flag of the base currency; null: the symbol is shown in its place.</summary>
    public ImageSource? Flag { get; }

    public bool HasFlag => Flag != null;

    /// <summary>Symbol of the base currency.</summary>
    public string Symbol { get; }

    public string Value
    {
        get => GetValue(() => Value) ?? NoValue;
        set => SetValue(() => Value, value);
    }

    public string? Change
    {
        get => GetValue(() => Change);
        set => SetValue(() => Change, value);
    }

    /// <summary>1: up since the previous day, -1: down, 0: the same, null: unknown.</summary>
    public int? Direction
    {
        get => GetValue(() => Direction);
        set => SetValue(() => Direction, value);
    }

    private readonly string _quoteSymbol;

    /// <param name="previous">The rate of the day before <paramref name="current"/>; null: none in the answer.</param>
    public void Show(decimal current, decimal? previous)
    {
        Value = $"{Format(current * Pair.Amount)} {_quoteSymbol}";
        if (previous is not { } before || before == 0)
        {
            Change = null;
            Direction = null;
            return;
        }

        var percent = Math.Round((current - before) / before * 100, 2);
        Direction = Math.Sign(percent);
        var text = (Math.Abs(percent) / 100).ToString("P2", CultureInfo.CurrentCulture);
        Change = Direction switch
        {
            > 0 => "▲ " + text,
            < 0 => "▼ " + text,
            _ => text
        };
    }

    /// <summary>About four significant digits, never fewer than two decimals.</summary>
    private static string Format(decimal value)
    {
        var magnitude = value <= 0 ? 0 : (int)Math.Floor(Math.Log10((double)value));
        var decimals = Math.Clamp(3 - magnitude, 2, 8);
        return value.ToString("N" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.CurrentCulture);
    }
}
