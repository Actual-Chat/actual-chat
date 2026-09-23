using ActualChat.Chat.Flows;
using ActualChat.Chat.Module;
using ActualChat.Live;
using ActualChat.Queues;
using ActualChat.Streaming;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public sealed class CallEntryTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(15);

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
    public async Task AFailedCallShouldLeaveNoLiveActivityBehind()
    {
        // The caller is registered as a recorder the moment they dial, so that the ring keeps the
        // session alive. Once the call is over that registration must be gone from every signal the
        // chat list and the call button read - otherwise a call nobody answered reads as "talking"
        // and the button stays hidden. The banner is not part of this: it lives on its own TTL.

        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var (chatId, bob, alice) = await NewPeerChat(tester);
        var backend = (LiveSessionsBackend)tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        var front = tester.AppServices.GetRequiredService<ILiveSessions>();
        var session = tester.Session;

        // act
        await backend.StartCall(chatId, bob.Id, new[] { alice.Id }.ToApiArray(), false, default);
        await backend.DeclineCall(chatId, alice.Id, default);

        // assert
        (await backend.GetState(chatId, default)).Should().BeNull();
        (await front.HasRecorder(session, chatId, default)).Should().BeFalse();
        (await front.GetAudioStreamingAuthorIds(session, chatId, default)).Should().BeEmpty();
        // Declined, not NoAnswer: the retired caller-facing enum had no "declined" and collapsed it
        // into NoAnswer - CallState keeps the two apart, and Alice did answer, with a no.
        (await backend.GetCallState(chatId, default))!.Status.Should().Be(CallStatus.Declined);

        // act - the caller's own hang-up has to leave the same clean slate
        await backend.StartCall(chatId, bob.Id, new[] { alice.Id }.ToApiArray(), false, default);
        await backend.CancelCall(chatId, bob.Id, default);

        // assert
        (await backend.GetState(chatId, default)).Should().BeNull();
        (await front.HasRecorder(session, chatId, default)).Should().BeFalse();
        (await front.GetAudioStreamingAuthorIds(session, chatId, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task ConcurrentClosersShouldWriteOneEntryPerCall()
    {
        // Two closers can decide the same call is over at the same instant - the caller's hang-up and
        // the session finalizer behind the summary flow - and both are expected to be safe. Observed
        // failure: one call left two identical entries 18ms apart, written from different threads.

        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var (chatId, bob, alice) = await NewPeerChat(tester);
        var backend = (LiveSessionsBackend)tester.AppServices.GetRequiredService<ILiveSessionsBackend>();

        // act - the offset between the two closers is swept rather than left to chance: the window
        // is only as wide as a couple of Redis round trips, and starting both at once never lands in it.
        var callCount = 0;
        for (var offsetMs = 0; offsetMs <= 20; offsetMs++) {
            callCount++;
            await backend.StartCall(chatId, bob.Id, new[] { alice.Id }.ToApiArray(), false, default);
            var hangUp = Task.Run(async () =>
                await backend.CancelCall(chatId, bob.Id, default).ConfigureAwait(false));
            await Task.Delay(offsetMs);
            var finalize = Task.Run(async () =>
                await backend.FinalizeSession(chatId, default).ConfigureAwait(false));
            await Task.WhenAll(hangUp, finalize);
        }

        // assert
        var entries = await ReadCallEntries(tester, chatId);
        entries.Should().HaveCount(callCount);
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
        await backend.SetParticipation(chatId, alice.Id, ParticipationKind.Record, false, default);

        // assert
        var entries = await ReadCallEntries(tester, chatId);
        entries.Should().ContainSingle();
        entries[0].Outcome.Should().Be(CallOutcome.Ended);
        entries[0].CallerId.Should().Be(bob.Id);
        entries[0].HasVideo.Should().BeTrue();

        var conversation = await conversations.Get(connected!.ToMaterializedConversation().Id, default);
        conversation.Should().NotBeNull();
        conversation!.IsCall.Should().BeTrue();
        // Nothing was said, so there is nothing to expand into: the card is the whole of it.
        conversation.MessageCount.Should().Be(0);
        conversation.IsExpandedByDefault.Should().BeFalse();
    }

    [Fact]
    public async Task ACallConversationShouldExpandByDefaultOnlyWhileItIsShort()
    {
        // A call is never summarized, so the tier the summary flow would have picked has to be
        // computed at materialization instead - from the same thresholds, so the two can't drift.

        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var (chatId, bob, alice) = await NewPeerChat(tester);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        var conversations = tester.AppServices.GetRequiredService<IConversationsBackend>();
        var settings = tester.AppServices.GetRequiredService<ChatSettings>().Summarization;
        var longLine = string.Join(' ', Enumerable.Repeat("word", 1 + settings.MinConversationWords / 10));

        // act - a short call
        await backend.StartCall(chatId, bob.Id, new[] { alice.Id }.ToApiArray(), false, default);
        await backend.AcceptCall(chatId, alice.Id, default);
        var shortCall = await backend.GetState(chatId, default);
        await tester.CreateTextEntry(chatId, "hi");
        await tester.CreateTextEntry(chatId, "hi back");
        await backend.SetParticipation(chatId, alice.Id, ParticipationKind.Record, false, default);

        // assert
        var shortConversation = await conversations.Get(shortCall!.ToMaterializedConversation().Id, default);
        shortConversation!.MessageCount.Should().Be(2);
        shortConversation.IsExpandedByDefault.Should().BeTrue();

        // act - a long one: both thresholds have to be crossed, the rule ORs them
        await backend.StartCall(chatId, bob.Id, new[] { alice.Id }.ToApiArray(), false, default);
        await backend.AcceptCall(chatId, alice.Id, default);
        var longCall = await backend.GetState(chatId, default);
        for (var i = 0; i < settings.MinConversationEntries; i++)
            await tester.CreateTextEntry(chatId, longLine);
        await backend.SetParticipation(chatId, alice.Id, ParticipationKind.Record, false, default);

        // assert
        var longConversation = await conversations.Get(longCall!.ToMaterializedConversation().Id, default);
        longConversation!.MessageCount.Should().Be(settings.MinConversationEntries);
        longConversation.IsExpandedByDefault.Should().BeFalse();
    }

    [Fact]
    public async Task AnsweredCallInterruptedByAMessageShouldMaterializeARangeAroundItsEntry()
    {
        // A message written between the ring and the answer pushes VisibleStartLid (set at the latch)
        // past EndEntryLid, which only a summary ever advances and a transcription-off call never gets.
        // The resulting range runs backwards, and a degenerate one drops both the card and the Ended
        // entry it anchors - the call disappears from the chat entirely.

        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var (chatId, bob, alice) = await NewPeerChat(tester);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        var conversations = tester.AppServices.GetRequiredService<IConversationsBackend>();

        // act
        await backend.StartCall(chatId, bob.Id, new[] { alice.Id }.ToApiArray(), false, default);
        await tester.CreateTextEntry(chatId, "can't talk right now");
        await backend.AcceptCall(chatId, alice.Id, default);
        var connected = await backend.GetState(chatId, default);
        connected.Should().NotBeNull();
        await backend.SetParticipation(chatId, alice.Id, ParticipationKind.Record, false, default);

        // assert
        var entries = await ReadCallEntries(tester, chatId);
        entries.Should().ContainSingle();
        entries[0].Outcome.Should().Be(CallOutcome.Ended);

        var conversation = await conversations.Get(connected!.ToMaterializedConversation().Id, default);
        conversation.Should().NotBeNull();
        conversation!.EntryLidRange.Contains(entries[0].LocalId).Should()
            .BeTrue("the card must cover the entry that is the call's only row");
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
        await backend.SetParticipation(chatId, alice.Id, ParticipationKind.Record, true, default);
        await backend.CancelCall(chatId, bob.Id, default);
        (await backend.GetState(chatId, default))!.Outcome.Should().Be(CallOutcome.Canceled);
        await backend.SetParticipation(chatId, alice.Id, ParticipationKind.Record, false, default);

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
        var aliceAccount = await tester.SignInAsUniqueAlice();
        var carolAccount = await tester.SignInAsNew("Carol");
        await tester.SignInAsUniqueBob();
        var (chatId, _) = await tester.CreateChat(false);
        var authors = tester.AppServices.GetRequiredService<IAuthorsBackend>();
        var bob = (await tester.GetOwnAuthor(chatId))!;
        var alice = await authors.EnsureJoined(chatId, aliceAccount.Id, default);
        var carol = await authors.EnsureJoined(chatId, carolAccount.Id, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.StartCall(chatId, bob.Id, new[] { alice.Id }.ToApiArray(), false, default);
        await backend.AcceptCall(chatId, alice.Id, default);
        // Nobody records, and Bob is the call's only party present - Carol is in the chat, not in the
        // call - so FinalizeSession gets past its own liveness guard. Carol is the ghost to clear.
        await backend.SetParticipation(chatId, bob.Id, ParticipationKind.AudioListen, true, default);
        await backend.SetParticipation(chatId, carol.Id, ParticipationKind.AudioListen, true, default);

        // act
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await backend.FinalizeSession(chatId, cts.Token).SilentAwait(false);

        // assert - the next call in this chat starts with its caller alone, not with the ghosts of
        // the one that was torn down
        await backend.StartCall(chatId, bob.Id, ApiArray<AuthorId>.Empty, false, default);
        await TestWait.When(async ct => {
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

    [Fact]
    public async Task ALateTranscriptShouldJoinTheCallConversation()
    {
        // A transcript's entry is created on its first non-empty result, so the last utterance of a
        // call routinely lands after the close already fixed the conversation's range.

        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var (chatId, bob, alice) = await NewPeerChat(tester);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        var conversations = tester.AppServices.GetRequiredService<IConversationsBackend>();

        await backend.StartCall(chatId, bob.Id, new[] { alice.Id }.ToApiArray(), false, default);
        await backend.AcceptCall(chatId, alice.Id, default);
        var connected = await backend.GetState(chatId, default);
        await tester.CreateTextEntry(chatId, "hi");
        await backend.SetParticipation(chatId, alice.Id, ParticipationKind.Record, false, default);

        var conversationId = connected!.ToMaterializedConversation().Id;
        var materialized = await conversations.Get(conversationId, default);
        materialized.Should().NotBeNull();
        var callEntry = (await ReadCallEntries(tester, chatId)).Single();

        // act - the transcript of a phrase spoken during the call arrives once the call is over
        var late = await tester.CreateStreamingEntry(chatId, Languages.English, beginsAt: materialized!.StartsAt);
        late = await tester.FinalizeStreamingEntry(late, "one last thing");
        await RunCallTailFlow(tester, conversationId);

        // assert
        await TestWait.When(async ct => {
            var grown = await conversations.Get(conversationId, ct);
            grown.Should().NotBeNull();
            grown!.EntryLidRange.Contains(late.ChatEntrySlim.LocalId).Should()
                .BeTrue("a phrase spoken during the call belongs to its conversation");
            grown.EntryLidRange.Contains(callEntry.LocalId).Should()
                .BeTrue("growing the range must not drop the entry the card stands in for");
            grown.MessageCount.Should().Be(2);
        }, WaitTimeout);
    }

    [Fact]
    public async Task AMessageWrittenAfterTheCallShouldStayOutsideIt()
    {
        // The test of belonging is when the speech started, and a message written after the hang-up
        // begins after the call ended - otherwise the card would keep swallowing the chat's tail.

        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var (chatId, bob, alice) = await NewPeerChat(tester);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        var conversations = tester.AppServices.GetRequiredService<IConversationsBackend>();

        await backend.StartCall(chatId, bob.Id, new[] { alice.Id }.ToApiArray(), false, default);
        await backend.AcceptCall(chatId, alice.Id, default);
        var connected = await backend.GetState(chatId, default);
        await tester.CreateTextEntry(chatId, "hi");
        await backend.SetParticipation(chatId, alice.Id, ParticipationKind.Record, false, default);

        var conversationId = connected!.ToMaterializedConversation().Id;
        var backdated = await BackdateCallEnd(tester, conversationId);

        // act
        var afterCall = await tester.CreateTextEntry(chatId, "forgot to say");
        await RunCallTailFlow(tester, conversationId);

        // assert
        await TestWait.When(async ct => {
            var refreshed = await conversations.Get(conversationId, ct);
            refreshed.Should().NotBeNull();
            refreshed!.EndEntryLid.Should().Be(backdated.EndEntryLid);
            refreshed.EntryLidRange.Contains(afterCall.LocalId).Should().BeFalse();
        }, WaitTimeout);
    }

    [Fact]
    public async Task ACallSizedOverStreamingTranscriptsShouldBeResizedOnceTheySettle()
    {
        // Finalization waits out the offline refine pass, so at the close the entries' content is
        // often still empty - and a call sized from empty text reads as an unexpandable short one.

        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var (chatId, bob, alice) = await NewPeerChat(tester);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        var conversations = tester.AppServices.GetRequiredService<IConversationsBackend>();
        var settings = tester.AppServices.GetRequiredService<ChatSettings>().Summarization;
        var longLine = string.Join(' ', Enumerable.Repeat("word", 1 + settings.MinConversationWords / 10));

        await backend.StartCall(chatId, bob.Id, new[] { alice.Id }.ToApiArray(), false, default);
        await backend.AcceptCall(chatId, alice.Id, default);
        var connected = await backend.GetState(chatId, default);
        var streaming = new List<StreamingEntry>();
        for (var i = 0; i < settings.MinConversationEntries; i++)
            streaming.Add(await tester.CreateStreamingEntry(chatId, Languages.English));
        await backend.SetParticipation(chatId, alice.Id, ParticipationKind.Record, false, default);

        var conversationId = connected!.ToMaterializedConversation().Id;
        var materialized = await BackdateCallEnd(tester, conversationId);
        materialized.IsExpandedByDefault.Should()
            .BeTrue("with no text yet the call measures as a short one");

        // act
        foreach (var entry in streaming)
            await tester.FinalizeStreamingEntry(entry, longLine);
        await RunCallTailFlow(tester, conversationId);

        // assert
        await TestWait.When(async ct => {
            var resized = await conversations.Get(conversationId, ct);
            resized.Should().NotBeNull();
            resized!.MessageCount.Should().Be(settings.MinConversationEntries);
            resized.IsExpandedByDefault.Should()
                .BeFalse("once the transcripts are in, the call is a long one");
        }, WaitTimeout);
    }

    [Fact]
    public async Task ResummarizingACallShouldLeaveTheCardsOwnShapeAlone()
    {
        // The refresh that fills in the tail a call's live summary never reaches runs the ordinary
        // summarizer over the conversation's range - and that range ends on the CallEntry. Left to
        // itself the summarizer would pull the end back off that entry (which then draws itself a
        // second time), count it as a message, list Wall-E among the speakers, and replace the call's
        // duration with the transcript's.

        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var (chatId, bob, alice) = await NewPeerChat(tester);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        var conversations = tester.AppServices.GetRequiredService<IConversationsBackend>();

        await backend.StartCall(chatId, bob.Id, new[] { alice.Id }.ToApiArray(), false, default);
        await backend.AcceptCall(chatId, alice.Id, default);
        var connected = await backend.GetState(chatId, default);
        await tester.CreateTextEntry(chatId, "hi");
        await tester.CreateTextEntry(chatId, "hi back");
        await backend.SetParticipation(chatId, alice.Id, ParticipationKind.Record, false, default);

        var materialized = await conversations.Get(connected!.ToMaterializedConversation().Id, default);
        materialized.Should().NotBeNull();
        var callEntry = (await ReadCallEntries(tester, chatId)).Single();

        // act
        var refreshed = await tester.AppServices.Commander()
            .Call(new ConversationBackend_Summarize(chatId, [materialized!.EntryLidRange]));

        // assert
        refreshed.Title.Should().NotBeNullOrWhiteSpace("the refresh exists to give the call a summary");
        refreshed.EntryLidRange.Contains(callEntry.LocalId).Should()
            .BeTrue("the card must keep covering the entry it stands in for");
        refreshed.CallerId.Should().Be(bob.Id);
        refreshed.StartsAt.Should().Be(materialized.StartsAt);
        refreshed.EndsAt.Should().Be(materialized.EndsAt);
        refreshed.MessageCount.Should().Be(2, "the CallEntry closing the range is not one of the messages");
        refreshed.AuthorIds.Should().NotContain(Constants.User.Walle.GetWalleAuthorId(chatId));
    }

    // Private methods

    private async Task RunCallTailFlow(IWebTester tester, ConversationId conversationId)
    {
        // Immediate (no-delay) resume - the close schedules this flow a few seconds out.
        await FlowHub.NewResumeEvent<CallTailFlow>(conversationId.Value)
            .WithDelayQuanta(TimeSpan.Zero)
            .Schedule();
        await tester.AppServices.Queues().WhenProcessing();
    }

    private static async Task<Conversation> BackdateCallEnd(IWebTester tester, ConversationId conversationId)
    {
        // The flow only takes its settling pass once the transcripts have had their time to finalize,
        // which is measured from the call's end - so a test that needs that pass moves the end back.
        var conversations = tester.AppServices.GetRequiredService<IConversationsBackend>();
        var conversation = await conversations.Get(conversationId, default).Require();
        var backdated = conversation with {
            EndsAt = conversation.EndsAt - Constants.Transcription.EntryFinalizationTimeout - TimeSpan.FromMinutes(1),
        };
        return await tester.AppServices.Commander().Call(new ConversationBackend_Materialize(backdated));
    }

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
