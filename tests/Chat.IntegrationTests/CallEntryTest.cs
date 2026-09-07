using ActualChat.Live;
using ActualChat.Streaming;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public sealed class CallEntryTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task CanceledRingShouldWriteOneCanceledEntry()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var (chatId, bob, alice) = await NewPeerChat(tester);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();

        // act
        await backend.StartCall(chatId, bob.Id, new[] { alice.Id }.ToApiArray(), false, default);
        await backend.CancelCall(chatId, bob.Id, default);

        // assert
        var caller = await tester.GetAuthor(bob.Id);
        var entries = await ReadCallEntries(tester, chatId);
        entries.Should().ContainSingle();
        entries[0].Outcome.Should().Be(CallOutcome.Canceled);
        entries[0].CallerId.Should().Be(bob.Id);
        entries[0].CallerName.Should().Be(caller!.Avatar.Name);
        entries[0].InviteeIds.Should().Equal(alice.Id);
        entries[0].HasVideo.Should().BeFalse();
    }

    [Fact]
    public async Task DeclinedThenCanceledCallShouldStayDeclined()
    {
        // The outcome is first-writer-wins: the decline is the call's story, and the caller's
        // hang-up right after it must not rewrite that into Canceled.

        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var (chatId, bob, alice) = await NewPeerChat(tester);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();

        // act
        await backend.StartCall(chatId, bob.Id, new[] { alice.Id }.ToApiArray(), false, default);
        await backend.DeclineCall(chatId, alice.Id, default);
        await backend.CancelCall(chatId, bob.Id, default);

        // assert
        var entries = await ReadCallEntries(tester, chatId);
        entries.Should().ContainSingle();
        entries[0].Outcome.Should().Be(CallOutcome.Declined);
    }

    [Fact]
    public async Task AnsweredCallShouldWriteEndedAndMaterializeACallConversation()
    {
        // The case the whole Ended outcome exists for: transcription is off, so nothing else would
        // remain in the chat once the session closes. The live state is read while the call is still
        // connected because it names the conversation and the close drops it.

        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var (chatId, bob, alice) = await NewPeerChat(tester);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        var conversations = tester.AppServices.GetRequiredService<IConversationsBackend>();

        // act
        await backend.StartCall(chatId, bob.Id, new[] { alice.Id }.ToApiArray(), true, default);
        await backend.AcceptCall(chatId, alice.Id, default);
        var connected = await backend.GetState(chatId, default);
        connected.Should().NotBeNull();
        await backend.LeaveCall(chatId, alice.Id, default);

        // assert
        var entries = await ReadCallEntries(tester, chatId);
        entries.Should().ContainSingle();
        entries[0].Outcome.Should().Be(CallOutcome.Ended);
        entries[0].CallerId.Should().Be(bob.Id);
        entries[0].HasVideo.Should().BeTrue();

        var conversation = await conversations.Get(connected!.ToMaterializedConversation().Id, default);
        conversation.Should().NotBeNull();
        conversation!.IsCall.Should().BeTrue();
    }

    [Fact]
    public async Task CallerHangingUpAnAnsweredCallShouldBeEndedNotCanceled()
    {
        // CancelCall is also the caller's hang-up, so a connected call reaches the close with
        // Canceled recorded on it. The split is decided by SessionStartedAt, not by that outcome.

        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var (chatId, bob, alice) = await NewPeerChat(tester);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();

        // act
        await backend.StartCall(chatId, bob.Id, new[] { alice.Id }.ToApiArray(), false, default);
        await backend.AcceptCall(chatId, alice.Id, default);
        await backend.CancelCall(chatId, bob.Id, default);
        (await backend.GetState(chatId, default))!.Outcome.Should().Be(CallOutcome.Canceled);
        await backend.LeaveCall(chatId, alice.Id, default);

        // assert
        var entries = await ReadCallEntries(tester, chatId);
        entries.Should().ContainSingle();
        entries[0].Outcome.Should().Be(CallOutcome.Ended);
    }

    [Fact]
    public async Task ClaimedCloseShouldTearDownEvenWhenItsTokenIsCanceled()
    {
        // FinalizeSession is the one close path carrying a revocable token - it comes from
        // LiveConversationSummaryFlow, whose step token dies on a timeout or a shutdown. By then the
        // claim that elects a single closer has already dropped the session key, so nothing retries:
        // the teardown has to survive the very cancellation that interrupted it.

        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var (chatId, bob, alice) = await NewPeerChat(tester);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.StartCall(chatId, bob.Id, new[] { alice.Id }.ToApiArray(), false, default);
        await backend.AcceptCall(chatId, alice.Id, default);
        // Nobody is recording any more, so FinalizeSession gets past its own liveness guard.
        await backend.SetParticipation(chatId, bob.Id, ParticipationKind.AudioListen, true, default);
        await backend.SetParticipation(chatId, alice.Id, ParticipationKind.AudioListen, true, default);

        // act
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await backend.FinalizeSession(chatId, cts.Token).SilentAwait(false);

        // assert - the next call in this chat starts with its caller alone, not with the ghosts of
        // the one that was torn down
        await backend.StartCall(chatId, bob.Id, ApiArray<AuthorId>.Empty, false, default);
        await ComputedTest.When(async ct => {
            var participants = await backend.ListParticipants(chatId, ct);
            participants.Should().Equal(bob.Id);
        }, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GroupCallShouldWriteNoEntry()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.GetOwnAuthor(chatId);
        author.Should().NotBeNull();
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();

        // act
        await backend.StartCall(chatId, author!.Id, ApiArray<AuthorId>.Empty, false, default);
        await backend.CancelCall(chatId, author.Id, default);

        // assert
        (await ReadCallEntries(tester, chatId)).Should().BeEmpty();
    }

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

    private static async Task<IReadOnlyList<CallEntry>> ReadCallEntries(IWebTester tester, ChatId chatId)
    {
        var chats = tester.AppServices.GetRequiredService<IChats>();
        var entries = await chats.ReadReverse(tester.Session, chatId, default)
            .Take(50)
            .ToListAsync();
        return entries.OfType<CallEntry>().ToList();
    }
}
