using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using OwlWidgets.Widgets.InternetSpeed.Measurement;

namespace OwlWidgets.Widgets.InternetSpeed.Providers;

/// <summary>
/// speed.cloudflare.com, the service behind Cloudflare's own speed test page: <c>GET /__down?bytes={n}</c> returns n bytes,
/// <c>POST /__up</c> takes a body of any size. Anycast leads to the nearest data center; its code comes in the <c>colo</c>
/// header. Several HTTP/1.1 connections at once, as the speed test of Cloudflare and Speedtest do.
/// </summary>
internal sealed partial class CloudflareProvider : ISpeedTestProvider
{
    private const string Endpoint = "https://speed.cloudflare.com/";

    /// <summary>A download request; the service refuses 100 MB and more. A request is cut when its phase ends.</summary>
    private const long DownloadBytes = 25_000_000;

    private const long UploadBytes = 10_000_000;

    /// <summary>Requests of the ping phase; the first one opens the connection and is not counted.</summary>
    private const int LatencyRequests = 21;

    /// <summary>The ping phase ends after this time once it has these many round trips.</summary>
    private static readonly TimeSpan LatencyTime = TimeSpan.FromSeconds(4);

    private const int MinLatencySamples = 3;

    public string Name => "Cloudflare";

    public async Task<ISpeedTestSession> ConnectAsync(SpeedTestOptions options, CancellationToken token)
    {
        var sockets = new TestSockets();
        var handler = new SocketsHttpHandler
        {
            // One more for the ping requests, which do not wait for the transfers.
            MaxConnectionsPerServer = options.Streams + 1,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            ConnectCallback = sockets.ConnectAsync,
            UseCookies = false
        };
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OwlWidgets-InternetSpeed");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var response = await client.GetAsync(Endpoint + "__down?bytes=0", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            var colo = Header(response.Headers, "colo") ?? "?";
            return new Session(client, sockets, options.Streams, colo);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static string? Header(HttpResponseHeaders headers, string name) =>
        headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    /// <summary>
    /// Time the server spent on a request, milliseconds: the largest <c>dur</c> of <c>Server-Timing</c>,
    /// e.g. <c>cfSpeedEdge;dur=2, cfSpeedWorker;dur=14</c>. It is taken off the round trip.
    /// </summary>
    private static double ServerTime(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Server-Timing", out var values))
            return 0;
        var max = 0.0;
        foreach (var value in values)
        {
            foreach (Match match in DurationPattern().Matches(value))
            {
                if (double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var duration))
                    max = Math.Max(max, duration);
            }
        }

        return max;
    }

    [GeneratedRegex(@"dur=([0-9.]+)")]
    private static partial Regex DurationPattern();

    private sealed class Session(HttpClient client, TestSockets sockets, int streams, string colo) : ISpeedTestSession
    {
        public string Server => colo;

        public bool HasLatencyPhase => true;

        public LatencyResult? Latency => null;

        /// <summary>
        /// Round trips of empty requests over one open connection, less the time the server reports for each.
        /// Ping is the median, jitter the average difference of neighbouring round trips.
        /// The worker behind the service now and then takes seconds over an empty answer: that time is not
        /// in the round trip, but the phase is cut short by <see cref="LatencyTime"/>.
        /// </summary>
        public async Task<LatencyResult> MeasureLatencyAsync(CancellationToken token)
        {
            var times = new List<double>(LatencyRequests);
            var clock = new Stopwatch();
            var phase = Stopwatch.StartNew();
            for (var i = 0; i < LatencyRequests && (phase.Elapsed < LatencyTime || times.Count < MinLatencySamples); i++)
            {
                clock.Restart();
                using var response = await client.GetAsync(Endpoint + "__down?bytes=0", HttpCompletionOption.ResponseHeadersRead, token);
                var elapsed = clock.Elapsed.TotalMilliseconds;
                response.EnsureSuccessStatusCode();
                if (i > 0)
                    times.Add(Math.Max(0, elapsed - ServerTime(response)));
            }

            var jitter = times.Zip(times.Skip(1), (a, b) => Math.Abs(a - b)).Average();
            times.Sort();
            var middle = times.Count / 2;
            var ping = times.Count % 2 == 1 ? times[middle] : (times[middle - 1] + times[middle]) / 2;
            return new LatencyResult(ping, jitter);
        }

        public Task DownloadAsync(ThroughputMeter meter, CancellationToken token) =>
            Task.WhenAll(Enumerable.Range(0, streams).Select(_ => DownloadStreamAsync(meter, token)));

        /// <summary>Counted by what the server acknowledged; <see cref="CountingContent"/> counts the writes as a fallback.</summary>
        public Task UploadAsync(ThroughputMeter meter, CancellationToken token)
        {
            meter.CountAcknowledged(sockets.Acknowledged);
            return Task.WhenAll(Enumerable.Range(0, streams).Select(_ => UploadStreamAsync(meter, token)));
        }

        public ValueTask DisposeAsync()
        {
            client.Dispose();
            return ValueTask.CompletedTask;
        }

        /// <summary>One connection: request after request until the phase ends.</summary>
        private async Task DownloadStreamAsync(ThroughputMeter meter, CancellationToken token)
        {
            var buffer = new byte[64 * 1024];
            while (!token.IsCancellationRequested)
            {
                using var response = await client.GetAsync(Endpoint + $"__down?bytes={DownloadBytes}", HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(token);
                int read;
                while ((read = await stream.ReadAsync(buffer, token)) > 0)
                    meter.Add(read);
            }
        }

        private async Task UploadStreamAsync(ThroughputMeter meter, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                using var content = new CountingContent(UploadBytes, meter);
                using var response = await client.PostAsync(Endpoint + "__up", content, token);
                response.EnsureSuccessStatusCode();
            }
        }
    }

    /// <summary>A body of random bytes that counts each piece as it is written to the connection.</summary>
    private sealed class CountingContent : HttpContent
    {
        private const int Chunk = 64 * 1024;

        // Random, so that nothing on the way could compress it; the same for every request.
        private static readonly byte[] Data = RandomData();

        private readonly long _length;
        private readonly ThroughputMeter _meter;

        public CountingContent(long length, ThroughputMeter meter)
        {
            _length = length;
            _meter = meter;
            Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken token)
        {
            for (var left = _length; left > 0;)
            {
                var size = (int)Math.Min(Chunk, left);
                await stream.WriteAsync(Data.AsMemory(0, size), token);
                _meter.Add(size);
                left -= size;
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _length;
            return true;
        }

        private static byte[] RandomData()
        {
            var data = new byte[Chunk];
            Random.Shared.NextBytes(data);
            return data;
        }
    }
}
