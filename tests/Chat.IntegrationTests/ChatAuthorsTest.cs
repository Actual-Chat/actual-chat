using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

public class ChatAuthorsTest : AppHostTestBase
{
    public ChatAuthorsTest(ITestOutputHelper @out) : base(@out) { }

    [Fact]
    public async Task NullAuthorResult()
    {
        using var appHost = await NewAppHost();
        using var tester = appHost.NewWebClientTester();
        var session = tester.Session;

        var chatAuthors = tester.ClientServices.GetRequiredService<IChatAuthors>();
        var author = await chatAuthors.Get(session, Constants.Chat.DefaultChatId, default);
        author.Should().BeNull();
    }
}
