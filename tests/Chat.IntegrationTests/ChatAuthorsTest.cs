using ActualChat.Testing.Host;
using Microsoft.Extensions.DependencyInjection;

namespace ActualChat.Chat.IntegrationTests;

public class ChatAuthorsTest
{
    [Fact]
    public async Task NullAuthorResult()
    {
        using var appHost = await TestHostFactory.NewAppHost();
        using var tester = appHost.NewWebClientTester();
        var session = tester.Session;

        var chatAuthors = tester.ClientServices.GetRequiredService<IChatAuthors>();
        var author = await chatAuthors.GetSessionChatAuthor(session, ChatConstants.DefaultChatId, default);
        author.Should().BeNull();
    }
}
