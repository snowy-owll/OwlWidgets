using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using OwlWidgets.Contracts;

namespace OwlWidgets.Widgets.NetworkMonitor.Traffic;

internal enum TrafficState
{
    /// <summary>The adapter is up and counted.</summary>
    Connected,

    /// <summary>Automatic, and Windows has no route to the internet.</summary>
    NoConnection,

    /// <summary>The chosen adapter is not in Windows or not up.</summary>
    NotConnected
}

/// <param name="Download">Bytes per second since the previous reading; null when there is nothing to compare with.</param>
internal sealed record TrafficSample(TrafficState State, string? AdapterName, string? AdapterDescription, double? Download, double? Upload);

/// <summary>
/// The byte counters of one adapter, read once a second off the UI thread. The counters are those of Task Manager:
/// <see cref="NetworkInterface.GetIPStatistics"/> asks <c>GetIfEntry2</c> for the 64-bit <c>InOctets</c> and <c>OutOctets</c>
/// on every call, without administrator rights.
/// </summary>
internal sealed class TrafficSampler : IDisposable
{
    /// <summary>Readings between the checks which adapter to watch, besides a change of the addresses of Windows.</summary>
    private const int ResolveEvery = 5;

    /// <summary>
    /// A rate is taken only over a gap of this many seconds: a longer one is a sleep or a stall of the process,
    /// and its average would show a plateau where there was none.
    /// </summary>
    private const double MinGap = 0.5, MaxGap = 3;

    /// <summary>1.1.1.1: the same in either byte order. Only the routing table is asked, nothing is sent.</summary>
    private const uint InternetAddress = 0x01010101;

    private readonly Config _config;
    private readonly IWidgetLogger _logger;
    private NetworkInterface? _adapter;
    private TrafficState _state = TrafficState.NoConnection;
    private int _sinceResolve;
    private volatile bool _addressesChanged = true;
    private bool _failureLogged;
    private long _received, _sent, _readAt;
    private bool _hasBaseline;

    public TrafficSampler(Config config, IWidgetLogger logger)
    {
        _config = config;
        _logger = logger;
        NetworkChange.NetworkAddressChanged += OnAddressesChanged;
    }

    public TrafficSample Read()
    {
        if (_addressesChanged || ++_sinceResolve >= ResolveEvery)
            Resolve();

        if (_adapter == null)
            return new TrafficSample(_state, null, null, null, null);

        IPInterfaceStatistics statistics;
        try
        {
            statistics = _adapter.GetIPStatistics();
        }
        catch (NetworkInformationException exception)
        {
            // The adapter went away between two checks: look again on the next reading.
            if (!_failureLogged)
                _logger.Log(WidgetLogLevel.Warning, $"Reading the counters of {_adapter.Name} failed.", exception);
            _failureLogged = true;
            _addressesChanged = true;
            _hasBaseline = false;
            return new TrafficSample(_state, _adapter.Name, _adapter.Description, null, null);
        }

        _failureLogged = false;
        long received = statistics.BytesReceived, sent = statistics.BytesSent, now = Stopwatch.GetTimestamp();
        double? download = null, upload = null;
        if (_hasBaseline)
        {
            var seconds = Stopwatch.GetElapsedTime(_readAt, now).TotalSeconds;
            // A counter that went back was reset: the adapter reconnected.
            if (seconds is >= MinGap and <= MaxGap && received >= _received && sent >= _sent)
            {
                download = (received - _received) / seconds;
                upload = (sent - _sent) / seconds;
            }
        }

        (_received, _sent, _readAt, _hasBaseline) = (received, sent, now, true);
        return new TrafficSample(_state, _adapter.Name, _adapter.Description, download, upload);
    }

    public void Dispose() => NetworkChange.NetworkAddressChanged -= OnAddressesChanged;

    private void OnAddressesChanged(object? sender, EventArgs e) => _addressesChanged = true;

    private void Resolve()
    {
        _addressesChanged = false;
        _sinceResolve = 0;
        var adapters = NetworkAdapters.List();
        NetworkInterface? adapter;
        TrafficState state;
        if (_config.AdapterId != null)
        {
            adapter = adapters.FirstOrDefault(a => a.Id == _config.AdapterId && a.OperationalStatus == OperationalStatus.Up);
            state = adapter == null ? TrafficState.NotConnected : TrafficState.Connected;
        }
        else
        {
            adapter = FindInternetAdapter(adapters);
            state = adapter == null ? TrafficState.NoConnection : TrafficState.Connected;
        }

        if (adapter?.Id != _adapter?.Id || state != _state)
        {
            _logger.Log(WidgetLogLevel.Information, adapter == null
                ? $"No adapter to watch: {state}."
                : $"Watching {adapter.Name} ({adapter.Description}, {adapter.NetworkInterfaceType}).");
            _hasBaseline = false;
        }

        _adapter = adapter;
        _state = state;
    }

    /// <summary>
    /// The adapter of the route to the internet; without an IPv4 route, the first adapter that is up and has a gateway.
    /// </summary>
    private static NetworkInterface? FindInternetAdapter(IReadOnlyList<NetworkInterface> adapters)
    {
        var up = adapters.Where(a => a.OperationalStatus == OperationalStatus.Up).ToList();
        if (GetBestInterface(InternetAddress, out var index) == 0)
        {
            var routed = up.FirstOrDefault(a => IndexOf(a) == index);
            if (routed != null)
                return routed;
        }

        return up.FirstOrDefault(a =>
        {
            try
            {
                return a.GetIPProperties().GatewayAddresses.Count > 0;
            }
            catch (NetworkInformationException)
            {
                return false;
            }
        });
    }

    private static int? IndexOf(NetworkInterface adapter)
    {
        try
        {
            return adapter.GetIPProperties().GetIPv4Properties()?.Index;
        }
        catch (NetworkInformationException)
        {
            // No IPv4 on this adapter.
            return null;
        }
    }

    [DllImport("iphlpapi.dll")]
    private static extern int GetBestInterface(uint destAddr, out int bestIfIndex);
}
