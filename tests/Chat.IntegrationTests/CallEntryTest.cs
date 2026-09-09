using ActualChat.Live;
using ActualChat.Streaming;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public sealed class CallEntryTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task OldPeerNewsShouldFallBackToThePreviousReadableEntry()
    {
        // LastTextEntry is the chat list's sort key as well as its preview line, so a call must
        // leave an old client's list untouched rather than blank and reordered.

        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var (chatId, bob, alice) = await NewPeerChat(tester);
        var text = await tester.CreateTextEntry(chatId, "before the call");
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        var chatsBackend = tester.AppServices.GetRequiredService<IChatsBackend>();

        // act
        await backend.StartCall(chatId, bob.Id, new[] { alice.Id }.ToApiArray(), false, default);
        await backend.CancelCall(chatId, bob.Id, default);

        // assert
        var news = await chatsBackend.GetNews(chatId, default);
        news!.LastTextEntry.Should().BeOfType<CallEntry>("a current peer sees the call");

        var legacy = await chatsBackend.GetLegacyNews(chatId, default);
        legacy!.LastTextEntry!.Id.Should().Be(text.Id, "an old peer keeps the message it could read");
        legacy.TextEntryLidRange.Should().Be(news.TextEntryLidRange, "unread counting is unaffected");
    }

    [Fact]
    public async Task OldPeerNewsShouldWalkBackAcrossMultipleTilesToFindAReadableEntry()
    {
        // The walk steps tile by tile, so a readable entry that isn't in the tile right before the
        // call only surfaces if the walk actually crosses more than one tile.

        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var (chatId, bob, alice) = await NewPeerChat(tester);
        var tileSize = (int)Constants.Chat.EntryIdTiles.TileSize;
        var text = await CreateTileAlignedTextEntry(tester, chatId, tileSize, "before the call");
        var chatsBackend = tester.AppServices.GetRequiredService<IChatsBackend>();
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();

        // act - text is the first entry of its tile; two tiles' worth of calls after it guarantee
        // at least one whole tile in between with nothing readable in it at all
        for (var i = 0; i < 2 * tileSize; i++) {
            await backend.StartCall(chatId, bob.Id, new[] { alice.Id }.ToApiArray(), false, default);
            await backend.CancelCall(chatId, bob.Id, default);
        }

        // assert
        var legacy = await chatsBackend.GetLegacyNews(chatId, default);
        legacy!.LastTextEntry!.Id.Should().Be(text.Id,
            "the walk must keep stepping back past the empty tile to the one before it");
    }

    [Fact]
    public async Task OldPeerNewsShouldReturnNullWhenNothingReadableIsWithinTheWalkBound()
    {
        // The walk is bounded so an old client's chat-list render can't turn into a full-history
        // scan; a readable entry that sits beyond that bound must read as if it didn't exist.

        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var (chatId, bob, alice) = await NewPeerChat(tester);
        var tileSize = (int)Constants.Chat.EntryIdTiles.TileSize;
        await CreateTileAlignedTextEntry(tester, chatId, tileSize, "before the call");
        var chatsBackend = tester.AppServices.GetRequiredService<IChatsBackend>();
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();

        // act - four tiles' worth of calls push the readable entry just past the walk's bound
        for (var i = 0; i < 4 * tileSize; i++) {
            await backend.StartCall(chatId, bob.Id, new[] { alice.Id }.ToApiArray(), false, default);
            await backend.CancelCall(chatId, bob.Id, default);
        }

        // assert
        var legacy = await chatsBackend.GetLegacyNews(chatId, default);
        legacy!.LastTextEntry.Should().BeNull(
            "the walk must give up at its bound rather than scanning the whole chat");
    }

    // Private methods

    private static async Task<(ChatId ChatId, AuthorFull Bob, AuthorFull Alice)> NewPeerChat(IWebTester tester)
    {
        var bob = await tester.SignInAsUniqueBob();
        var alice = await tester.SignInAsUniqueAlice();
        // Alice adding Bob lifts the non-contact message cap on Bob's side, which some tests
        // exercise by posting several filler entries.
        await tester.CreatePeerContact(alice, bob);
        await tester.SignIn(bob);
        var chatId = (ChatId)PeerChatId.New(bob.Id, alice.Id);
        var authors = tester.AppServices.GetRequiredService<IAuthorsBackend>();
        return (chatId,
            await authors.EnsureJoined(chatId, bob.Id, default),
            await authors.EnsureJoined(chatId, alice.Id, default));
    }

    private static async Task<ChatEntry> CreateTileAlignedTextEntry(
        IWebTester tester, ChatId chatId, int tileSize, string text)
    {
        ChatEntry filler;
        do {
            filler = await tester.CreateTextEntry(chatId, "filler");
        } while ((filler.LocalId + 1) % tileSize != 0);
        return await tester.CreateTextEntry(chatId, text);
    }

}
