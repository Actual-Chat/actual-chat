using ActualChat.Testing.Host;

namespace ActualChat.UI.Blazor.IntegrationTests;

[Collection(nameof(UICollection)), Trait("Category", nameof(UICollection))]
public class ExampleTest(AppHostFixture fixture, ITestOutputHelper @out)
{
    private TestAppHost Host => fixture.Host;
    private ITestOutputHelper Out { get; } = fixture.Host.SetOutput(@out);

    [Fact]
    public Task SessionTest()
    {
        var appHost = Host;
        var session = Session.New();

        Assert.NotNull(session);
        session.ToString().Length.Should().Be(20);
        return Task.CompletedTask;
    }
}
