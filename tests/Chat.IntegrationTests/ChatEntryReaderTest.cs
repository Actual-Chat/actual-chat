using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Testing.Host;
using Stl.Mathematics;

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


    [Fact]
    public async Task ReadAllTilesTest()
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
        var tiles = Constants.Chat.IdTileStack.FirstLayer.GetCoveringTiles(new Range<long>(acDcId, chuckBerryId));
        var result = await reader.ReadAllTiles(new Range<long>(tiles[0].Start, tiles[^1].End), CancellationToken.None).ToListAsync();
        result.Count.Should().BeGreaterThan(0);
        result.Count.Should().BeLessThanOrEqualTo(2);
        result[0].Value.Should().NotBeNull();
        result[0].Value.Entries.Length.Should().BeGreaterThan(3);
    }

    [Fact]
    public async Task ReadNewTilesTest()
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

        var reader = chats.CreateEntryReader(session, ChatId, ChatEntryType.Text);

        var cts1 = new CancellationTokenSource();
        cts1.CancelAfter(500);
        var result = await reader.ReadNewTiles(cts1.Token).TrimOnCancellation().ToListAsync();
        result.Count.Should().Be(0);

        var cts2 = new CancellationTokenSource();
        _ = Task.Run(() => AddChatEntries(chats,
                session,
                ChatId,
                CancellationToken.None,
                (int)Constants.Chat.IdTileStack.MinTileSize)
            .ContinueWith(_ => cts2.CancelAfter(500), CancellationToken.None));

        result = await reader.ReadNewTiles(cts2.Token).TrimOnCancellation().ToListAsync();
        result.Count.Should().Be(1);
        result[0].Value.Should().NotBeNull();
        result[0].Value.Entries.Length.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ReadAllUpdatesTest()
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

        var commander = tester.AppServices.GetRequiredService<ICommander>();
        var reader = chats.CreateEntryReader(session, ChatId, ChatEntryType.Text);
        await AddChatEntries(chats,
            session,
            ChatId,
            CancellationToken.None,
            1);
        var idRange = await chats.GetIdRange(session, ChatId, ChatEntryType.Text, CancellationToken.None);

        var updateCount = 0;
        var cts = new CancellationTokenSource();
        var id = idRange.End - 1;
        var updates = reader.ReadAllUpdates(
            id,
            (_,_) => true,
            cts.Token);
        await foreach (var entry in updates.TrimOnCancellation()) {
            entry.Id.Should().Be(id);
            var updated = entry with { Content = entry.Content + " 1" };
            var completeCommand = new IChatsBackend.UpsertEntryCommand(updated);
            await commander.Call(completeCommand, true, default).ConfigureAwait(false);
            if (++updateCount >= 3)
                cts.Cancel();
        }
    }

    private async Task AddChatEntries(IChats chats, Session session, string chatId, CancellationToken cancellationToken, int entryCount = 3)
    {
        var phrases = new[] {
            "back in black i hit the sack",
            "rape me rape me my friend",
            "it was a teenage wedding and the all folks wished them well",
        };

        var count = 0;
        while (true)
            foreach (var text in phrases) {
                await chats.CreateTextEntry(new (session, chatId, text), cancellationToken).ConfigureAwait(false);
                if (++count > entryCount)
                    return;
            }
    }
}
