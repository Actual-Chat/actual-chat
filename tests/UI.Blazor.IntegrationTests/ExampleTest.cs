using ActualChat.Testing.Host;

namespace ActualChat.UI.Blazor.IntegrationTests;

[Collection(nameof(UICollection)), Trait("Category", nameof(UICollection))]
public class ExampleTest(AppHostFixture fixture, ITestOutputHelper @out)
    : AppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public Task SessionTest()
    {
        var session = Session.New();
        Assert.NotNull(session);
        session.ToString().Length.Should().Be(20);
        return Task.CompletedTask;
    }
}
