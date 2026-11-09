namespace OwlWidgets.Widgets.InternetSpeed.Measurement;

/// <summary>
/// Servers a test runs against. A provider only finds a server and moves bytes: the phases, their duration,
/// the warm-up and the arithmetic of the result belong to <see cref="SpeedTestRunner"/>, the same for every provider.
/// A new provider implements this interface and its session, and gets a value in <see cref="SpeedTestProviderKind"/>.
/// </summary>
internal interface ISpeedTestProvider
{
    /// <summary>Name for the log and for the stored result, the same in every language.</summary>
    string Name { get; }

    /// <summary>Finds the server of this test; runs on the thread pool.</summary>
    Task<ISpeedTestSession> ConnectAsync(SpeedTestOptions options, CancellationToken token);
}

/// <summary>
/// One test against one server. The methods run on the thread pool, one at a time, in the order of the phases.
/// The transfers run until <c>token</c> is cancelled, which is how the runner ends a phase on time,
/// or until the server ends the test itself; both are no error.
/// </summary>
internal interface ISpeedTestSession : IAsyncDisposable
{
    /// <summary>Where the server is, for the card: a city or a data center code.</summary>
    string Server { get; }

    /// <summary>
    /// false: the latency comes along the transfers, in <see cref="Latency"/>, and there is no ping phase.
    /// </summary>
    bool HasLatencyPhase { get; }

    /// <summary>The latency measured by the transfers so far; read by the runner from another thread.</summary>
    LatencyResult? Latency { get; }

    /// <summary>Called only when <see cref="HasLatencyPhase"/> is set.</summary>
    Task<LatencyResult> MeasureLatencyAsync(CancellationToken token);

    /// <summary>Receives data and counts every byte in <paramref name="meter"/>.</summary>
    Task DownloadAsync(ThroughputMeter meter, CancellationToken token);

    /// <summary>Sends data and counts in <paramref name="meter"/> what is sent or, when the server reports it, received there.</summary>
    Task UploadAsync(ThroughputMeter meter, CancellationToken token);
}

/// <param name="Duration">Of the download and of the upload each; a server may end its phase earlier.</param>
/// <param name="Streams">Connections at once in each direction, for providers that open several.</param>
internal sealed record SpeedTestOptions(TimeSpan Duration, int Streams);

/// <param name="Ping">Round trip, milliseconds.</param>
/// <param name="Jitter">Variation of the round trip, milliseconds; null when the provider does not measure it.</param>
internal sealed record LatencyResult(double Ping, double? Jitter);

internal enum SpeedTestPhase
{
    Idle,
    Connecting,
    Latency,
    Download,
    Upload
}

/// <summary>What the runner reports while a test goes; called on the thread that runs the test, the UI thread of the card.</summary>
internal interface ISpeedTestObserver
{
    void PhaseStarted(SpeedTestPhase phase);

    void ServerFound(string server);

    void LatencyMeasured(LatencyResult latency);

    /// <summary>The current speed of a transfer, bits per second, several times a second.</summary>
    void SpeedMeasured(SpeedTestPhase phase, double bitsPerSecond);
}

/// <param name="Time">End of the test.</param>
/// <param name="Download">Bits per second.</param>
/// <param name="Upload">Bits per second.</param>
internal sealed record SpeedTestResult(
    string Provider,
    string Server,
    DateTimeOffset Time,
    double? Ping,
    double? Jitter,
    double Download,
    double Upload);
