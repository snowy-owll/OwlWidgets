using OwlWidgets.Contracts;
using Xunit;

namespace OwlWidgets.Tests;

/// <summary>The anchor corner of a stopped instance: the card has no wrapper to ask, the last reported size converts.</summary>
public sealed class AnchorTests
{
    private readonly FakeDescriptor _descriptor = new();

    [Fact]
    public void A_new_anchor_gives_the_position_of_that_corner_of_the_same_card()
    {
        using var fixture = Fixture(width: 300.4, height: 180);
        var instance = fixture.CreateInstance(0);

        instance.Anchor = CardCorner.BottomRight;

        Assert.Equal(CardCorner.BottomRight, instance.Session.Anchor);
        Assert.Equal(400, instance.Session.X);
        Assert.Equal(230, instance.Session.Y);
        Assert.Equal("400", instance.PositionX);
        Assert.Equal("230", instance.PositionY);
        var stored = fixture.Stored(instance.Session.InstanceId);
        Assert.Equal(CardCorner.BottomRight, stored.Anchor);
        Assert.Equal(400, stored.X);
        Assert.Equal(230, stored.Y);
    }

    [Fact]
    public void Going_back_to_the_first_corner_gives_the_first_position()
    {
        using var fixture = Fixture(width: 300, height: 180);
        var session = fixture.Session(0);

        session.SetAnchor(CardCorner.TopRight);
        session.SetAnchor(CardCorner.BottomLeft);
        session.SetAnchor(CardCorner.TopLeft);

        Assert.Equal(100, session.X);
        Assert.Equal(50, session.Y);
    }

    [Fact]
    public void Without_a_reported_size_the_numbers_stay()
    {
        using var fixture = Fixture(width: 0, height: 0);
        var session = fixture.Session(0);

        session.SetAnchor(CardCorner.BottomRight);

        Assert.Equal(CardCorner.BottomRight, session.Anchor);
        Assert.Equal(100, session.X);
        Assert.Equal(50, session.Y);
    }

    [Fact]
    public void A_draft_in_the_fields_gives_way_to_the_new_corner()
    {
        using var fixture = Fixture(width: 300, height: 180);
        var instance = fixture.CreateInstance(0);
        instance.PositionX = "7";

        instance.Anchor = CardCorner.TopRight;

        Assert.Equal("400", instance.PositionX);
        Assert.False(instance.PositionChanged);
    }

    private ManagerFixture Fixture(double width, double height)
    {
        var package = _descriptor.ToPackage();
        var record = ManagerFixture.Record(package, x: 100, y: 50);
        record.CardWidth = width;
        record.CardHeight = height;
        return new ManagerFixture([package], record);
    }
}
