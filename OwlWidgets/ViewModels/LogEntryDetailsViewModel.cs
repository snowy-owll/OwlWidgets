using System.Globalization;
using OwlWidgets.Contracts;
using OwlWidgets.Logging;
using OwlWidgets.Services;
using OwlWidgets.UI.Common;

namespace OwlWidgets.ViewModels;

/// <summary>
/// One log record in a dialog: every field in text boxes that can be selected and copied,
/// and the whole record copied at once as a line of the log file.
/// </summary>
internal sealed class LogEntryDetailsViewModel
{
    public LogEntryDetailsViewModel(LogEntryViewModel line, bool isDarkTheme, IDialogService dialogs, IClipboardService clipboard)
    {
        var entry = line.Entry;
        var record = entry.Record;
        var local = record.Timestamp.ToLocalTime();
        Level = record.Level;
        LevelText = line.LevelText;
        IsDarkTheme = isDarkTheme;
        Time = $"{local.ToString("d", CultureInfo.CurrentCulture)} {local.ToString("HH:mm:ss.fff zzz", CultureInfo.CurrentCulture)}";
        Origin = line.Origin;
        InstanceId = (entry.Log ?? entry.InstanceId)?.ToString();
        Source = record.Source;
        Category = record.Category;
        Message = record.Message;
        Exception = record.Exception;

        // The line as in the file: with the instance mark of the manager log, so it says which instance it is about.
        var text = LogLineFormatter.Format(record, entry.Log ?? entry.InstanceId).TrimEnd();
        CopyCommand = new RelayCommand(() => clipboard.SetText(text));
        CloseCommand = new RelayCommand(dialogs.Close);
    }

    public WidgetLogLevel Level { get; }

    public string LevelText { get; }

    public bool IsDarkTheme { get; }

    /// <summary>Date by the Windows regional settings, time with milliseconds and the offset from UTC.</summary>
    public string Time { get; }

    public string Origin { get; }

    /// <summary>The instance the record belongs to or concerns; null for a record of the manager alone.</summary>
    public string? InstanceId { get; }

    public bool HasInstanceId => InstanceId != null;

    public string Source { get; }

    public string Category { get; }

    public bool HasCategory => !string.IsNullOrEmpty(Category);

    public string Message { get; }

    public string? Exception { get; }

    public bool HasException => !string.IsNullOrEmpty(Exception);

    public RelayCommand CopyCommand { get; }

    public RelayCommand CloseCommand { get; }
}
