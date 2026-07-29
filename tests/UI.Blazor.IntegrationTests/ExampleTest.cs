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
        // Id, not ToString(): the latter is redacted to "{4-char prefix}:{Hash}" while
        // sanitization is active, so it stopped being a way to measure the Id
        session.Id.Length.Should().BeOneOf(20, 24); // Recently we switched to 24-char IDs
        return Task.CompletedTask;
    }
}
