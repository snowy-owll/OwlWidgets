using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using OwlWidgets.Common;
using OwlWidgets.Contracts;
using OwlWidgets.UI.Common;
using OwlWidgets.Widgets.SystemMonitor.Properties;
using OwlWidgets.Widgets.SystemMonitor.Sensors;

namespace OwlWidgets.Widgets.SystemMonitor.ViewModels
{
    internal class ContentViewModel : ViewModelBase
    {
        private const string CpuKey = "cpu";

        private readonly SystemSampler? _sampler;
        private readonly IWidgetLogger? _logger;
        private readonly InstanceFile<CardState>? _cardState;
        private readonly CardCorner? _buttonCorner;
        private readonly CollapsedValues _collapsedValues = new();
        /// <summary>Always the first tile of <see cref="Temperatures"/>; the video cards follow it.</summary>
        private readonly TemperatureTile _cpu;
        /// <summary>What to run for the CPU temperature: the program chosen, or any the widget knows.</summary>
        private readonly string _startProgram = Strings.CpuTemperature_StartAny;
        private CancellationTokenSource? _stopping;

        [Obsolete("Design-time only", true)]
        public ContentViewModel()
        {
            MoveCommand = new RelayCommand(() => { });
            ToggleTopmostCommand = new RelayCommand(() => IsTopmost = !IsTopmost);
            ToggleCollapsedCommand = new RelayCommand(() => IsCollapsed = !IsCollapsed);
            Anchor = CardCorner.TopRight;
            Drives.Add(new DriveUsage("C:\\", 137.5, 229.8));
            Drives.Add(new DriveUsage("D:\\", 780.3, 1000));
            CpuUsagePercentage = 35;
            SetMemory(new MemorySample(13.5, 31.3));
            GpuUsage.Add(new GpuUsageInfo("1", "AMD Radeon 780M Graphics") { UsagePercentage = 1.3 });
            GpuUsage.Add(new GpuUsageInfo("2", "NVIDIA GeForce RTX 4070 Laptop GPU") { UsagePercentage = 35.4 });
            _cpu = new TemperatureTile(CpuKey, "AMD Ryzen 9 7940HS", isCpu: true);
            foreach (var value in new[] { 41.0, 78.4, 56.3 })
                _cpu.Update(value, null);
            Temperatures.Add(_cpu);
            var integrated = new TemperatureTile("1", "AMD Radeon 780M Graphics", isCpu: false);
            foreach (var value in new[] { 44.0, 61.0, 56.6 })
                integrated.Update(value, null);
            Temperatures.Add(integrated);
            var discrete = new TemperatureTile("2", "NVIDIA GeForce RTX 4070 Laptop GPU", isCpu: false);
            discrete.Update(null, Strings.Gpu_Asleep, asleep: true);
            Temperatures.Add(discrete);
            UpdateTimeAndDate();
            UpdateCollapsedValues();
        }

        /// <param name="enterMoveMode">Asks the wrapper for the move mode of the card.</param>
        /// <param name="setTopmost">Asks the manager to keep the card above all windows or to put it back under them.</param>
        public ContentViewModel(SystemSampler sampler, Config config, IWidgetLogger logger, InstanceFile<CardState> cardState,
            Action enterMoveMode, Action<bool> setTopmost)
        {
            _sampler = sampler;
            _logger = logger;
            _cardState = cardState;
            _buttonCorner = config.ButtonCorner;
            _collapsedValues = config.Collapsed ?? new CollapsedValues();
            // Black, as the background of CardStyle: 50 % gives its 0x7F.
            var alpha = (byte)(Math.Clamp(config.BackgroundOpacity, 0, 100) * 255 / 100);
            CardBackground = new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0));
            CardBackground.Freeze();
            MoveCommand = new RelayCommand(enterMoveMode);
            ToggleTopmostCommand = new RelayCommand(() => setTopmost(!IsTopmost));
            ToggleCollapsedCommand = new RelayCommand(ToggleCollapsed);
            IsCollapsed = cardState.Read()?.Collapsed ?? false;
            ShowDrives = config.ShowDrives;
            ShowSystem = config.ShowSystem;
            ShowTemperatures = config.ShowTemperatures;
            _startProgram = config.Provider switch
            {
                SensorProviderKind.HWiNFO => Strings.CpuTemperature_StartHWiNFO,
                SensorProviderKind.Afterburner => Strings.CpuTemperature_StartAfterburner,
                SensorProviderKind.LibreHardwareMonitor => Strings.CpuTemperature_StartLibreHardwareMonitor,
                _ => Strings.CpuTemperature_StartAny
            };
            _cpu = new TemperatureTile(CpuKey, sampler.CpuName ?? Strings.Cpu, isCpu: true);
            Temperatures.Add(_cpu);
            UpdateTimeAndDate();
            UpdateCollapsedValues();
        }

        #region Public properties

        /// <summary>The sections of the card; one turned off is not read either. They do not change while the model lives.</summary>
        public bool ShowDrives { get; } = true;

        public bool ShowSystem { get; } = true;

        public bool ShowTemperatures { get; } = true;

        /// <summary>The line above a section is left out when no section is above it.</summary>
        public bool SystemIsFirst => !ShowDrives;

        public bool TemperaturesIsFirst => !ShowDrives && !ShowSystem;

        /// <summary>The background of the whole card and of the collapsed one, of the opacity in the settings.</summary>
        public SolidColorBrush CardBackground { get; } = new(Color.FromArgb(0x7F, 0, 0, 0));

        /// <summary>Asks the wrapper for the move mode.</summary>
        public ICommand MoveCommand { get; }

        /// <summary>Asks the manager to keep the card above all windows, or to put it back; <see cref="IsTopmost"/> follows its answer.</summary>
        public ICommand ToggleTopmostCommand { get; }

        public ICommand ToggleCollapsedCommand { get; }

        /// <summary>The card is above all windows, as the wrapper said last.</summary>
        public bool IsTopmost
        {
            get => GetValue(() => IsTopmost);
            set => SetValue(() => IsTopmost, value);
        }

        /// <summary>
        /// One or two lines of <see cref="CollapsedValues"/> in place of the sections, in the corner of the buttons;
        /// the window keeps the size of the whole card, so the buttons stay in place. Kept in <c>card-state.json</c>.
        /// </summary>
        public bool IsCollapsed
        {
            get => GetValue(() => IsCollapsed);
            set
            {
                SetValue(() => IsCollapsed, value);
                OnPropertyChanged(nameof(ChevronUp));
            }
        }

        /// <summary>The anchor corner of the instance, from the wrapper: the buttons stand in it unless the settings name another.</summary>
        public CardCorner Anchor
        {
            get => GetValue(() => Anchor);
            set
            {
                SetValue(() => Anchor, value);
                OnPropertyChanged(nameof(ButtonCorner));
                OnPropertyChanged(nameof(ButtonsAtTop));
                OnPropertyChanged(nameof(ButtonsAtLeft));
                OnPropertyChanged(nameof(ChevronUp));
            }
        }

        public CardCorner ButtonCorner => _buttonCorner ?? Anchor;

        public bool ButtonsAtTop => !ButtonCorner.IsBottom();

        public bool ButtonsAtLeft => !ButtonCorner.IsRight();

        /// <summary>The chevron points to where the card goes: to the corner of the buttons when it collapses, away when it expands.</summary>
        public bool ChevronUp => ButtonsAtTop != IsCollapsed;

        /// <summary>What the collapsed card shows, in the order of the settings.</summary>
        public ObservableCollection<CollapsedValue> CollapsedValues { get; } = [];

        public string? CurrentTime
        {
            get => GetValue(() => CurrentTime);
            set => SetValue(() => CurrentTime, value);
        }

        public string? CurrentDate
        {
            get => GetValue(() => CurrentDate);
            set => SetValue(() => CurrentDate, value);
        }

        public ObservableCollection<DriveUsage> Drives { get; } = [];

        public double CpuUsagePercentage
        {
            get => GetValue(() => CpuUsagePercentage);
            set => SetValue(() => CpuUsagePercentage, value);
        }

        public double UsedRam
        {
            get => GetValue(() => UsedRam);
            set => SetValue(() => UsedRam, value);
        }

        public double TotalRam
        {
            get => GetValue(() => TotalRam);
            set => SetValue(() => TotalRam, value);
        }

        public double RamUsagePercentage
        {
            get => GetValue(() => RamUsagePercentage);
            set => SetValue(() => RamUsagePercentage, value);
        }

        public ObservableCollection<GpuUsageInfo> GpuUsage { get; } = [];

        /// <summary>The processor, then the video cards that report a temperature or sleep.</summary>
        public ObservableCollection<TemperatureTile> Temperatures { get; } = [];

        #endregion Public properties

        #region Internal methods

        internal void Start()
        {
            if (_sampler == null || _stopping != null)
                return;
            _stopping = new CancellationTokenSource();
            _ = RunAsync(_sampler, _stopping.Token);
        }

        /// <summary>The sampler is disposed when the reading in progress, if any, ends.</summary>
        internal void Stop()
        {
            _stopping?.Cancel();
        }

        #endregion Internal methods

        #region Private methods

        /// <summary>Reads off the UI thread once a second, at the turn of the second so the clock does not skip.</summary>
        private async Task RunAsync(SystemSampler sampler, CancellationToken stopping)
        {
            try
            {
                while (!stopping.IsCancellationRequested)
                {
                    try
                    {
                        Apply(await Task.Run(sampler.Read, CancellationToken.None));
                    }
                    catch (Exception exception)
                    {
                        _logger?.Log(WidgetLogLevel.Error, "Reading the sensors failed.", exception);
                    }

                    UpdateTimeAndDate();
                    await Task.Delay(1000 - DateTime.Now.Millisecond, stopping);
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                sampler.Dispose();
            }
        }

        /// <summary>
        /// Values are rounded to what the card shows, so a reading that changes nothing on screen
        /// raises no change and the card is not laid out and drawn again for it.
        /// </summary>
        private void Apply(SystemSample sample)
        {
            if (sample.CpuLoad is { } load)
                CpuUsagePercentage = Math.Round(load);
            if (sample.Memory is { } memory)
                SetMemory(new MemorySample(Math.Round(memory.Used, 1), Math.Round(memory.Total, 1)));
            _cpu.Update(Round(sample.CpuTemperature), sample.CpuTemperatureSource switch
            {
                CpuTemperatureSource.Provider => null,
                CpuTemperatureSource.NoProvider => Strings.CpuTemperature_NoProvider,
                CpuTemperatureSource.NotReported => Strings.CpuTemperature_NotReported,
                _ => _startProgram
            });

            Sync(GpuUsage, 0, sample.GpuLoads, s => s.Key, i => i.Key, s => new GpuUsageInfo(s.Key, s.Name),
                (item, s) => item.UsagePercentage = Math.Round(s.Load));
            Sync(Temperatures, 1, sample.GpuTemperatures, s => s.Key, i => i.Key, s => new TemperatureTile(s.Key, s.Name, isCpu: false),
                (item, s) => item.Update(Round(s.Value), s.Asleep ? Strings.Gpu_Asleep : null, s.Asleep));

            if (sample.Drives != null)
            {
                Drives.Clear();
                foreach (var drive in sample.Drives)
                    Drives.Add(new DriveUsage(drive.Name, drive.Used, drive.Total));
            }

            UpdateCollapsedValues();
        }

        private void ToggleCollapsed()
        {
            IsCollapsed = !IsCollapsed;
            _cardState!.Write(new CardState { Collapsed = IsCollapsed });
        }

        /// <summary>From the values of the sections; a section turned off has none to give.</summary>
        private void UpdateCollapsedValues()
        {
            var values = new List<(string Key, string Label, string? Tip, bool Temperature, bool Cpu, double? Value, bool Asleep)>();
            string Numbered(string label, int index, int count) => count > 1 ? label + (index + 1) : label;
            if (ShowSystem && _collapsedValues.CpuLoad)
                values.Add(("cpu", Strings.Term_Cpu, null, false, true, CpuUsagePercentage, false));
            if (ShowSystem && _collapsedValues.Memory)
                values.Add(("ram", Strings.Term_Ram, null, false, false, RamUsagePercentage, false));
            if (ShowSystem && _collapsedValues.GpuLoad)
            {
                for (var i = 0; i < GpuUsage.Count; i++)
                    values.Add(("gpu:" + GpuUsage[i].Key, Numbered(Strings.Term_Gpu, i, GpuUsage.Count), GpuUsage[i].Name, false, false, GpuUsage[i].UsagePercentage, false));
            }

            if (ShowTemperatures && _collapsedValues.CpuTemperature)
                values.Add(("t:" + _cpu.Key, Strings.Term_Cpu, _cpu.Hint ?? _cpu.Name, true, true, _cpu.Temperature, false));
            if (ShowTemperatures && _collapsedValues.GpuTemperature)
            {
                var cards = Temperatures.Skip(1).ToList();
                for (var i = 0; i < cards.Count; i++)
                    values.Add(("t:" + cards[i].Key, Numbered(Strings.Term_Gpu, i, cards.Count), cards[i].Name, true, false, cards[i].Temperature, cards[i].IsAsleep));
            }

            if (ShowDrives && _collapsedValues.Drives)
            {
                foreach (var drive in Drives)
                    values.Add(("d:" + drive.Name, DeviceNames.Drive(drive.Name), drive.Name, false, false, drive.UsagePercentage, false));
            }

            Sync(CollapsedValues, 0, values, v => v.Key, i => i.Key, v => new CollapsedValue(v.Key, v.Label, v.Temperature, v.Cpu),
                (item, v) => item.Update(v.Value, v.Asleep, v.Tip));
        }

        /// <summary>Temperatures are shown to a tenth of a degree.</summary>
        private static double? Round(double? temperature) => temperature is { } value ? Math.Round(value, 1) : null;

        private void SetMemory(MemorySample memory)
        {
            TotalRam = memory.Total;
            UsedRam = memory.Used;
            RamUsagePercentage = memory.Total > 0 ? memory.Used / memory.Total * 100 : 0;
        }

        private void UpdateTimeAndDate()
        {
            var now = DateTime.Now;
            CurrentTime = now.ToString("HH:mm:ss");
            // Names of the day and the month are words of the interface, so they follow its language, not the regional
            // settings: "Tuesday, November 10", "вторник, 10 ноября". Weather names the days of its forecast the same way.
            var culture = System.Globalization.CultureInfo.CurrentUICulture;
            CurrentDate = now.ToString("dddd", culture) + ", " + now.ToString(culture.DateTimeFormat.MonthDayPattern, culture);
        }

        /// <summary>
        /// Updates the rows in place while the cards stay the same, so the view keeps its elements;
        /// rebuilds the list when a card appears, goes or the provider changes.
        /// </summary>
        /// <param name="start">The items before it are not the samples' and are kept.</param>
        private static void Sync<TItem, TSample>(ObservableCollection<TItem> items, int start, IReadOnlyList<TSample> samples,
            Func<TSample, string> sampleKey, Func<TItem, string> itemKey, Func<TSample, TItem> create, Action<TItem, TSample> update)
        {
            if (!items.Skip(start).Select(itemKey).SequenceEqual(samples.Select(sampleKey)))
            {
                while (items.Count > start)
                    items.RemoveAt(items.Count - 1);
                foreach (var sample in samples)
                    items.Add(create(sample));
            }

            for (var i = 0; i < samples.Count; i++)
                update(items[start + i], samples[i]);
        }

        #endregion Private methods
    }

    /// <summary>The colour of a value: the load and the temperature have their own thresholds, as on the full card.</summary>
    internal enum ValueLevel
    {
        None,
        Low,
        Medium,
        High
    }

    /// <summary>A value of the collapsed card: a load in percent or a temperature in °C.</summary>
    internal class CollapsedValue(string key, string label, bool isTemperature, bool isCpu) : ViewModelBase
    {
        public string Key { get; } = key;

        /// <summary>CPU, RAM, GPU, GPU2, C:.</summary>
        public string Label { get; } = label;

        public bool IsTemperature { get; } = isTemperature;

        /// <summary>The processor icon of a temperature; otherwise a video card.</summary>
        public bool IsCpu { get; } = isCpu;

        /// <summary>Percent or °C, rounded as shown; null: no value.</summary>
        public double? Value
        {
            get => GetValue(() => Value);
            private set => SetValue(() => Value, value);
        }

        /// <summary>A video card powered off to save power: idle, not missing.</summary>
        public bool IsAsleep
        {
            get => GetValue(() => IsAsleep);
            private set => SetValue(() => IsAsleep, value);
        }

        /// <summary>23%, 67°C, a dash or idle.</summary>
        public string Text
        {
            get => GetValue(() => Text);
            private set => SetValue(() => Text, value);
        }

        public ValueLevel Level
        {
            get => GetValue(() => Level);
            private set => SetValue(() => Level, value);
        }

        /// <summary>The device, or why there is no value.</summary>
        public string? ToolTip
        {
            get => GetValue(() => ToolTip);
            private set => SetValue(() => ToolTip, value);
        }

        internal void Update(double? value, bool asleep, string? toolTip)
        {
            Value = value is { } v ? Math.Round(v) : null;
            IsAsleep = asleep;
            ToolTip = toolTip;
            var culture = System.Globalization.CultureInfo.CurrentCulture;
            Text = Value is { } shown
                ? shown.ToString("0", culture) + (IsTemperature ? "°C" : "%")
                : asleep ? Strings.ImHex_Idle : "—";
            Level = Value is not { } level ? ValueLevel.None
                : IsTemperature ? level < 70 ? ValueLevel.Low : level < 85 ? ValueLevel.Medium : ValueLevel.High
                : level < 50 ? ValueLevel.Low : level < 80 ? ValueLevel.Medium : ValueLevel.High;
        }
    }

    public class DriveUsage(string name, double usedSpace, double totalSpace)
    {
        public string Name { get; } = name;

        public double UsedSpace { get; } = usedSpace;

        public double TotalSpace { get; } = totalSpace;

        public double UsagePercentage { get; } = totalSpace > 0 ? usedSpace / totalSpace * 100 : 0;
    }

    internal class GpuUsageInfo(string key, string name) : ViewModelBase
    {
        public string Key { get; } = key;

        public string Name { get; } = name;

        public double UsagePercentage
        {
            get => GetValue(() => UsagePercentage);
            set => SetValue(() => UsagePercentage, value);
        }
    }

    /// <summary>The temperature of the processor or of a video card.</summary>
    internal class TemperatureTile(string key, string name, bool isCpu) : ViewModelBase
    {
        public string Key { get; } = key;

        public string Name { get; } = name;

        /// <summary>The processor icon and colour; otherwise a video card.</summary>
        public bool IsCpu { get; } = isCpu;

        /// <summary>°C; null: no value, <see cref="Hint"/> says why when it is known.</summary>
        public double? Temperature
        {
            get => GetValue(() => Temperature);
            set => SetValue(() => Temperature, value);
        }

        /// <summary>Lowest since the card started.</summary>
        public double? TemperatureMin
        {
            get => GetValue(() => TemperatureMin);
            set => SetValue(() => TemperatureMin, value);
        }

        /// <summary>Highest since the card started.</summary>
        public double? TemperatureMax
        {
            get => GetValue(() => TemperatureMax);
            set => SetValue(() => TemperatureMax, value);
        }

        /// <summary>
        /// Under the ring: why there is no temperature (no sensor program, a video card asleep, not woken up to be asked).
        /// </summary>
        public string? Hint
        {
            get => GetValue(() => Hint);
            set => SetValue(() => Hint, value);
        }

        /// <summary>A video card powered off to save power: the terminal styles show it as idle, not as missing.</summary>
        public bool IsAsleep
        {
            get => GetValue(() => IsAsleep);
            set => SetValue(() => IsAsleep, value);
        }

        /// <summary>
        /// A hint has been shown under the ring: its line stays when the hint goes, so a card that falls asleep
        /// and wakes up does not move the tiles under it.
        /// </summary>
        public bool KeepsHintLine
        {
            get => GetValue(() => KeepsHintLine);
            private set => SetValue(() => KeepsHintLine, value);
        }

        internal void Update(double? temperature, string? hint, bool asleep = false)
        {
            Temperature = temperature;
            Hint = hint;
            if (hint != null)
                KeepsHintLine = true;
            IsAsleep = asleep;
            if (temperature is { } value)
            {
                TemperatureMin = Math.Min(TemperatureMin ?? value, value);
                TemperatureMax = Math.Max(TemperatureMax ?? value, value);
            }
        }
    }
}
