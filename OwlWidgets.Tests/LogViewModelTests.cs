using OwlWidgets.Contracts;
using OwlWidgets.Logging;
using OwlWidgets.Properties;
using OwlWidgets.ViewModels;
using Xunit;
using static OwlWidgets.Tests.LogFeedTests;

namespace OwlWidgets.Tests;

public sealed class LogViewModelTests : IDisposable
{
    private readonly FakeDescriptor _descriptor = new() { Name = "Clock" };
    private readonly ManagerFixture _fixture;

    public LogViewModelTests()
    {
        var package = _descriptor.ToPackage();
        _fixture = new ManagerFixture([package], ManagerFixture.Record(package), ManagerFixture.Record(package));
    }

    public void Dispose() => _fixture.Dispose();

    private Guid First => _fixture.Session(0).InstanceId;

    private Guid Second => _fixture.Session(1).InstanceId;

    private static string[] Lines(LogViewModel log) => log.Entries.Select(line => line.Message).ToArray();

    private static LogSourceViewModel SourceOf(LogViewModel log, Guid instanceId) =>
        log.Sources.Single(source => source.InstanceId == instanceId);

    [Fact]
    public void ThePickerOffersEveryLogTheManagerAndEachInstance()
    {
        _fixture.Session(1).Tag = "Kitchen";

        var log = _fixture.CreateLog();

        Assert.Equal([Strings.Log_All, Strings.Log_Manager, "Clock", "Kitchen"], log.Sources.Select(source => source.Title));
        Assert.Equal(LogSourceKind.All, log.SelectedSource?.Kind);
    }

    [Fact]
    public void ThePickerFollowsTheNamesOfInstances()
    {
        var log = _fixture.CreateLog();

        _fixture.Session(0).Tag = "Hall";

        Assert.Equal("Hall", SourceOf(log, First).Title);
    }

    [Fact]
    public async Task DeletingTheShownInstanceShowsEveryLog()
    {
        var log = _fixture.CreateLog();
        var deleted = Second;
        log.SelectedSource = SourceOf(log, deleted);

        await _fixture.Manager.DeleteAsync(_fixture.Session(1));

        Assert.Equal(LogSourceKind.All, log.SelectedSource?.Kind);
        Assert.DoesNotContain(log.Sources, source => source.InstanceId == deleted);
    }

    [Fact]
    public void NewRecordsOfTheShownLogAppearOnTheUiThread()
    {
        var log = _fixture.CreateLog();
        log.SelectedSource = SourceOf(log, First);
        Assert.True(log.IsEmpty);

        _fixture.Log.WriteInstance(First, Record("first"));
        _fixture.Log.WriteInstance(Second, Record("second"));
        Assert.Empty(log.Entries);

        _fixture.Ui.RunPending();

        Assert.Equal(["first"], Lines(log));
        Assert.False(log.IsEmpty);
        Assert.Equal("Clock", log.Entries[0].Origin);
    }

    [Fact]
    public void EveryLogShowsAnInstanceErrorOnce()
    {
        var log = _fixture.CreateLog();
        var failure = Record("failed", WidgetLogLevel.Error);

        _fixture.Log.WriteInstance(First, failure);
        _fixture.Log.WriteManager(failure, First);
        _fixture.Ui.RunPending();

        Assert.Equal(["failed"], Lines(log));

        // The manager log has its copy and names the instance.
        log.SelectedSource = log.Sources[1];
        var line = Assert.Single(log.Entries, entry => entry.Message == "failed");
        Assert.Equal("Clock", line.Origin);
    }

    [Fact]
    public void LevelsAndTextFilterTheLines()
    {
        _fixture.Log.WriteInstance(First, Record("started"));
        _fixture.Log.WriteInstance(First, Record("slow answer", WidgetLogLevel.Warning));
        _fixture.Log.WriteInstance(First, new() { Timestamp = DateTimeOffset.Now, Level = WidgetLogLevel.Error,
            Message = "crashed", Exception = "System.IO.IOException: disk" });
        var log = _fixture.CreateLog();
        log.SelectedSource = SourceOf(log, First);

        log.ShowInformation = false;
        Assert.Equal(["slow answer", "crashed"], Lines(log));

        log.SearchText = "ioexception";
        Assert.Equal(["crashed"], Lines(log));
        Assert.Contains("disk", log.Entries[0].FullText);

        log.ShowErrors = false;
        Assert.True(log.IsEmpty);

        // A record hidden by the filters stays hidden when it arrives.
        log.SearchText = string.Empty;
        _fixture.Log.WriteInstance(First, Record("ready"));
        _fixture.Ui.RunPending();
        Assert.Equal(["slow answer"], Lines(log));
    }

    [Fact]
    public void RecordsPushedOutOfTheFeedLeaveTheScreen()
    {
        var feed = new LogFeed(managerCapacity: 2, instanceCapacity: 2);
        var log = _fixture.CreateLog(feed);

        feed.AddInstance(First, Record("a1"));
        feed.AddInstance(First, Record("a2"));
        _fixture.Ui.RunPending();
        feed.AddInstance(First, Record("a3"));
        _fixture.Ui.RunPending();

        Assert.Equal(["a2", "a3"], Lines(log));
    }

    [Fact]
    public void ClearDropsOnlyTheShownLog()
    {
        _fixture.Log.WriteInstance(First, Record("first"));
        _fixture.Log.WriteInstance(Second, Record("second"));
        var log = _fixture.CreateLog();
        log.SelectedSource = SourceOf(log, First);

        log.ClearCommand.Execute(null);
        _fixture.Ui.RunPending();

        Assert.True(log.IsEmpty);
        Assert.Empty(_fixture.Feed.Instance(First));
        Assert.Single(_fixture.Feed.Instance(Second));
    }

    [Fact]
    public void ALineOpensInADialogThatCopiesItAsAFileLine()
    {
        _fixture.Log.WriteInstance(First, new() { Timestamp = DateTimeOffset.Now, Level = WidgetLogLevel.Error,
            Source = "Widget", Category = "ClockRuntime", Message = "crashed", Exception = "System.IO.IOException: disk" });
        var log = _fixture.CreateLog();

        log.ShowDetailsCommand.Execute(log.Entries.Single());

        var details = Assert.IsType<LogEntryDetailsViewModel>(Assert.Single(_fixture.Dialogs.Shown));
        Assert.Equal("Clock", details.Origin);
        Assert.Equal(First.ToString(), details.InstanceId);
        Assert.Equal("Widget", details.Source);
        Assert.Equal("ClockRuntime", details.Category);
        Assert.Equal("System.IO.IOException: disk", details.Exception);

        details.CopyCommand.Execute(null);
        var copied = _fixture.Clipboard.Text;
        Assert.NotNull(copied);
        Assert.Contains($"[ERR] [Widget] {{{First:N}}} ClockRuntime: crashed", copied);
        Assert.EndsWith("System.IO.IOException: disk", copied);

        details.CloseCommand.Execute(null);
        Assert.Equal(1, _fixture.Dialogs.CloseCount);
    }

    [Fact]
    public void AManagerLineHasNoInstanceId()
    {
        _fixture.Log.WriteManager(Record("started"));
        var log = _fixture.CreateLog();
        log.SelectedSource = log.Sources[1];

        log.ShowDetailsCommand.Execute(log.Entries.Single(line => line.Message == "started"));

        var details = Assert.IsType<LogEntryDetailsViewModel>(Assert.Single(_fixture.Dialogs.Shown));
        Assert.Equal(Strings.Log_Manager, details.Origin);
        Assert.False(details.HasInstanceId);
    }

    [Fact]
    public void TheFolderButtonOpensTheLogFolder()
    {
        var log = _fixture.CreateLog();

        log.OpenFolderCommand.Execute(null);

        Assert.Equal([_fixture.Log.Root], _fixture.Shell.OpenedFolders);
    }
}
