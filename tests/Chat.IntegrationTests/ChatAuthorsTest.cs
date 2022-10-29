using System.Diagnostics;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

public class ChatAuthorsTest : AppHostTestBase
{
    public ChatAuthorsTest(ITestOutputHelper @out) : base(@out) { }

    [Fact]
    public async Task NullAuthorResult()
    {
        var sw = Stopwatch.StartNew();
        using var appHost = await NewAppHost();
        using var tester = appHost.NewWebClientTester();
        Out.WriteLine($"{sw.Elapsed}: app host init");
        var session = tester.Session;

        var chatAuthors = tester.ClientServices.GetRequiredService<IChatAuthors>();
        var author = await chatAuthors.GetOwn(session, Constants.Chat.DefaultChatId, default);
        Out.WriteLine($"{sw.Elapsed}: get author");
        author.Should().BeNull();
    }
}
