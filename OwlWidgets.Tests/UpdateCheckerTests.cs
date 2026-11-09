using System.Net.Http;
using OwlWidgets.Properties;
using OwlWidgets.Settings;
using OwlWidgets.Updates;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace OwlWidgets.Tests;

public sealed class SemanticVersionTests
{
    [Theory]
    [InlineData("0.12.1", "0.12.0")]
    [InlineData("0.13.0", "0.12.9")]
    [InlineData("1.0.0", "0.99.99")]
    [InlineData("0.10.0", "0.9.0")]
    [InlineData("1.0.0", "1.0.0-rc.1")]
    [InlineData("1.0.0-rc.2", "1.0.0-rc.1")]
    [InlineData("1.0.0-rc.10", "1.0.0-rc.2")]
    [InlineData("1.0.0-beta", "1.0.0-alpha.1")]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha")]
    [InlineData("1.0.0-alpha.beta", "1.0.0-alpha.1")]
    public void Versions_are_ordered_by_SemVer(string higher, string lower)
    {
        Assert.True(SemanticVersion.Parse(higher) > SemanticVersion.Parse(lower));
        Assert.True(SemanticVersion.Parse(lower) < SemanticVersion.Parse(higher));
    }

    [Theory]
    [InlineData("v0.13.0", "0.13.0")]
    [InlineData("V1.2.3-rc.1", "1.2.3-rc.1")]
    [InlineData("1.2.3+abc", "1.2.3")]
    public void A_release_tag_is_read_as_a_version(string tag, string version) =>
        Assert.Equal(version, SemanticVersion.Parse(tag).ToString());

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("1.2.x")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3-rc..1")]
    public void Not_a_version(string text) => Assert.False(SemanticVersion.TryParse(text, out _));
}

public sealed class ReleaseInfoTests
{
    [Fact]
    public void The_answer_of_GitHub_gives_the_version_and_the_page()
    {
        var release = ReleaseInfo.FromGitHub("""
            {"tag_name": "v0.13.0", "html_url": "https://github.com/snowy-owll/OwlWidgets/releases/tag/v0.13.0", "draft": false}
            """);

        Assert.Equal(new SemanticVersion(0, 13, 0), release.Version);
        Assert.Equal("https://github.com/snowy-owll/OwlWidgets/releases/tag/v0.13.0", release.PageUrl);
    }

    [Fact]
    public void A_page_outside_the_repository_is_replaced_by_the_list_of_releases()
    {
        var release = ReleaseInfo.FromGitHub("""{"tag_name": "0.13.0", "html_url": "https://example.com/owl"}""");

        Assert.Equal("https://github.com/snowy-owll/OwlWidgets/releases", release.PageUrl);
    }

    [Fact]
    public void A_tag_that_is_not_a_version_is_an_error() =>
        Assert.Throws<FormatException>(() => ReleaseInfo.FromGitHub("""{"tag_name": "nightly"}"""));
}

public sealed class UpdateCheckerTests : IDisposable
{
    private readonly ManagerFixture _fixture = new([]);

    public void Dispose() => _fixture.Dispose();

    private static ReleaseInfo Release(string version) =>
        new(SemanticVersion.Parse(version), $"https://github.com/snowy-owll/OwlWidgets/releases/tag/v{version}");

    [Fact]
    public async Task A_newer_release_is_available_and_told_about_once()
    {
        _fixture.Releases.Latest = Release("0.13.0");
        using var updates = _fixture.CreateUpdates("0.12.1");

        await updates.CheckAsync(requested: false);

        Assert.Equal(UpdateStatus.Available, updates.Status);
        Assert.Equal("0.13.0", updates.Available?.Version.ToString());
        var shown = Assert.Single(_fixture.Notifications.Shown);
        Assert.Equal(string.Format(Strings.Update_Notification, "0.13.0"), shown.Message);

        shown.Clicked!();
        Assert.Equal(["https://github.com/snowy-owll/OwlWidgets/releases/tag/v0.13.0"], _fixture.Shell.OpenedWebPages);

        // The next start of the manager knows the version was told about.
        using var restarted = _fixture.CreateUpdates("0.12.1");
        await restarted.CheckAsync(requested: false);
        Assert.Equal(UpdateStatus.Available, restarted.Status);
        Assert.Single(_fixture.Notifications.Shown);
    }

    [Fact]
    public async Task A_check_on_request_does_not_notify_and_the_version_is_not_told_about_later()
    {
        _fixture.Releases.Latest = Release("0.13.0");
        using var updates = _fixture.CreateUpdates("0.12.1");

        await updates.CheckAsync(requested: true);
        await updates.CheckAsync(requested: false);

        Assert.Equal(UpdateStatus.Available, updates.Status);
        Assert.Empty(_fixture.Notifications.Shown);
    }

    [Theory]
    [InlineData("0.12.1")]
    [InlineData("0.12.0")]
    public async Task The_same_or_an_older_release_is_up_to_date(string latest)
    {
        _fixture.Releases.Latest = Release(latest);
        using var updates = _fixture.CreateUpdates("0.12.1");

        await updates.CheckAsync(requested: false);

        Assert.Equal(UpdateStatus.UpToDate, updates.Status);
        Assert.Null(updates.Available);
        Assert.NotNull(updates.LastChecked);
        Assert.Empty(_fixture.Notifications.Shown);
    }

    [Fact]
    public async Task No_published_release_is_up_to_date()
    {
        using var updates = _fixture.CreateUpdates();

        await updates.CheckAsync(requested: false);

        Assert.Equal(UpdateStatus.UpToDate, updates.Status);
    }

    [Fact]
    public async Task A_failed_request_is_shown_and_the_release_page_falls_back_to_the_list()
    {
        _fixture.Releases.Failure = new HttpRequestException("No network");
        using var updates = _fixture.CreateUpdates();

        await updates.CheckAsync(requested: true);

        Assert.Equal(UpdateStatus.Failed, updates.Status);
        Assert.NotNull(updates.LastChecked);
        updates.OpenReleasePage();
        Assert.Equal(["https://github.com/snowy-owll/OwlWidgets/releases"], _fixture.Shell.OpenedWebPages);
    }

    [Fact]
    public async Task An_automatic_check_is_due_once_a_day_and_only_while_the_setting_is_on()
    {
        using var updates = _fixture.CreateUpdates();

        await updates.CheckIfDueAsync();
        await updates.CheckIfDueAsync();
        Assert.Equal(1, _fixture.Releases.Requests);

        using var off = CreateWithSetting(checkForUpdates: false);
        await off.CheckIfDueAsync();
        Assert.Equal(1, _fixture.Releases.Requests);
    }

    [Fact]
    public async Task The_About_screen_follows_the_checker()
    {
        _fixture.Releases.Latest = Release("0.13.0");
        using var updates = _fixture.CreateUpdates("0.12.1");
        var about = _fixture.CreateAbout(updates);
        Assert.Equal(Strings.About_UpdateNotChecked, about.UpdateText);
        Assert.False(about.IsUpdateChecked);

        await about.CheckUpdatesCommand.ExecuteAsync();

        Assert.True(about.IsUpdateAvailable);
        Assert.True(about.IsUpdateChecked);
        Assert.Equal(string.Format(Strings.About_UpdateAvailable, "0.13.0"), about.UpdateText);
        about.OpenReleaseCommand.Execute(null);
        Assert.Equal(["https://github.com/snowy-owll/OwlWidgets/releases/tag/v0.13.0"], _fixture.Shell.OpenedWebPages);
    }

    private UpdateChecker CreateWithSetting(bool checkForUpdates)
    {
        var settings = new ManagerSettingsStore(_fixture.Paths, NullLogger<ManagerSettingsStore>.Instance);
        settings.Load();
        settings.Current.CheckForUpdates = checkForUpdates;
        return new UpdateChecker(_fixture.Releases, settings, _fixture.Notifications, _fixture.Shell, _fixture.Ui,
            NullLogger<UpdateChecker>.Instance, "0.12.1");
    }
}
