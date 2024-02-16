using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class AuthorsTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact(Skip = "Fails on CI")]
    public async Task NullAuthorResult()
    {
        var startedAt = CpuTimestamp.Now;
        var appHost = AppHost;
        using var tester = appHost.NewWebClientTester(Out);
        Out.WriteLine($"{startedAt}: app host init");
        var session = tester.Session;

        var authors = tester.ClientServices.GetRequiredService<IAuthors>();
        var author = await authors.GetOwn(session, Constants.Chat.DefaultChatId, default);
        Out.WriteLine($"{startedAt}: get author");
        author.Should().BeNull();
    }
}
