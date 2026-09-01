using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class ChatEntryReaderTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private ChatId TestChatId { get; } = ChatId.Parse("the-actual-one");

    [FlakyFact("Flaky")]
    public async Task BasicTest()
    {
        var appHost = AppHost;
        await using var tester = appHost.NewWebClientTester(Out);
        var services = tester.AppServices;
        var account = await tester.SignInAsBob();
        var session = tester.Session;

        var accounts = services.GetRequiredService<IAccounts>();
        var ownAccount = await accounts.GetOwn(session, CancellationToken.None);
        ownAccount.Id.Should().Be(account.Id);
        ownAccount.Name.Should().Be(account.Name);

        var chats = services.GetRequiredService<IChats>();
        var chat = await chats.Get(session, TestChatId, CancellationToken.None);
        chat.Should().NotBeNull();
        chat?.Title.Should().Be("The Actual One");

        await services.GetRequiredService<IAuthors>().EnsureJoined(session, TestChatId, CancellationToken.None);
        await WaitForIdRangeToStabilize(chats, session, TestChatId);

        await CreateChatEntries(chats, session, TestChatId, 3);
        var idRange = await chats.GetIdRange(session, TestChatId, CancellationToken.None);
        var chuckBerryId = idRange.End - 1;
        var nirvanaId = chuckBerryId - 1;
        var acDcId = nirvanaId - 1;

        var reader = chats.NewEntryReader(session, TestChatId);

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

    [FlakyFact("Flaky")]
    public async Task FindByMinBeginsAtTest()
    {
        var appHost = AppHost;
        await using var tester = appHost.NewWebClientTester(Out);
        var services = tester.AppServices;
        var account = await tester.SignInAsBob();
        var session = tester.Session;
        var clocks = services.Clocks().SystemClock;

        var chats = services.GetRequiredService<IChats>();
        var chat = await chats.Get(session, TestChatId, CancellationToken.None);
        chat.Should().NotBeNull();
        chat?.Title.Should().Be("The Actual One");

        await services.GetRequiredService<IAuthors>().EnsureJoined(session, TestChatId, CancellationToken.None);
        await WaitForIdRangeToStabilize(chats, session, TestChatId);

        await CreateChatEntries(chats, session, TestChatId, 3);
        var idRange = await chats.GetIdRange(session, TestChatId, CancellationToken.None);

        var reader = chats.NewEntryReader(session, TestChatId);
        var entry = await reader.FindByMinBeginsAt(clocks.Now + TimeSpan.FromDays(1), idRange, CancellationToken.None);
        entry.Should().BeNull();
        entry = await reader.FindByMinBeginsAt(default, idRange, CancellationToken.None);
        entry!.Id.LocalId.Should().Be(idRange.Start);

        for (var entryLid = idRange.End - 3; entryLid < idRange.End; entryLid++) {
            entry = await reader.Get(entryLid, CancellationToken.None);
            var eCopy = await reader.FindByMinBeginsAt(entry!.BeginsAt, idRange, CancellationToken.None);
            eCopy!.Id.LocalId.Should().Be(entryLid);
            eCopy = await reader.FindByMinBeginsAt(entry.BeginsAt, (entry.LocalId, entry.LocalId + 1), CancellationToken.None);
            eCopy!.Id.LocalId.Should().Be(entryLid);
            eCopy = await reader.FindByMinBeginsAt(entry.BeginsAt, (entry.LocalId, entry.LocalId + 2), CancellationToken.None);
            eCopy!.Id.LocalId.Should().Be(entryLid);
            eCopy = await reader.FindByMinBeginsAt(entry.BeginsAt, (entry.LocalId, entry.LocalId), CancellationToken.None);
            eCopy.Should().BeNull();
            eCopy = await reader.FindByMinBeginsAt(entry.BeginsAt, (entry.LocalId - 1, entry.LocalId), CancellationToken.None);
            eCopy.Should().BeNull();
            eCopy = await reader.FindByMinBeginsAt(entry.BeginsAt, (entry.LocalId - 2, entry.LocalId), CancellationToken.None);
            eCopy.Should().BeNull();
        }
    }

    [FlakyFact("Flaky")]
    public async Task ReadTilesTest()
    {
        var appHost = AppHost;
        await using var tester = appHost.NewWebClientTester(Out);
        var services = tester.AppServices;
        var account = await tester.SignInAsBob();
        var session = tester.Session;

        var accounts = services.GetRequiredService<IAccounts>();
        var ownAccount = await accounts.GetOwn(session, CancellationToken.None);
        ownAccount.Id.Should().Be(account.Id);
        ownAccount.Name.Should().Be(account.Name);

        var chats = services.GetRequiredService<IChats>();
        var chat = await chats.Get(session, TestChatId, CancellationToken.None);
        chat.Should().NotBeNull();
        chat?.Title.Should().Be("The Actual One");

        await services.GetRequiredService<IAuthors>().EnsureJoined(session, TestChatId, CancellationToken.None);
        await WaitForIdRangeToStabilize(chats, session, TestChatId);

        await CreateChatEntries(chats, session, TestChatId, 3);
        var idRange = await chats.GetIdRange(session, TestChatId, CancellationToken.None);
        var chuckBerryId = idRange.End - 1;
        var nirvanaId = chuckBerryId - 1;
        var acDcId = nirvanaId - 1;

        var reader = chats.NewEntryReader(session, TestChatId);
        var tiles = Constants.Chat.EntryIdTiles.GetCoveringTiles(new Range<long>(acDcId, chuckBerryId));
        var result = await reader.ReadTiles(new Range<long>(tiles[0].Start, tiles[^1].End), CancellationToken.None).ToListAsync();
        result.Count.Should().BeGreaterThan(0);
        result.SelectMany(t => t.Entries).Should().NotBeEmpty();
        var totalEntries = result.Sum(t => t.Entries.Length);
        totalEntries.Should().BeGreaterThanOrEqualTo(2, "the tile range covers acDcId and nirvanaId, which may span multiple tiles");
    }

    [Fact]
    public async Task ShouldReadTilesReverse()
    {
        var appHost = AppHost;
        await using var tester = appHost.NewWebClientTester(Out);

        var services = tester.AppServices;
        var account = await tester.SignInAsBob();
        var session = tester.Session;

        var accounts = services.GetRequiredService<IAccounts>();
        var ownAccount = await accounts.GetOwn(session, CancellationToken.None);
        ownAccount.Id.Should().Be(account.Id);
        ownAccount.Name.Should().Be(account.Name);

        var chats = services.GetRequiredService<IChats>();
        var chat = await chats.Get(session, TestChatId, CancellationToken.None);
        chat.Should().NotBeNull();
        chat?.Title.Should().Be("The Actual One");

        await CreateChatEntries(chats, session, TestChatId, 3);
        var idRange = await chats.GetIdRange(session, TestChatId, CancellationToken.None);
        var reader = chats.NewEntryReader(session, TestChatId);
        var tiles = await reader.ReadTilesReverse(idRange, CancellationToken.None).ToListAsync();

        tiles.Should().HaveCountGreaterThan(400);
        var entries = tiles.SelectMany(t => t.Entries.Revert()).ToList();
        entries
            .Select(x => x.Content)
            .Where(x => !x.IsNullOrEmpty())
            .Take(3)
            .Should()
            .BeEquivalentTo(
                "it was a teenage wedding and the all folks wished them well",
                "rape me rape me my friend",
                "back in black i hit the sack");
    }

    [FlakyTheory("Flaky")]
    [InlineData(0, "it was a teenage wedding and the all folks wished them well")]
    [InlineData(1, "rape me rape me my friend")]
    [InlineData(2, "back in black i hit the sack")]
    [InlineData(3, null)]
    public async Task GetLastShouldSkipDeleted(int removeLastCount, string? expected)
    {
        var appHost = AppHost;
        await using var tester = appHost.NewWebClientTester(Out);

        var services = tester.AppServices;
        var account = await tester.SignInAsBob();
        var session = tester.Session;

        var accounts = services.GetRequiredService<IAccounts>();
        var ownAccount = await accounts.GetOwn(session, CancellationToken.None);
        ownAccount.Id.Should().Be(account.Id);
        ownAccount.Name.Should().Be(account.Name);

        var chats = services.GetRequiredService<IChats>();
        var chat = await chats.Get(session, TestChatId, CancellationToken.None);
        chat.Should().NotBeNull();
        chat?.Title.Should().Be("The Actual One");

        await services.GetRequiredService<IAuthors>().EnsureJoined(session, TestChatId, CancellationToken.None);
        await WaitForIdRangeToStabilize(chats, session, TestChatId);

        var idRangeBefore = await chats.GetIdRange(session, TestChatId, CancellationToken.None);
        await CreateChatEntries(chats, session, TestChatId, 3);
        var author = await services.GetRequiredService<IAuthors>()
            .GetOwn(session, TestChatId, CancellationToken.None)
            .Require();
        var idRange = await chats.GetIdRange(session, TestChatId, CancellationToken.None);
        var ourRange = new Range<long>(idRangeBefore.End, idRange.End);
        var reader = chats.NewEntryReader(session, TestChatId);

        for (var i = 0; i < removeLastCount; i++)
            await services.Commander().Call(new Chats_RemoveEntry {
                Session = session,
                ChatId = TestChatId,
                LocalId = idRange.End - 1 - i,
            }, CancellationToken.None);

        var entry = await reader.GetLast(ourRange, x => x.AuthorId == author.Id, 0, CancellationToken.None);
        entry?.Content.Should().Be(expected);
    }

    [FlakyFact("Flaky")]
    public async Task ObserveTest1()
    {
        var appHost = AppHost;
        await using var tester = appHost.NewWebClientTester(Out);
        var services = tester.AppServices;
        var account = await tester.SignInAsBob();
        var session = tester.Session;

        var accounts = services.GetRequiredService<IAccounts>();
        var ownAccount = await accounts.GetOwn(session, CancellationToken.None);
        ownAccount.Id.Should().Be(account.Id);
        ownAccount.Name.Should().Be(account.Name);

        var chats = services.GetRequiredService<IChats>();
        var chat = await chats.Get(session, TestChatId, CancellationToken.None);
        chat.Should().NotBeNull();
        chat?.Title.Should().Be("The Actual One");

        // Ensure Bob is joined before capturing idRange to avoid
        // a "member joined" system entry being created during CreateChatEntries.
        // EnsureJoined fires AuthorUpsertedEvent which creates a system entry asynchronously,
        // so we need to wait for the idRange to stabilize.
        await services.GetRequiredService<IAuthors>().EnsureJoined(session, TestChatId, CancellationToken.None);
        await WaitForIdRangeToStabilize(chats, session, TestChatId);

        var reader = chats.NewEntryReader(session, TestChatId);
        var idRange = await chats.GetIdRange(session, TestChatId, CancellationToken.None);

        { // Test 1
            using var cts = new CancellationTokenSource(500);
            var result = await reader.Observe(idRange.End, cts.Token).SuppressCancellation().ToListAsync();
            result.Count.Should().Be(0);
        }

        { // Test 2
            using var cts = new CancellationTokenSource(500);
            var result = await reader.Observe(idRange.End - 1, cts.Token).SuppressCancellation().ToListAsync();
            result.Count.Should().Be(1);
        }

        { // Test 3 + entry creation
            using var cts = new CancellationTokenSource(2000);
            var resultTask = reader.Observe(idRange.End - 1, cts.Token).SuppressCancellation().ToListAsync();
            _ = BackgroundTask.Run(() => CreateChatEntries(
                    tester.AppServices.GetRequiredService<IChats>(), session, TestChatId,
                    (int)Constants.Chat.EntryIdTiles.TileSize));
            var result = await resultTask;
            result.Count.Should().Be(1 + (int)Constants.Chat.EntryIdTiles.TileSize);
        }
    }

    [FlakyFact("Flaky")]
    public async Task ObserveTest2()
    {
        var appHost = AppHost;
        await using var tester = appHost.NewWebClientTester(Out);
        var services = tester.AppServices;
        var account = await tester.SignInAsBob();
        var session = tester.Session;

        var accounts = services.GetRequiredService<IAccounts>();
        var ownAccount = await accounts.GetOwn(session, CancellationToken.None);
        ownAccount.Id.Should().Be(account.Id);
        ownAccount.Name.Should().Be(account.Name);

        var chats = services.GetRequiredService<IChats>();
        var chat = await chats.Get(session, TestChatId, CancellationToken.None);
        chat.Should().NotBeNull();
        chat?.Title.Should().Be("The Actual One");

        // Ensure Bob is joined before capturing idRange to avoid
        // a "member joined" system entry being created during CreateChatEntries.
        // EnsureJoined fires AuthorUpsertedEvent which creates a system entry asynchronously,
        // so we need to wait for the idRange to stabilize.
        await services.GetRequiredService<IAuthors>().EnsureJoined(session, TestChatId, CancellationToken.None);
        await WaitForIdRangeToStabilize(chats, session, TestChatId);

        var idRangeTask = chats.GetIdRange(session, TestChatId, CancellationToken.None);
        var reader = chats.NewEntryReader(session, TestChatId);

        { // Test 1
            using var cts = new CancellationTokenSource(2000);
            var idRange = await idRangeTask;
            var resultTask = reader.Observe(idRange.End - 1, cts.Token).SuppressCancellation().ToListAsync();
            _ = BackgroundTask.Run(() => CreateChatEntries(
                    chats, session, TestChatId,
                    (int)Constants.Chat.EntryIdTiles.TileSize));
            var result = await resultTask;
            result.Count.Should().Be(1 + (int)Constants.Chat.EntryIdTiles.TileSize);
        }
    }

    private static async Task WaitForIdRangeToStabilize(
        IChats chats,
        Session session,
        ChatId chatId,
        int maxAttempts = 20,
        int delayMs = 100)
    {
        var idRange = await chats.GetIdRange(session, chatId, CancellationToken.None);
        for (var i = 0; i < maxAttempts; i++) {
            await Task.Delay(delayMs);
            var newIdRange = await chats.GetIdRange(session, chatId, CancellationToken.None);
            if (newIdRange == idRange)
                return;
            idRange = newIdRange;
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

                var cmd = new Chats_UpsertEntry { Session = session, ChatId = chatId, LocalId = null, Text = text };
                await commander.Call(cmd, CancellationToken.None).ConfigureAwait(false);
            }
    }
}
