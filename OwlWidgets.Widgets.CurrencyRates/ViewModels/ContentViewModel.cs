using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using OwlWidgets.Common;
using OwlWidgets.Contracts;
using OwlWidgets.UI.Common;
using OwlWidgets.Widgets.CurrencyRates.Properties;

namespace OwlWidgets.Widgets.CurrencyRates.ViewModels;

internal sealed class ContentViewModel : ViewModelBase
{
    /// <summary>A week and the weekend around it: the answer always holds the previous day.</summary>
    private const int HistoryDays = 10;

    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(2);

    private readonly Config _config;
    private readonly IWidgetLogger? _logger;
    private CancellationTokenSource? _cancellation;
    private DateOnly? _date;
    private DateTime? _updated;

    [Obsolete("Design-time only", true)]
    public ContentViewModel()
    {
        _config = new Config();
        OpenSettingsCommand = new RelayCommand(() => { });
        RefreshCommand = new RelayCommand(() => { });
        var usd = new RateViewModel(new Config.CurrencyPair { Base = "USD", Quote = "RUB" });
        usd.Show(83.56m, 83.48m);
        var eur = new RateViewModel(new Config.CurrencyPair { Base = "EUR", Quote = "RUB" });
        eur.Show(94.23m, 94.40m);
        var jpy = new RateViewModel(new Config.CurrencyPair { Base = "JPY", Quote = "RUB", Amount = 100 });
        jpy.Show(0.5292m, 0.5292m);
        var gold = new RateViewModel(new Config.CurrencyPair { Base = "XAU", Quote = "USD" });
        Rates = [usd, eur, jpy, gold];
        _date = new DateOnly(2026, 10, 5);
        _updated = DateTime.Now;
        ShowStatus(false);
    }

    public ContentViewModel(Config config, IWidgetLogger logger, Action openSettings)
    {
        _config = config;
        _logger = logger;
        OpenSettingsCommand = new RelayCommand(openSettings);
        RefreshCommand = new RelayCommand(Refresh);
        var pairs = (config.Pairs ?? []).Where(pair => !string.IsNullOrWhiteSpace(pair.Base) && !string.IsNullOrWhiteSpace(pair.Quote)).ToList();
        if (pairs.Count > Config.MaxPairs)
        {
            logger.Log(WidgetLogLevel.Warning, $"{pairs.Count} pairs in the settings, only the first {Config.MaxPairs} are shown.");
            pairs = pairs.Take(Config.MaxPairs).ToList();
        }

        Rates = new ObservableCollection<RateViewModel>(pairs.Select(pair => new RateViewModel(pair)));
        Status = Strings.Status_Loading;
    }

    public ObservableCollection<RateViewModel> Rates { get; }

    public bool IsEmpty => Rates.Count == 0;

    public string? Status
    {
        get => GetValue(() => Status);
        set => SetValue(() => Status, value);
    }

    /// <summary>A request of the last update failed: the refresh button is shown.</summary>
    public bool Failed
    {
        get => GetValue(() => Failed);
        set => SetValue(() => Failed, value);
    }

    public ICommand OpenSettingsCommand { get; }

    /// <summary>Requests the rates at once instead of waiting for the retry.</summary>
    public ICommand RefreshCommand { get; }

    /// <summary>Runs on the UI thread: the requests are awaited there and the rows are updated in place.</summary>
    internal void Start()
    {
        if (IsEmpty)
            return;
        _cancellation?.Cancel();
        _cancellation = new CancellationTokenSource();
        _ = RunAsync(_cancellation.Token);
    }

    internal void Stop()
    {
        _cancellation?.Cancel();
        _cancellation = null;
    }

    /// <summary>Starts the loop over, so the rates are requested at once; the rows keep their values meanwhile.</summary>
    private void Refresh()
    {
        if (IsEmpty || _cancellation == null)
            return;
        _logger?.Log(WidgetLogLevel.Information, "Refresh requested.");
        Status = Strings.Status_Loading;
        Failed = false;
        Start();
    }

    private async Task RunAsync(CancellationToken token)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(_config.UpdateInterval, Config.MinUpdateInterval));
        while (!token.IsCancellationRequested)
        {
            var updated = await UpdateAsync(token);
            if (token.IsCancellationRequested)
                return;
            try
            {
                await WallClock.DelayAsync(updated || interval < RetryDelay ? interval : RetryDelay, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>One request per base currency. A failed request leaves its rows as they were.</summary>
    /// <returns>false: at least one request failed.</returns>
    private async Task<bool> UpdateAsync(CancellationToken token)
    {
        var from = DateOnly.FromDateTime(DateTime.Today).AddDays(-HistoryDays);
        var failed = false;
        foreach (var group in Rates.GroupBy(rate => rate.Pair.Base.ToUpperInvariant()))
        {
            var quotes = group.Select(rate => rate.Pair.Quote.ToUpperInvariant()).Distinct().ToArray();
            IReadOnlyList<RatePoint> points;
            try
            {
                points = await RatesClient.GetAsync(group.Key, quotes, from, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception exception)
            {
                failed = true;
                _logger?.Log(WidgetLogLevel.Warning, $"Rates of {group.Key} to {string.Join(',', quotes)} were not updated.", exception);
                continue;
            }

            foreach (var rate in group)
            {
                var series = points
                    .Where(point => string.Equals(point.Quote, rate.Pair.Quote, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(point => point.Date)
                    .ToArray();
                // Not a failure to retry: the source does not price this pair, the row keeps its dash.
                if (series.Length == 0)
                {
                    _logger?.Log(WidgetLogLevel.Warning, $"No rate of {group.Key} to {rate.Pair.Quote} in the answer.");
                    continue;
                }

                var last = series[^1];
                rate.Show(last.Rate, series.Length > 1 ? series[^2].Rate : null);
                if (_date == null || last.Date > _date)
                    _date = last.Date;
                _updated = DateTime.Now;
            }
        }

        if (!failed)
            _logger?.Log(WidgetLogLevel.Debug, $"Rates of {Rates.Count} pairs updated, as of {_date:yyyy-MM-dd}.");
        ShowStatus(failed);
        return !failed;
    }

    private void ShowStatus(bool failed)
    {
        var date = _date?.ToString("d", CultureInfo.CurrentCulture);
        var updated = FormatUpdated(_updated);
        Failed = failed;
        Status = (failed, date) switch
        {
            (false, null) => null,
            (true, null) => Strings.Status_Failed,
            (true, _) => string.Format(CultureInfo.CurrentCulture, Strings.Status_FailedAsOf, date, updated),
            _ => string.Format(CultureInfo.CurrentCulture, Strings.Status_AsOf, date, updated)
        };
    }

    /// <summary>Time only for today, the date as well for an older update.</summary>
    private static string? FormatUpdated(DateTime? updated) =>
        updated?.ToString(updated.Value.Date == DateTime.Today ? "t" : "g", CultureInfo.CurrentCulture);
}
