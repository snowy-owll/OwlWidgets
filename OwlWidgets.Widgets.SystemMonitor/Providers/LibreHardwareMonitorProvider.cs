using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OwlWidgets.Widgets.SystemMonitor.Sensors;

namespace OwlWidgets.Widgets.SystemMonitor.Providers;

/// <summary>
/// The web server of LibreHardwareMonitor (Options → Remote Web Server → Run), read from data.json: the tree
/// of its window, computer → hardware → group → sensor. LibreHardwareMonitor has no shared memory, and its WMI
/// classes do not answer a process without administrator rights. A sensor has an id ("/amdcpu/0/temperature/2"),
/// the type of the sensor and its value as text with the unit, formatted by the regional settings of Windows
/// ("53,6 °C"); there is no number without the unit. The names of the sensors are not translated.
/// </summary>
internal sealed partial class LibreHardwareMonitorProvider : ISensorProvider
{
    /// <summary>
    /// The CPU temperature, in order of preference: AMD (Tctl/Tdie, the one the CPU regulates by),
    /// Intel (Package), then the labels of older CPUs.
    /// </summary>
    private static readonly string[] CpuTemperatureLabels =
        ["Core (Tctl/Tdie)", "CPU Package", "Core (Tctl)", "Core (Tdie)", "Core Average", "Core Max"];

    private const string CpuLoadLabel = "CPU Total";

    /// <summary>The video card load: NVIDIA and AMD, then the 3D engine as Windows counts it (Intel).</summary>
    private static readonly string[] GpuLoadLabels = ["GPU Core", "D3D 3D"];

    private const string GpuTemperatureLabel = "GPU Core";

    /// <summary>A server that does not answer is not asked again until then, so the reading of Windows is not held up.</summary>
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(5);

    private readonly HttpClient _client;
    private readonly Uri? _data;
    /// <summary>The server is on this computer: it is not asked while nothing listens on its port.</summary>
    private readonly int? _localPort;
    private readonly AuthenticationHeaderValue? _authorization;
    /// <summary>Why the settings cannot be used; thrown on every reading, so the log has it once.</summary>
    private readonly string? _settingsError;
    private DateTime _retryAt = DateTime.MinValue;

    public LibreHardwareMonitorProvider(LibreHardwareMonitorServer server)
    {
        _client = new HttpClient(new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(1),
            UseProxy = false
        })
        {
            Timeout = TimeSpan.FromSeconds(2)
        };

        var address = LibreHardwareMonitorServer.ParseAddress(server.Address);
        if (address == null)
        {
            _settingsError = $"The address of the LibreHardwareMonitor web server is not one: '{server.Address}'.";
            return;
        }

        // The page of the server, so a path in the address is kept.
        _data = new Uri(address.AbsoluteUri.EndsWith('/') ? address : new Uri(address.AbsoluteUri + "/"), "data.json");
        if (address.IsLoopback)
            _localPort = address.Port;

        if (server.UserName.Length == 0)
            return;
        string password;
        try
        {
            password = server.ReadPassword();
        }
        catch (Exception exception) when (exception is System.Security.Cryptography.CryptographicException or FormatException)
        {
            _settingsError = "The saved password of the LibreHardwareMonitor web server cannot be read: it was saved by another "
                + "Windows user or on another computer. Enter it again in the settings.";
            return;
        }

        _authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{server.UserName}:{password}")));
    }

    public string Name => "LibreHardwareMonitor";

    public SensorReading? Read()
    {
        if (_settingsError != null)
            throw new InvalidOperationException(_settingsError);
        if (DateTime.UtcNow < _retryAt)
            return null;
        var target = _data;
        if (_localPort is { } port)
        {
            // A connection to a closed port of this computer is refused only after a second of retries in Windows.
            target = LocalTarget(port);
            if (target == null)
                return null;
        }

        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, target);
            request.Headers.Authorization = _authorization;
            response = _client.Send(request, HttpCompletionOption.ResponseContentRead);
        }
        catch (HttpRequestException exception) when (exception.HttpRequestError is HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError)
        {
            _retryAt = DateTime.UtcNow + RetryAfter;
            return null;
        }
        catch (OperationCanceledException)
        {
            // The timeout of the client: the server or the computer it is on does not answer.
            _retryAt = DateTime.UtcNow + RetryAfter;
            return null;
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new InvalidOperationException(_authorization == null
                    ? "The LibreHardwareMonitor web server asks for a user name and password: HTTP authentication is turned on in it."
                    : "The LibreHardwareMonitor web server did not accept the user name and password.");
            }

            response.EnsureSuccessStatusCode();
            using var stream = response.Content.ReadAsStream();
            using var json = JsonDocument.Parse(stream);
            return Parse(json.RootElement);
        }
    }

    public void Dispose() => _client.Dispose();

    internal static SensorReading Parse(JsonElement root)
    {
        // By the id of the hardware ("/gpu-nvidia/0"), in the order of the tree.
        var hardware = new Dictionary<string, Hardware>();
        Walk(root, null, null, hardware);

        double? cpuTemperature = null;
        double? cpuLoad = null;
        var gpuLoads = new List<GpuLoadSample>();
        var gpuTemperatures = new List<TemperatureSample>();
        foreach (var (id, item) in hardware)
        {
            if (FirstCpu().IsMatch(id))
            {
                cpuTemperature = First(item, "Temperature", CpuTemperatureLabels);
                cpuLoad = Value(item, "Load", CpuLoadLabel) is { } load ? Math.Clamp(load, 0, 100) : null;
            }
            else if (id.StartsWith("/gpu-", StringComparison.Ordinal))
            {
                var key = "LibreHardwareMonitor " + id;
                if (First(item, "Load", GpuLoadLabels) is { } load)
                    gpuLoads.Add(new GpuLoadSample(key, item.Name, Math.Clamp(load, 0, 100)));
                if (item.Values.ContainsKey(("Temperature", GpuTemperatureLabel)))
                    gpuTemperatures.Add(new TemperatureSample(key, item.Name, Value(item, "Temperature", GpuTemperatureLabel)));
            }
        }

        return new SensorReading(cpuTemperature, cpuLoad, gpuLoads, gpuTemperatures);
    }

    /// <param name="parent">The text of the node above: the group of a sensor.</param>
    /// <param name="grandparent">The text two nodes above: the hardware of a sensor.</param>
    private static void Walk(JsonElement node, string? parent, string? grandparent, Dictionary<string, Hardware> hardware)
    {
        var text = node.TryGetProperty("Text", out var textProperty) ? textProperty.GetString() ?? string.Empty : string.Empty;
        if (node.TryGetProperty("SensorId", out var idProperty) && idProperty.GetString() is { } sensorId
            && node.TryGetProperty("Type", out var typeProperty) && typeProperty.GetString() is { } type
            && HardwareId(sensorId) is { } hardwareId)
        {
            if (!hardware.TryGetValue(hardwareId, out var item))
                hardware[hardwareId] = item = new Hardware(grandparent ?? hardwareId);
            // Some names repeat ("D3D Copy"); the first one is kept.
            item.Values.TryAdd((type, text),
                node.TryGetProperty("Value", out var value) ? Number(value.GetString()) : null);
        }

        if (node.TryGetProperty("Children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
                Walk(child, text, parent, hardware);
        }
    }

    /// <summary>"/gpu-nvidia/0/load/3" → "/gpu-nvidia/0": the id of the sensor without its type and number.</summary>
    private static string? HardwareId(string sensorId)
    {
        var last = sensorId.LastIndexOf('/');
        var type = last > 0 ? sensorId.LastIndexOf('/', last - 1) : -1;
        return type > 0 ? sensorId[..type] : null;
    }

    /// <summary>"53,6 °C" or "53.6 °C" → 53.6; null: no number ("-" while the sensor has no value).</summary>
    internal static double? Number(string? text)
    {
        if (text == null || LeadingNumber().Match(text) is not { Success: true } match)
            return null;
        return double.Parse(match.Value.Replace(',', '.'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture);
    }

    private static double? First(Hardware hardware, string type, string[] labels)
    {
        foreach (var label in labels)
        {
            if (Value(hardware, type, label) is { } value)
                return value;
        }

        return null;
    }

    private static double? Value(Hardware hardware, string type, string label) =>
        hardware.Values.TryGetValue((type, label), out var value) ? value : null;

    /// <summary>
    /// The page on this computer, by what listens on its port. LibreHardwareMonitor binds the server to the network
    /// interface chosen in it (Options → Remote Web Server → Port), and then it does not answer on localhost:
    /// the page is asked at the address of that interface, which is this computer too.
    /// </summary>
    /// <returns>Null: nothing listens on the port.</returns>
    private Uri? LocalTarget(int port)
    {
        var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()
            .Where(endpoint => endpoint.Port == port)
            .Select(endpoint => endpoint.Address)
            .ToList();
        if (listeners.Count == 0)
            return null;
        if (listeners.Any(address => IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)))
            return _data;

        var address = listeners.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) ?? listeners[0];
        return new UriBuilder(_data!)
        {
            Host = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? $"[{address}]" : address.ToString()
        }.Uri;
    }

    /// <summary>The first CPU: "/amdcpu/0", "/intelcpu/0".</summary>
    [GeneratedRegex(@"^/\w*cpu/0$")]
    private static partial Regex FirstCpu();

    [GeneratedRegex(@"^\s*-?\d+(?:[.,]\d+)?")]
    private static partial Regex LeadingNumber();

    private sealed class Hardware(string name)
    {
        public string Name { get; } = name;

        /// <summary>By the type and the name of the sensor; null: the sensor has no value now.</summary>
        public Dictionary<(string Type, string Text), double?> Values { get; } = [];
    }
}
