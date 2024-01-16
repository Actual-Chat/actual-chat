using ActualChat.Testing.Host;

namespace ActualChat.UI.Blazor.IntegrationTests;

public class ExampleTest(ITestOutputHelper @out) : AppHostTestBase(@out)
{
    [Fact]
    public async Task SessionTest()
    {
        using var appHost = await NewAppHost();
        var session = Session.New();

        Assert.NotNull(session);
        session.ToString().Length.Should().Be(20);
    }
}
