namespace OwlWidgets.Widgets.CurrencyRates;

public class Config
{
    public const int MaxPairs = 10;

    /// <summary>Minutes.</summary>
    public const int DefaultUpdateInterval = 60;

    /// <summary>Minutes.</summary>
    public const int MinUpdateInterval = 5;

    /// <summary>Minutes between updates.</summary>
    public int UpdateInterval { get; set; } = DefaultUpdateInterval;

    public List<CurrencyPair> Pairs { get; set; } = [];

    public sealed class CurrencyPair
    {
        /// <summary>ISO 4217 code of the currency being priced.</summary>
        public string Base { get; set; } = "USD";

        /// <summary>ISO 4217 code of the currency the price is given in.</summary>
        public string Quote { get; set; } = "EUR";

        /// <summary>How many units of <see cref="Base"/> are priced, for example 100 JPY.</summary>
        public decimal Amount { get; set; } = 1;
    }
}
