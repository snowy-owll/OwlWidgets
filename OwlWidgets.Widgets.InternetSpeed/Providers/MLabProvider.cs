using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text.Json;
using OwlWidgets.Widgets.InternetSpeed.Measurement;

namespace OwlWidgets.Widgets.InternetSpeed.Providers;

/// <summary>
/// Measurement Lab, NDT7 (github.com/m-lab/ndt-server, spec/ndt7-protocol.md). The locate service names the nearest
/// server and gives signed URLs of both tests. Each test is one WebSocket with the subprotocol
/// <c>net.measurementlab.ndt.v7</c>: binary messages carry the data, text messages carry the measurements of the side
/// that sends them as JSON. The server ends the download after about 10 seconds; the client ends the upload.
/// The round trip comes from the TCP state of the server in the download measurements, so there is no ping phase.
/// </summary>
internal sealed class MLabProvider : ISpeedTestProvider
{
    private const string Locate = "https://locate.measurementlab.net/v2/nearest/ndt/ndt7";
    private const string SubProtocol = "net.measurementlab.ndt.v7";
    private const string DownloadKey = "wss:///ndt/v7/download";
    private const string UploadKey = "wss:///ndt/v7/upload";

    /// <summary>The protocol caps a message at 16 MiB.</summary>
    private const int MaxMessage = 1 << 24;

    private const int FirstMessage = 1 << 13;

    private static readonly HttpClient Http = CreateClient();

    public string Name => "M-Lab";

    public async Task<ISpeedTestSession> ConnectAsync(SpeedTestOptions options, CancellationToken token)
    {
        var version = typeof(MLabProvider).Assembly.GetName().Version?.ToString(3) ?? "0";
        var url = $"{Locate}?client_name=owlwidgets-internetspeed&client_version={version}";
        using var response = await Http.GetAsync(url, token);
        // The URLs of the answer carry access tokens: the message holds the status code only.
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        if (!json.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            throw new SpeedTestException("The M-Lab locate service named no server.");

        var server = results[0];
        var urls = server.GetProperty("urls");
        var download = new Uri(urls.GetProperty(DownloadKey).GetString()!);
        var upload = new Uri(urls.GetProperty(UploadKey).GetString()!);
        var city = server.TryGetProperty("location", out var location) && location.TryGetProperty("city", out var c) ? c.GetString() : null;
        var country = location.ValueKind == JsonValueKind.Object && location.TryGetProperty("country", out var k) ? k.GetString() : null;
        var name = string.Join(", ", new[] { city, country }.Where(part => !string.IsNullOrWhiteSpace(part)));
        return new Session(download, upload, name.Length > 0 ? name : download.Host);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OwlWidgets-InternetSpeed");
        return client;
    }

    private sealed class Session(Uri downloadUrl, Uri uploadUrl, string server) : ISpeedTestSession
    {
        private readonly TestSockets _sockets = new();
        private volatile LatencyResult? _latency;
        private HttpMessageInvoker? _invoker;

        /// <summary>The WebSockets of this test, with the large buffers: the whole upload of NDT7 goes over one connection.</summary>
        private HttpMessageInvoker Invoker => _invoker ??= new HttpMessageInvoker(new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(10),
            ConnectCallback = _sockets.ConnectAsync
        });

        public string Server => server;

        public bool HasLatencyPhase => false;

        public LatencyResult? Latency => _latency;

        public Task<LatencyResult> MeasureLatencyAsync(CancellationToken token) => throw new NotSupportedException();

        public async Task DownloadAsync(ThroughputMeter meter, CancellationToken token)
        {
            using var socket = await OpenAsync(downloadUrl, token);
            var buffer = new byte[1 << 16];
            using var text = new MemoryStream();
            while (true)
            {
                var received = await socket.ReceiveAsync(buffer.AsMemory(), token);
                if (received.MessageType == WebSocketMessageType.Close)
                {
                    await CloseAsync(socket);
                    return;
                }

                if (received.MessageType == WebSocketMessageType.Binary)
                {
                    meter.Add(received.Count);
                    continue;
                }

                text.Write(buffer, 0, received.Count);
                if (!received.EndOfMessage)
                    continue;
                ReadLatency(text.ToArray());
                text.SetLength(0);
            }
        }

        /// <summary>
        /// Messages of growing size, as the protocol advises: a message doubles once the bytes sent reach
        /// 16 of it, so a slow line is not stuck behind one huge message. The speed is what the server acknowledged
        /// over TCP, read from our socket at every sample: the measurements of the server come every quarter
        /// of a second or so and arrive late by the network, and a rate between two of them jumps.
        /// </summary>
        public async Task UploadAsync(ThroughputMeter meter, CancellationToken token)
        {
            using var socket = await OpenAsync(uploadUrl, token);
            meter.CountAcknowledged(_sockets.Acknowledged);
            var receiving = DiscardMeasurementsAsync(socket, token);
            var data = new byte[FirstMessage];
            Random.Shared.NextBytes(data);
            long sent = 0;
            try
            {
                while (!token.IsCancellationRequested && !receiving.IsCompleted)
                {
                    await socket.SendAsync(data, WebSocketMessageType.Binary, true, token);
                    meter.Add(data.Length);
                    sent += data.Length;
                    if (data.Length < MaxMessage && sent >= 16L * data.Length)
                    {
                        data = new byte[data.Length * 2];
                        Random.Shared.NextBytes(data);
                    }
                }
            }
            finally
            {
                await CloseAsync(socket);
            }

            await receiving;
        }

        public ValueTask DisposeAsync()
        {
            _invoker?.Dispose();
            return ValueTask.CompletedTask;
        }

        /// <summary>The measurements of the server during the upload are read only to see the close of the socket.</summary>
        private static async Task DiscardMeasurementsAsync(ClientWebSocket socket, CancellationToken token)
        {
            var buffer = new byte[1 << 14];
            while (true)
            {
                var received = await socket.ReceiveAsync(buffer.AsMemory(), token);
                if (received.MessageType == WebSocketMessageType.Close)
                    return;
            }
        }

        /// <summary>
        /// A measurement of the server during the download: <c>TCPInfo.MinRTT</c> and <c>TCPInfo.RTTVar</c>
        /// in microseconds. A message that is not such JSON is skipped.
        /// </summary>
        private void ReadLatency(byte[] message)
        {
            try
            {
                using var json = JsonDocument.Parse(message);
                if (!json.RootElement.TryGetProperty("TCPInfo", out var tcp))
                    return;
                if (tcp.TryGetProperty("MinRTT", out var minRtt) && minRtt.GetInt64() > 0)
                {
                    double? jitter = tcp.TryGetProperty("RTTVar", out var rttVar) ? rttVar.GetInt64() / 1000.0 : null;
                    _latency = new LatencyResult(minRtt.GetInt64() / 1000.0, jitter);
                }
            }
            catch (JsonException)
            {
            }
            catch (InvalidOperationException)
            {
            }
            catch (FormatException)
            {
            }
        }

        private async Task<ClientWebSocket> OpenAsync(Uri url, CancellationToken token)
        {
            var socket = new ClientWebSocket();
            socket.Options.AddSubProtocol(SubProtocol);
            socket.Options.SetRequestHeader("User-Agent", "OwlWidgets-InternetSpeed");
            try
            {
                await socket.ConnectAsync(url, Invoker, token);
                return socket;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        /// <summary>Says goodbye politely, but does not wait long: the phase is over either way.</summary>
        private static async Task CloseAsync(ClientWebSocket socket)
        {
            if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
                return;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, timeout.Token);
            }
            catch (Exception exception) when (exception is WebSocketException or OperationCanceledException or IOException)
            {
            }
        }
    }
}
