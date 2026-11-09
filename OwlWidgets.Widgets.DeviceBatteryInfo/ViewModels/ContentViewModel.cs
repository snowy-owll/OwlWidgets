using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using OwlWidgets.Common;
using OwlWidgets.Common.Bluetooth;
using OwlWidgets.Contracts;
using OwlWidgets.UI.Common;

namespace OwlWidgets.Widgets.DeviceBatteryInfo.ViewModels;

/// <summary>Runs on the UI thread. PnP is asked on the thread pool.</summary>
internal sealed class ContentViewModel : ViewModelBase
{
    private readonly Config _config;
    private readonly string _dataFolder = string.Empty;
    private readonly IWidgetLogger? _logger;
    private CancellationTokenSource? _cancellation;
    private bool _failed;

    [Obsolete("Design-time only", true)]
    public ContentViewModel()
    {
        _config = new Config();
        OpenSettingsCommand = new RelayCommand(() => { });
        var gamepad = new DeviceViewModel(new Config.Device { Address = "987A14F05B04", Icon = "gamepad" }, _dataFolder, null);
        gamepad.Show(new BluetoothDevice(gamepad.Address, "Gamepad", true, 45));
        var headphones = new DeviceViewModel(new Config.Device { Address = "E8EECC7EAF84" }, _dataFolder, null);
        headphones.Show(new BluetoothDevice(headphones.Address, "Headphones Long Name Just To Test", false, 80));
        Devices = [gamepad, headphones];
    }

    /// <param name="dataFolder">Files of the instance, where the user icons are.</param>
    public ContentViewModel(Config config, string dataFolder, IWidgetLogger logger, Action openSettings)
    {
        _config = config;
        _dataFolder = dataFolder;
        _logger = logger;
        OpenSettingsCommand = new RelayCommand(openSettings);
        Devices = new ObservableCollection<DeviceViewModel>(Rows(config.Devices ?? []));
        IsEmpty = Devices.Count == 0;
    }

    public ObservableCollection<DeviceViewModel> Devices { get; }

    /// <summary>No device is added: the card shows the way to the settings.</summary>
    public bool IsEmpty
    {
        get => GetValue(() => IsEmpty);
        set => SetValue(() => IsEmpty, value);
    }

    public ICommand OpenSettingsCommand { get; }

    internal void Start()
    {
        if (IsEmpty)
            return;
        _cancellation?.Cancel();
        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;
        // Through the dispatcher: the awaits in it come back to the UI thread wherever Start is called from.
        Dispatcher.CurrentDispatcher.InvokeAsync(() => RunAsync(token));
    }

    internal void Stop()
    {
        _cancellation?.Cancel();
        _cancellation = null;
    }

    private async Task RunAsync(CancellationToken token)
    {
        var interval = _config.UpdateInterval > 0 ? _config.UpdateInterval : Config.DefaultUpdateInterval;
        while (!token.IsCancellationRequested)
        {
            await UpdateAsync(token);
            try
            {
                await Task.Delay(interval, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>A failed reading leaves the rows as they were; it is logged once until a reading succeeds.</summary>
    private async Task UpdateAsync(CancellationToken token)
    {
        IReadOnlyList<BluetoothDevice> found;
        try
        {
            found = await Task.Run(BluetoothDevices.GetAll);
        }
        catch (Exception exception)
        {
            if (!_failed && !token.IsCancellationRequested)
                _logger?.Log(WidgetLogLevel.Warning, "Bluetooth devices were not read.", exception);
            _failed = true;
            return;
        }

        if (token.IsCancellationRequested)
            return;
        _failed = false;

        foreach (var row in Devices)
        {
            var before = row.Status;
            row.Show(found.FirstOrDefault(device => device.Address == row.Address));
            if (row.Status != before)
                _logger?.Log(WidgetLogLevel.Debug, $"{row.Name} ({row.Address}): {before ?? "nothing"} -> {row.Status ?? "nothing"}.");
        }
    }

    /// <summary>A device without an address or a second row of one device is skipped.</summary>
    private List<DeviceViewModel> Rows(IEnumerable<Config.Device> devices) =>
        devices
            .Select(device => new DeviceViewModel(device, _dataFolder, _logger))
            .Where(row => row.Address.Length > 0)
            .DistinctBy(row => row.Address)
            .ToList();
}
