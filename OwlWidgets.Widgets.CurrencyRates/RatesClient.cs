using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace OwlWidgets.Widgets.CurrencyRates;

/// <summary>One rate of a day: how much of <see cref="Quote"/> one unit of <see cref="Base"/> costs.</summary>
internal sealed record RatePoint(DateOnly Date, string Base, string Quote, decimal Rate);

/// <summary>
/// Frankfurter API v2: <c>GET /v2/rates?base={base}&amp;quotes={a,b}&amp;from={yyyy-MM-dd}</c> returns every day
/// from <c>from</c> to the latest as <c>[{"date","base","quote","rate"}]</c>.
/// </summary>
internal static class RatesClient
{
    private const string Endpoint = "https://api.frankfurter.dev/v2/rates";

    private static readonly HttpClient Http = CreateClient();

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<IReadOnlyList<RatePoint>> GetAsync(string @base, IEnumerable<string> quotes, DateOnly from,
        CancellationToken token)
    {
        var url = $"{Endpoint}?base={Uri.EscapeDataString(@base)}"
            + $"&quotes={string.Join(',', quotes.Select(Uri.EscapeDataString))}"
            + $"&from={from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
        using var response = await Http.GetAsync(url, token);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(token);
            throw new HttpRequestException($"Frankfurter answered {(int)response.StatusCode} to {url}: {body}");
        }

        var points = await response.Content.ReadFromJsonAsync<List<Point>>(Json, token) ?? [];
        return points
            .Select(point => new RatePoint(
                DateOnly.ParseExact(point.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture), point.Base, point.Quote, point.Rate))
            .ToArray();
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OwlWidgets-CurrencyRates");
        return client;
    }

    private sealed class Point
    {
        public string Date { get; set; } = string.Empty;

        public string Base { get; set; } = string.Empty;

        public string Quote { get; set; } = string.Empty;

        public decimal Rate { get; set; }
    }
}
