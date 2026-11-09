using Xunit;

namespace OwlWidgets.Tests;

public sealed class AboutViewModelTests : IDisposable
{
    private readonly ManagerFixture _fixture = new([]);

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void TheProjectLinkOpensTheRepository()
    {
        var about = _fixture.CreateAbout();

        about.OpenRepositoryCommand.Execute(null);

        Assert.Equal(["https://github.com/snowy-owll/OwlWidgets"], _fixture.Shell.OpenedWebPages);
        Assert.Equal("github.com/snowy-owll/OwlWidgets", about.Repository);
    }
}
