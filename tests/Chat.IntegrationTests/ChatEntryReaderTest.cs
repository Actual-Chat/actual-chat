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

        await CreateChatEntries(chats, session, ChatId, 3);
        var idRange = await chats.GetIdRange(session, ChatId, ChatEntryType.Text, CancellationToken.None);
        var chuckBerryId = idRange.End - 1;
        var nirvanaId = chuckBerryId - 1;
        var acDcId = nirvanaId - 1;

        var reader = chats.NewEntryReader(session, ChatId, ChatEntryType.Text);

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

        await CreateChatEntries(chats, session, ChatId, 3);
        var idRange = await chats.GetIdRange(session, ChatId, ChatEntryType.Text, CancellationToken.None);

        var reader = chats.NewEntryReader(session, ChatId, ChatEntryType.Text);
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
    public async Task ReadTilesTest()
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

        await CreateChatEntries(chats, session, ChatId, 3);
        var idRange = await chats.GetIdRange(session, ChatId, ChatEntryType.Text, CancellationToken.None);
        var chuckBerryId = idRange.End - 1;
        var nirvanaId = chuckBerryId - 1;
        var acDcId = nirvanaId - 1;

        var reader = chats.NewEntryReader(session, ChatId, ChatEntryType.Text);
        var tiles = Constants.Chat.IdTileStack.FirstLayer.GetCoveringTiles(new Range<long>(acDcId, chuckBerryId));
        var result = await reader.ReadTiles(new Range<long>(tiles[0].Start, tiles[^1].End), CancellationToken.None).ToListAsync();
        result.Count.Should().BeGreaterThan(0);
        result.Count.Should().BeLessThanOrEqualTo(2);
        result[0].Should().NotBeNull();
        result[0].Entries.Length.Should().BeGreaterThan(3);
    }

    [Fact]
    public async Task ObserveTest1()
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

        var reader = chats.NewEntryReader(session, ChatId, ChatEntryType.Text);
        var idRange = await chats.GetIdRange(session, ChatId, ChatEntryType.Text, CancellationToken.None).ConfigureAwait(false);

        using var cts1 = new CancellationTokenSource();
        cts1.CancelAfter(500);
        var result = await reader.Observe(idRange.End, cts1.Token).TrimOnCancellation().ToListAsync();
        result.Count.Should().Be(0);

        using var cts2 = new CancellationTokenSource();
        cts2.CancelAfter(500);
        result = await reader.Observe(idRange.End - 1, cts2.Token).TrimOnCancellation().ToListAsync();
        result.Count.Should().Be(1);

        using var cts3 = new CancellationTokenSource();
        var resultTask = reader.Observe(idRange.End - 1, cts3.Token).TrimOnCancellation().ToListAsync();
        _ = BackgroundTask.Run(() => CreateChatEntries(
                chats, session, ChatId,
                (int)Constants.Chat.IdTileStack.MinTileSize)
            .ContinueWith(_ => cts3.CancelAfter(500), CancellationToken.None));

        result = await resultTask;
        result.Count.Should().Be(1 + (int)Constants.Chat.IdTileStack.MinTileSize);
    }

    [Fact(Skip = "Doesn't work at CI since 09434ba5")]
    public async Task ObserveTest2()
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

        var idRange = chats.GetIdRange(session, ChatId, ChatEntryType.Text, CancellationToken.None);
        var reader = chats.NewEntryReader(session, ChatId, ChatEntryType.Text);

        using var cts2 = new CancellationTokenSource();
        var resultTask = reader.Observe(idRange.Result.End - 1, cts2.Token).TrimOnCancellation().ToListAsync();

        _ = BackgroundTask.Run(() => CreateChatEntries(
                chats, session, ChatId,
                (int) Constants.Chat.IdTileStack.MinTileSize)
            .ContinueWith(_ => cts2.CancelAfter(500), CancellationToken.None));

        var result = await resultTask;
        result.Count.Should().Be(1 + (int)Constants.Chat.IdTileStack.MinTileSize);
    }

    private async Task CreateChatEntries(
        IChats chats,
        Session session,
        string chatId,
        int count)
    {
        var phrases = new[] {
            "back in black i hit the sack",
            "rape me rape me my friend",
            "it was a teenage wedding and the all folks wished them well",
        };

        while (true)
            foreach (var text in phrases) {
                if (count-- <= 0)
                    return;
                await chats
                    .CreateTextEntry(new (session, chatId, text), CancellationToken.None)
                    .ConfigureAwait(false);
            }
    }
}
