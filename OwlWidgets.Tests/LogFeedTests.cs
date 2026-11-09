using OwlWidgets.Contracts;
using OwlWidgets.Logging;
using OwlWidgets.Protocol;
using Xunit;

namespace OwlWidgets.Tests;

public sealed class LogFeedTests
{
    private readonly Guid _first = Guid.NewGuid();
    private readonly Guid _second = Guid.NewGuid();

    internal static LogRecord Record(string message, WidgetLogLevel level = WidgetLogLevel.Information) => new()
    {
        Timestamp = DateTimeOffset.Now,
        Level = level,
        Message = message
    };

    private static string[] Messages(IEnumerable<LogEntry> entries) => entries.Select(entry => entry.Record.Message).ToArray();

    [Fact]
    public void EachLogKeepsOnlyItsLatestRecords()
    {
        var feed = new LogFeed(managerCapacity: 2, instanceCapacity: 1);
        var changes = new List<LogFeedChange>();
        feed.Changed += changes.Add;

        feed.AddManager(Record("m1"), null);
        feed.AddManager(Record("m2"), null);
        feed.AddManager(Record("m3"), null);
        feed.AddInstance(_first, Record("a1"));
        feed.AddInstance(_first, Record("a2"));
        feed.AddInstance(_second, Record("b1"));

        Assert.Equal(["m2", "m3"], Messages(feed.Manager()));
        Assert.Equal(["a2"], Messages(feed.Instance(_first)));
        Assert.Equal(["b1"], Messages(feed.Instance(_second)));
        Assert.Equal("m1", changes[2].Evicted?.Record.Message);
        Assert.Equal("a1", changes[4].Evicted?.Record.Message);
        Assert.Null(changes[5].Evicted);
    }

    [Fact]
    public void AllLogsLeaveOutManagerCopiesOfInstanceRecords()
    {
        var feed = new LogFeed();
        var failure = Record("failed", WidgetLogLevel.Error);

        feed.AddManager(Record("manager"), null);
        feed.AddInstance(_first, failure);
        feed.AddManager(failure, _first);
        feed.AddInstance(_second, Record("second"));

        Assert.Equal(["manager", "failed", "second"], Messages(feed.All()));
        Assert.Equal(["manager", "failed"], Messages(feed.Manager()));
    }

    [Fact]
    public void RemovingAnInstanceDropsItsLogAndItsCopiesInTheManagerLog()
    {
        var feed = new LogFeed();
        feed.AddManager(Record("manager"), null);
        feed.AddManager(Record("first copy"), _first);
        feed.AddManager(Record("second copy"), _second);
        feed.AddInstance(_first, Record("first"));
        var changes = new List<LogFeedChange>();
        feed.Changed += changes.Add;

        feed.RemoveInstance(_first);

        Assert.Empty(feed.Instance(_first));
        Assert.Equal(["manager", "second copy"], Messages(feed.Manager()));
        Assert.Same(LogFeedChange.Reset, Assert.Single(changes));
    }

    [Fact]
    public void ClearingOneLogKeepsTheOthers()
    {
        var feed = new LogFeed();
        feed.AddManager(Record("manager"), null);
        feed.AddInstance(_first, Record("first"));

        feed.Clear(null);
        Assert.Empty(feed.Manager());
        Assert.Equal(["first"], Messages(feed.All()));

        feed.ClearAll();
        Assert.Empty(feed.All());
    }

    [Fact]
    public void ManagerLogPutsWrittenRecordsIntoTheFeed()
    {
        using var fixture = new ManagerFixture([]);
        var instanceId = Guid.NewGuid();

        fixture.Log.WriteManager(Record("manager"));
        fixture.Log.WriteInstance(instanceId, Record("instance"));
        // Below the minimum level of any build: not written, not kept.
        fixture.Log.WriteManager(Record("trace", WidgetLogLevel.Trace));

        Assert.Contains("manager", Messages(fixture.Feed.Manager()));
        Assert.DoesNotContain("trace", Messages(fixture.Feed.Manager()));
        Assert.Equal(["instance"], Messages(fixture.Feed.Instance(instanceId)));

        fixture.Log.RemoveInstance(instanceId);
        Assert.Empty(fixture.Feed.Instance(instanceId));
    }
}
