using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

public class AuthorsTest : AppHostTestBase
{
    public AuthorsTest(ITestOutputHelper @out) : base(@out) { }

    [Fact(Skip = "Fails on CI")]
    public async Task NullAuthorResult()
    {
        var startedAt = CpuTimestamp.Now;
        using var appHost = await NewAppHost();
        using var tester = appHost.NewWebClientTester();
        Out.WriteLine($"{startedAt}: app host init");
        var session = tester.Session;

        var authors = tester.ClientServices.GetRequiredService<IAuthors>();
        var author = await authors.GetOwn(session, Constants.Chat.DefaultChatId, default);
        Out.WriteLine($"{startedAt}: get author");
        author.Should().BeNull();
    }
}
