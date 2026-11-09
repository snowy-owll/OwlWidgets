using System.Net.Http;

namespace OwlWidgets.Widgets.Weather.Providers;

internal static class WeatherHttp
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static async Task<string> GetStringAsync(string url, CancellationToken token)
    {
        using var response = await Client.GetAsync(url, token);
        // The message holds the status code only: a URL with a key stays out of the log.
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(token);
    }
}
