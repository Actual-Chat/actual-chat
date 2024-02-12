using ActualChat.Testing.Host;
using ActualLab.Mathematics;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection)), Trait("Category", nameof(ChatCollection))]
public class ChatEntryReaderTest(AppHostFixture fixture, ITestOutputHelper @out)
{
    private TestAppHost Host => fixture.Host;
    private ITestOutputHelper Out { get; } = fixture.Host.SetOutput(@out);
    private ChatId TestChatId { get; } = new("the-actual-one");

    [Fact(Skip = "Flaky")]
    public async Task BasicTest()
    {
        var appHost = Host;
        await using var tester = appHost.NewWebClientTester(Out);
        var services = tester.AppServices;
        var account = await tester.SignInAsBob();
        var session = tester.Session;

        var auth = services.GetRequiredService<IAuth>();
        var u = await auth.GetUser(session, CancellationToken.None);
        u!.Id.Should().Be(account.Id);
        u.Name.Should().Be(account.User.Name);

        var chats = services.GetRequiredService<IChats>();
        var chat = await chats.Get(session, TestChatId, CancellationToken.None);
        chat.Should().NotBeNull();
        chat?.Title.Should().Be("The Actual One");

        await CreateChatEntries(chats, session, TestChatId, 3);
        var idRange = await chats.GetIdRange(session, TestChatId, ChatEntryKind.Text, CancellationToken.None);
        var chuckBerryId = idRange.End - 1;
        var nirvanaId = chuckBerryId - 1;
        var acDcId = nirvanaId - 1;

        var reader = chats.NewEntryReader(session, TestChatId, ChatEntryKind.Text);

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

    [Fact(Skip = "Flaky")]
    public async Task FindByMinBeginsAtTest()
    {
        var appHost = Host;
        await using var tester = appHost.NewWebClientTester(Out);
        var services = tester.AppServices;
        var account = await tester.SignInAsBob();
        var session = tester.Session;
        var clocks = services.Clocks().SystemClock;

        var chats = services.GetRequiredService<IChats>();
        var chat = await chats.Get(session, TestChatId, CancellationToken.None);
        chat.Should().NotBeNull();
        chat?.Title.Should().Be("The Actual One");

        await CreateChatEntries(chats, session, TestChatId, 3);
        var idRange = await chats.GetIdRange(session, TestChatId, ChatEntryKind.Text, CancellationToken.None);

        var reader = chats.NewEntryReader(session, TestChatId, ChatEntryKind.Text);
        var entry = await reader.FindByMinBeginsAt(clocks.Now + TimeSpan.FromDays(1), idRange, CancellationToken.None);
        entry.Should().BeNull();
        entry = await reader.FindByMinBeginsAt(default, idRange, CancellationToken.None);
        entry!.Id.Should().Be(idRange.Start);

        for (var entryId = idRange.End - 3; entryId < idRange.End; entryId++) {
            entry = await reader.Get(entryId, CancellationToken.None);
            var eCopy = await reader.FindByMinBeginsAt(entry!.BeginsAt, idRange, CancellationToken.None);
            eCopy!.Id.Should().Be(entryId);
            eCopy = await reader.FindByMinBeginsAt(entry.BeginsAt, (entry.LocalId, entry.LocalId + 1), CancellationToken.None);
            eCopy!.Id.Should().Be(entryId);
            eCopy = await reader.FindByMinBeginsAt(entry.BeginsAt, (entry.LocalId, entry.LocalId + 2), CancellationToken.None);
            eCopy!.Id.Should().Be(entryId);
            eCopy = await reader.FindByMinBeginsAt(entry.BeginsAt, (entry.LocalId, entry.LocalId), CancellationToken.None);
            eCopy.Should().BeNull();
            eCopy = await reader.FindByMinBeginsAt(entry.BeginsAt, (entry.LocalId - 1, entry.LocalId), CancellationToken.None);
            eCopy.Should().BeNull();
            eCopy = await reader.FindByMinBeginsAt(entry.BeginsAt, (entry.LocalId - 2, entry.LocalId), CancellationToken.None);
            eCopy.Should().BeNull();
        }
    }

    [Fact(Skip = "Flaky")]
    public async Task ReadTilesTest()
    {
        var appHost = Host;
        await using var tester = appHost.NewWebClientTester(Out);
        var services = tester.AppServices;
        var account = await tester.SignInAsBob();
        var session = tester.Session;

        var auth = services.GetRequiredService<IAuth>();
        var u = await auth.GetUser(session, CancellationToken.None);
        u!.Id.Should().Be(account.Id);
        u.Name.Should().Be(account.User.Name);

        var chats = services.GetRequiredService<IChats>();
        var chat = await chats.Get(session, TestChatId, CancellationToken.None);
        chat.Should().NotBeNull();
        chat?.Title.Should().Be("The Actual One");

        await CreateChatEntries(chats, session, TestChatId, 3);
        var idRange = await chats.GetIdRange(session, TestChatId, ChatEntryKind.Text, CancellationToken.None);
        var chuckBerryId = idRange.End - 1;
        var nirvanaId = chuckBerryId - 1;
        var acDcId = nirvanaId - 1;

        var reader = chats.NewEntryReader(session, TestChatId, ChatEntryKind.Text);
        var tiles = Constants.Chat.ReaderIdTileStack.FirstLayer.GetCoveringTiles(new Range<long>(acDcId, chuckBerryId));
        var result = await reader.ReadTiles(new Range<long>(tiles[0].Start, tiles[^1].End), CancellationToken.None).ToListAsync();
        result.Count.Should().BeGreaterThan(0);
        result.Count.Should().BeLessThanOrEqualTo(2);
        result[0].Should().NotBeNull();
        result[0].Entries.Count.Should().BeGreaterThan(3);
    }

    [Fact(Skip = "Flaky")]
    public async Task ShouldReadTilesReverse()
    {
        var appHost = Host;
        await using var tester = appHost.NewWebClientTester(Out);

        var services = tester.AppServices;
        var account = await tester.SignInAsBob();
        var session = tester.Session;

        var auth = services.GetRequiredService<IAuth>();
        var u = await auth.GetUser(session, CancellationToken.None);
        u!.Id.Should().Be(account.Id);
        u.Name.Should().Be(account.User.Name);

        var chats = services.GetRequiredService<IChats>();
        var chat = await chats.Get(session, TestChatId, CancellationToken.None);
        chat.Should().NotBeNull();
        chat?.Title.Should().Be("The Actual One");

        await CreateChatEntries(chats, session, TestChatId, 3);
        var idRange = await chats.GetIdRange(session, TestChatId, ChatEntryKind.Text, CancellationToken.None);
        var idTileRange = ChatEntryReader.IdTileStack.LastLayer;
        var reader = chats.NewEntryReader(session, TestChatId, ChatEntryKind.Text, idTileRange);
        var tiles = await reader.ReadTilesReverse(idRange, CancellationToken.None).ToListAsync();

        tiles.Should().HaveCount(2);
        tiles[0]
            .Entries
            .TakeLast(3)
            .Select(x => x.Content)
            .Should()
            .BeEquivalentTo("back in black i hit the sack",
                "rape me rape me my friend",
                "it was a teenage wedding and the all folks wished them well");
    }

    [Theory(Skip = "Flaky")]
    [InlineData(0, "it was a teenage wedding and the all folks wished them well")]
    [InlineData(1, "rape me rape me my friend")]
    [InlineData(2, "back in black i hit the sack")]
    [InlineData(3, null)]
    public async Task GetLastShouldSkipDeleted(int removeLastCount, string? expected)
    {
        var appHost = Host;
        await using var tester = appHost.NewWebClientTester(Out);

        var services = tester.AppServices;
        var account = await tester.SignInAsBob();
        var session = tester.Session;

        var auth = services.GetRequiredService<IAuth>();
        var u = await auth.GetUser(session, CancellationToken.None);
        u!.Id.Should().Be(account.Id);
        u.Name.Should().Be(account.User.Name);

        var chats = services.GetRequiredService<IChats>();
        var chat = await chats.Get(session, TestChatId, CancellationToken.None);
        chat.Should().NotBeNull();
        chat?.Title.Should().Be("The Actual One");

        await CreateChatEntries(chats, session, TestChatId, 3);
        var author = await services.GetRequiredService<IAuthors>()
            .GetOwn(session, TestChatId, CancellationToken.None)
            .Require();
        var idRange = await chats.GetIdRange(session, TestChatId, ChatEntryKind.Text, CancellationToken.None);
        var reader = chats.NewEntryReader(session, TestChatId, ChatEntryKind.Text);
        var tile = await reader.ReadTilesReverse(idRange, CancellationToken.None).FirstAsync();
        foreach (var chatEntry in tile.Entries.TakeLast(removeLastCount))
            await services.Commander().Call(new Chats_RemoveTextEntry(session, TestChatId, chatEntry.LocalId), CancellationToken.None);

        var entry = await reader.GetLast(idRange, x => x.AuthorId == author.Id, 0, CancellationToken.None);
        entry?.Content.Should().Be(expected);
    }

    [Fact(Skip = "Flaky")]
    public async Task ObserveTest1()
    {
        var appHost = Host;
        await using var tester = appHost.NewWebClientTester(Out);
        var services = tester.AppServices;
        var account = await tester.SignInAsBob();
        var session = tester.Session;

        var auth = services.GetRequiredService<IAuth>();
        var u = await auth.GetUser(session, CancellationToken.None);
        u!.Id.Should().Be(account.Id);
        u.Name.Should().Be(account.User.Name);

        var chats = services.GetRequiredService<IChats>();
        var chat = await chats.Get(session, TestChatId, CancellationToken.None);
        chat.Should().NotBeNull();
        chat?.Title.Should().Be("The Actual One");

        var reader = chats.NewEntryReader(session, TestChatId, ChatEntryKind.Text);
        var idRange = await chats.GetIdRange(session, TestChatId, ChatEntryKind.Text, CancellationToken.None);

        { // Test 1
            using var cts = new CancellationTokenSource(500);
            var result = await reader.Observe(idRange.End, cts.Token).TrimOnCancellation().ToListAsync();
            result.Count.Should().Be(0);
        }

        { // Test 2
            using var cts = new CancellationTokenSource(500);
            var result = await reader.Observe(idRange.End - 1, cts.Token).TrimOnCancellation().ToListAsync();
            result.Count.Should().Be(1);
        }

        { // Test 3 + entry creation
            using var cts = new CancellationTokenSource(2000);
            var resultTask = reader.Observe(idRange.End - 1, cts.Token).TrimOnCancellation().ToListAsync();
            _ = BackgroundTask.Run(() => CreateChatEntries(
                    tester.AppServices.GetRequiredService<IChats>(), session, TestChatId,
                    (int)Constants.Chat.ReaderIdTileStack.MinTileSize));
            var result = await resultTask;
            result.Count.Should().Be(1 + (int)Constants.Chat.ReaderIdTileStack.MinTileSize);
        }
    }

    [Fact(Skip = "Flaky")]
    public async Task ObserveTest2()
    {
        var appHost = Host;
        await using var tester = appHost.NewWebClientTester(Out);
        var services = tester.AppServices;
        var account = await tester.SignInAsBob();
        var session = tester.Session;

        var auth = services.GetRequiredService<IAuth>();
        var u = await auth.GetUser(session, CancellationToken.None);
        u!.Id.Should().Be(account.Id);
        u.Name.Should().Be(account.User.Name);

        var chats = services.GetRequiredService<IChats>();
        var chat = await chats.Get(session, TestChatId, CancellationToken.None);
        chat.Should().NotBeNull();
        chat?.Title.Should().Be("The Actual One");

        var idRangeTask = chats.GetIdRange(session, TestChatId, ChatEntryKind.Text, CancellationToken.None);
        var reader = chats.NewEntryReader(session, TestChatId, ChatEntryKind.Text);

        { // Test 1
            using var cts = new CancellationTokenSource(2000);
            var idRange = await idRangeTask;
            var resultTask = reader.Observe(idRange.End - 1, cts.Token).TrimOnCancellation().ToListAsync();
            _ = BackgroundTask.Run(() => CreateChatEntries(
                    chats, session, TestChatId,
                    (int)Constants.Chat.ReaderIdTileStack.MinTileSize));
            var result = await resultTask;
            result.Count.Should().Be(1 + (int)Constants.Chat.ReaderIdTileStack.MinTileSize);
        }
    }

    private async Task CreateChatEntries(
        IChats chats,
        Session session,
        ChatId chatId,
        int count)
    {
        var phrases = new[] {
            "back in black i hit the sack",
            "rape me rape me my friend",
            "it was a teenage wedding and the all folks wished them well",
        };
        var commander = chats.GetCommander();

        while (true)
            foreach (var text in phrases) {
                if (count-- <= 0)
                    return;

                var command = new Chats_UpsertTextEntry(session, chatId, null, text);
                await commander.Call(command, CancellationToken.None).ConfigureAwait(false);
            }
    }
}
