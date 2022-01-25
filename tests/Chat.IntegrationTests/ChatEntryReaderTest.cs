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
        var services = tester.ClientServices;
        var user = await tester.SignIn(new User("", "Bob"));
        var session = tester.Session;

        var auth = services.GetRequiredService<IAuth>();
        var u = await auth.GetUser(session, CancellationToken.None);
        u.IsAuthenticated.Should().BeTrue();
        u.Id.Should().Be(user.Id);
        u.Name.Should().Be(user.Name);

        var chats = services.GetRequiredService<IChats>();
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

    [Fact]
    public async Task FindByMinBeginsAtTest()
    {
        using var appHost = await TestHostFactory.NewAppHost();
        using var tester = appHost.NewWebClientTester();
        var services = tester.AppServices;
        var user = await tester.SignIn(new User("", "Bob"));
        var session = tester.Session;
        var clocks = services.Clocks().SystemClock;

        var chats = services.GetRequiredService<IChats>();
        var chat = await chats.Get(session, ChatId, CancellationToken.None);
        chat.Should().NotBeNull();
        chat?.Title.Should().Be("The Actual One");

        await AddChatEntries(chats, session, ChatId, CancellationToken.None);
        var idRange = await chats.GetIdRange(session, ChatId, ChatEntryType.Text, CancellationToken.None);

        var reader = chats.CreateEntryReader(session, ChatId, ChatEntryType.Text);
        var entry = await reader.FindByMinBeginsAt(clocks.Now + TimeSpan.FromDays(1), idRange, CancellationToken.None);
        entry.Should().BeNull();
        entry = await reader.FindByMinBeginsAt(default, idRange, CancellationToken.None);
        entry!.Id.Should().Be(idRange.Start);

        for (var entryId = idRange.End - 3; entryId < idRange.End; entryId++) {
            entry = await reader.Get(entryId, CancellationToken.None);
            var beginsAt = entry!.BeginsAt;
            var eCopy = await reader.FindByMinBeginsAt(beginsAt, idRange, CancellationToken.None);
            eCopy!.Id.Should().Be(entryId);
            eCopy = await reader.FindByMinBeginsAt(entry.BeginsAt, (entry.Id, entry.Id + 1), CancellationToken.None);
            eCopy!.Id.Should().Be(entryId);
            eCopy = await reader.FindByMinBeginsAt(entry.BeginsAt, (entry.Id, entry.Id + 2), CancellationToken.None);
            eCopy!.Id.Should().Be(entryId);
            eCopy = await reader.FindByMinBeginsAt(entry.BeginsAt, (entry.Id, entry.Id), CancellationToken.None);
            eCopy.Should().BeNull();
            eCopy = await reader.FindByMinBeginsAt(entry.BeginsAt, (entry.Id - 1, entry.Id), CancellationToken.None);
            eCopy.Should().BeNull();
            eCopy = await reader.FindByMinBeginsAt(entry.BeginsAt, (entry.Id - 2, entry.Id), CancellationToken.None);
            eCopy.Should().BeNull();
        }
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
