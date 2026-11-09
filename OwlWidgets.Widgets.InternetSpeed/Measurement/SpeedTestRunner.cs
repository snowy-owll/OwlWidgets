namespace OwlWidgets.Widgets.InternetSpeed.Measurement;

/// <summary>
/// One test with any provider: server, ping, download, upload. Runs on the UI thread of the card and reports there;
/// the network work of the provider goes to the thread pool, so a fast connection does not load the UI thread.
/// </summary>
internal sealed class SpeedTestRunner(ISpeedTestProvider provider, SpeedTestOptions options, ISpeedTestObserver observer)
{
    /// <summary>How often the needle moves.</summary>
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(200);

    /// <exception cref="OperationCanceledException"><paramref name="token"/> was cancelled.</exception>
    /// <exception cref="SpeedTestException">A phase moved no data.</exception>
    /// <remarks>Any other exception is a failure of the network or of the server.</remarks>
    public async Task<SpeedTestResult> RunAsync(CancellationToken token)
    {
        observer.PhaseStarted(SpeedTestPhase.Connecting);
        await using var session = await Task.Run(() => provider.ConnectAsync(options, token), token);
        observer.ServerFound(session.Server);

        LatencyResult? latency = null;
        if (session.HasLatencyPhase)
        {
            observer.PhaseStarted(SpeedTestPhase.Latency);
            latency = await Task.Run(() => session.MeasureLatencyAsync(token), token);
            observer.LatencyMeasured(latency);
        }

        var download = await TransferAsync(session, SpeedTestPhase.Download, session.DownloadAsync, token);
        var upload = await TransferAsync(session, SpeedTestPhase.Upload, session.UploadAsync, token);
        latency ??= session.Latency;
        return new SpeedTestResult(provider.Name, session.Server, DateTimeOffset.Now, latency?.Ping, latency?.Jitter, download, upload);
    }

    private async Task<double> TransferAsync(ISpeedTestSession session, SpeedTestPhase phase,
        Func<ThroughputMeter, CancellationToken, Task> transfer, CancellationToken token)
    {
        observer.PhaseStarted(phase);
        var meter = new ThroughputMeter();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        meter.Start();
        deadline.CancelAfter(options.Duration);
        var task = Task.Run(() => transfer(meter, deadline.Token), CancellationToken.None);
        LatencyResult? latency = null;
        while (!task.IsCompleted)
        {
            await Task.WhenAny(task, Task.Delay(Tick, token));
            token.ThrowIfCancellationRequested();
            meter.Sample();
            observer.SpeedMeasured(phase, meter.Current);
            if (!session.HasLatencyPhase && session.Latency is { } current && current != latency)
            {
                latency = current;
                observer.LatencyMeasured(current);
            }
        }

        try
        {
            await task;
        }
        // The deadline cuts the connections in the middle of a read or a write: whatever that throws ends the phase.
        catch (Exception) when (deadline.IsCancellationRequested && !token.IsCancellationRequested)
        {
        }

        meter.Sample();
        var result = meter.Result ?? throw new SpeedTestException($"{phase} of {provider.Name} moved no data.");
        observer.SpeedMeasured(phase, result);
        return result;
    }
}

internal sealed class SpeedTestException(string message) : Exception(message);
