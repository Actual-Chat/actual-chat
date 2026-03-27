using ActualChat.Testing.Host;

namespace ActualChat.UI.Blazor.IntegrationTests;

[Collection(nameof(UICollection))]
public class ExampleTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public Task SessionTest()
    {
        var session = Session.New();
        Assert.NotNull(session);
        session.ToString().Length.Should().BeOneOf(20, 24); // Recently we switched to 24-char IDs
        return Task.CompletedTask;
    }
}
