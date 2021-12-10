using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

public class ChatEntryReaderTest : AppHostTestBase
{
    private const string ChatId = "the-actual-one";

    public ChatEntryReaderTest(ITestOutputHelper @out) : base(@out) { }

    [Fact]
    public async Task BasicTest()
    {
        using var appHost = await TestHostFactory.NewAppHost();
        using var tester = appHost.NewWebClientTester();
        var user = tester.SignIn(new User("", "reader-test-user")).ConfigureAwait(false);
        var session = tester.Session;

        var chats = tester.ClientServices.GetRequiredService<IChats>();

        var chat = await chats.Get(session, ChatId, CancellationToken.None);
        chat.Should().NotBeNull();
        chat?.Title.Should().Be("The Actual One");

        await AddChatEntries(chats, session, ChatId, CancellationToken.None);
        var idRange = await chats.GetIdRange(session, ChatId, ChatEntryType.Text, CancellationToken.None);

        var chuckBerryId = idRange.End - 1;
        var nirvanaId = chuckBerryId - 1;
        var acDcId = nirvanaId - 1;

        var reader = chats.CreateEntryReader(session, ChatId, ChatEntryType.Text);

        var entry = await reader.Get(acDcId, CancellationToken.None);
        entry.Should().NotBeNull();
        entry!.Content.Should().Be("back in black i hit the sack");

        entry = await reader.Get(nirvanaId, CancellationToken.None);
        entry.Should().NotBeNull();
        entry!.Content.Should().Be("rape me rape me my friend");

        entry = await reader.Get(chuckBerryId, CancellationToken.None);
        entry.Should().NotBeNull();
        entry!.Content.Should().Be("it was a teenage wedding and the all folks wished them well");
    }

    private async Task AddChatEntries(IChats chats, Session session, string chatId, CancellationToken cancellationToken)
    {
        var phrases = new[] {
            "back in black i hit the sack",
            "rape me rape me my friend",
            "it was a teenage wedding and the all folks wished them well",
        };
        foreach (var text in phrases)
            _ = await chats.CreateEntry(new (session, chatId, text), cancellationToken).ConfigureAwait(false);
    }
}
