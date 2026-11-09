using System.Collections.Concurrent;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OwlWidgets.Widgets.CurrencyRates.Properties;

namespace OwlWidgets.Widgets.CurrencyRates;

/// <summary>
/// A currency Frankfurter knows. <see cref="FlagCode"/> null: no flag, the symbol is shown instead.
/// </summary>
public sealed record Currency(string Code, string Symbol, string? FlagCode)
{
    /// <summary>The name in the UI language; the code when the resources have none.</summary>
    public string Name => Strings.ResourceManager.GetString("Currency_" + Code, Strings.Culture) ?? Code;

    public ImageSource? Flag => CurrencyCatalog.Flag(FlagCode);

    public string Title => $"{Code} · {Name}";
}

/// <summary>
/// Currencies of the source with their symbols and flags. Shared by the settings screen and the runtime,
/// so the flags are embedded in this assembly and read from its manifest, not by a pack URI.
/// </summary>
public static partial class CurrencyCatalog
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Flags = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Currency[] Known = CreateKnown();
    private static readonly Dictionary<string, Currency> ByCode = Known.ToDictionary(currency => currency.Code, StringComparer.OrdinalIgnoreCase);

    /// <summary>Sorted by name in the UI language.</summary>
    public static IReadOnlyList<Currency> All { get; } =
        Known.OrderBy(currency => currency.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();

    /// <summary>
    /// A code missing from the catalog still works with the source: it gets a currency without a flag
    /// whose symbol is the code.
    /// </summary>
    public static Currency Find(string code) =>
        ByCode.TryGetValue(code, out var currency) ? currency : new Currency(code.ToUpperInvariant(), code.ToUpperInvariant(), null);

    public static bool Contains(string code) => ByCode.ContainsKey(code);

    internal static ImageSource? Flag(string? code) =>
        code == null ? null : Flags.GetOrAdd(code, Load);

    private static ImageSource? Load(string code)
    {
        using var stream = typeof(CurrencyCatalog).Assembly.GetManifestResourceStream($"Flags.{code}.png");
        if (stream == null)
            return null;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
