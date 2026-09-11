using ActualChat.Live;
using ActualChat.Streaming;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public sealed class LiveSessionsTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task SessionShouldStayLiveWhileRecordingThenClose()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        author.Should().NotBeNull();
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();

        // act — a streamer registers (auto-joins the registry as a recorder)
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, true, default);

        // assert
        var live = await backend.GetState(chatId, default);
        live.Should().NotBeNull();
        live!.TranscriptionOn.Should().BeTrue();
        live.AuthorIds.Should().Contain(author.Id);
        // the recording participant keeps the session live through a speech gap (no stream needed)
        live.IsClosing.Should().BeFalse();

        // act — recording stops (mic off): the last participant leaves
        await backend.SetParticipation(chatId, author!.Id, ParticipationKind.Record, false, default);

        // assert — an explicit leave that empties the call closes it outright (no lingering grace)
        (await backend.GetState(chatId, default)).Should().BeNull();
    }

    [Fact]
    public async Task PhoneModeShouldStayLiveWhileRecordingThenClose()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();

        // act
        await backend.OnStreamRegistered(chatId, author!.Id, null, false, true, default);

        // assert — present, and the recording participant keeps the call live (not closing) across a silence gap
        var live = await backend.GetState(chatId, default);
        live.Should().NotBeNull();
        live!.IsClosing.Should().BeFalse();

        // act — recording stops: the last participant leaves
        await backend.SetParticipation(chatId, author!.Id, ParticipationKind.Record, false, default);

        // assert — phone-mode has nothing to persist, so the empty call closes outright
        (await backend.GetState(chatId, default)).Should().BeNull();
    }

    [Fact]
    public async Task ParticipationShouldBeTracked()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        var authorId = author!.Id;

        // act — a streamer registers
        await backend.OnStreamRegistered(chatId, authorId, null, false, true, default);

        // assert — it is auto-registered as a participant (recorders join the registry)
        await ComputedTest.When(async ct =>
            (await backend.ListParticipants(chatId, ct)).Contains(authorId).Should().BeTrue());

        // act — an explicit leave
        await backend.SetParticipation(chatId, authorId, ParticipationKind.Record, false, default);

        // assert — it removes them
        await ComputedTest.When(async ct =>
            (await backend.ListParticipants(chatId, ct)).Contains(authorId).Should().BeFalse());

        // act — a re-join as a listener
        await backend.SetParticipation(chatId, authorId, ParticipationKind.AudioListen, true, default);

        // assert — they are back
        await ComputedTest.When(async ct =>
            (await backend.ListParticipants(chatId, ct)).Contains(authorId).Should().BeTrue());
    }

    [Fact]
    public async Task ExplicitLeaveShouldCloseImmediately()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, true, default);

        // act — the only participant explicitly leaves
        await backend.SetParticipation(chatId, author!.Id, ParticipationKind.Record, false, default);

        // assert — the session is gone at once and the registry is cleared (the grace is only for stale clients)
        (await backend.GetState(chatId, default)).Should().BeNull();
        // Unlike GetState, ListParticipants is consolidated: an already-observed value keeps serving
        // the pre-leave registry until the consolidation delay elapses.
        await ComputedTest.When(async ct => (await backend.ListParticipants(chatId, ct)).Should().BeEmpty());
    }

    [Fact]
    public async Task RejoinAfterCloseShouldStartFreshSession()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, true, default);
        await backend.SetParticipation(chatId, author!.Id, ParticipationKind.Record, false, default);
        (await backend.GetState(chatId, default)).Should().BeNull();

        // act — recording resumes after the close
        await backend.OnStreamRegistered(chatId, author.Id, null, true, true, default);

        // assert — a brand-new live session is started
        var live = await backend.GetState(chatId, default);
        live.Should().NotBeNull();
        live!.IsClosing.Should().BeFalse();
    }

    [Fact]
    public async Task LeaveWithOthersPresentShouldKeepSessionLive()
    {
        // arrange — two real accounts both join as participants
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await bob.CreateChat(true);
        await alice.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var backend = bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.OnStreamRegistered(chatId, bobAuthor!.Id, null, true, true, default);
        await backend.OnStreamRegistered(chatId, aliceAuthor!.Id, null, true, true, default);

        // act — one of two participants leaves
        await backend.SetParticipation(chatId, aliceAuthor!.Id, ParticipationKind.Record, false, default);

        // assert — the other keeps the call alive (not closing, not closed)
        var live = await backend.GetState(chatId, default);
        live.Should().NotBeNull();
        live!.IsClosing.Should().BeFalse();

        // act — the last participant leaves too
        await backend.SetParticipation(chatId, bobAuthor!.Id, ParticipationKind.Record, false, default);

        // assert — a latched transcription session doesn't vanish on empty: it's marked closing and
        // LiveConversationSummaryFlow owns the final pass, then calls FinalizeSession
        live = await backend.GetState(chatId, default);
        live.Should().NotBeNull();
        live!.IsClosing.Should().BeTrue();
    }

    [Fact]
    public async Task RecorderLeavingWithOnlyListenerLeftShouldCloseSession()
    {
        // The session stays live only while someone is streaming (recording audio or video); a lone
        // listener does not keep it alive. Once the last recorder leaves, the session closes even
        // with a listener still present.

        // arrange — two streamers latch the session; then one switches to just listening
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await bob.CreateChat(true);
        await alice.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var backend = bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.OnStreamRegistered(chatId, bobAuthor!.Id, null, true, true, default);
        await backend.OnStreamRegistered(chatId, aliceAuthor!.Id, null, true, true, default);
        await backend.SetParticipation(chatId, aliceAuthor!.Id, ParticipationKind.AudioListen, true, default);

        var live = await backend.GetState(chatId, default);
        live!.IsClosing.Should().BeFalse("bob is still recording");

        // act — the last recorder (bob) leaves; only the listener (alice) remains
        await backend.SetParticipation(chatId, bobAuthor!.Id, ParticipationKind.Record, false, default);

        // assert — the session closes despite alice still listening
        live = await backend.GetState(chatId, default);
        live.Should().NotBeNull();
        live!.IsClosing.Should().BeTrue("nobody is streaming anymore, so a lone listener can't keep it live");
    }

    [Fact]
    public async Task RecorderDowngradingToListenerShouldMarkSessionClosing()
    {
        // Stopping recording while staying on as a listener empties the session of streamers - a lone
        // listener can't keep it live - so it's marked closing (recoverable if a recorder returns), just
        // like an explicit leave that stops the last stream.

        // arrange — a solo streamer records
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, true, default);
        (await backend.GetState(chatId, default))!.IsClosing.Should().BeFalse();

        // act — recording stops but the same author stays on as a listener (mic off, still listening)
        await backend.SetParticipation(chatId, author!.Id, ParticipationKind.AudioListen, true, default);

        // assert — no recorder is left, so the session is marked closing
        var live = await backend.GetState(chatId, default);
        live.Should().NotBeNull();
        live!.IsClosing.Should().BeTrue("a lone listener can't keep the session live");
    }

    [Fact]
    public async Task LastRecorderDowngradingToListenerShouldCloseSession()
    {
        // The user-reported case: both peers stop recording but keep listening. Neither leaves, yet with no
        // recorder left the latched session must close (marked closing, then finalized by the summary flow).

        // arrange — two streamers latch the session
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await bob.CreateChat(true);
        await alice.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var backend = bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.OnStreamRegistered(chatId, bobAuthor!.Id, null, true, true, default);
        await backend.OnStreamRegistered(chatId, aliceAuthor!.Id, null, true, true, default);

        // act — alice stops recording but keeps listening; bob is still recording, so it stays live
        await backend.SetParticipation(chatId, aliceAuthor!.Id, ParticipationKind.AudioListen, true, default);
        var live = await backend.GetState(chatId, default);
        live!.IsClosing.Should().BeFalse("bob is still recording");

        // act — bob stops recording but also stays on as a listener: no recorder remains
        await backend.SetParticipation(chatId, bobAuthor!.Id, ParticipationKind.AudioListen, true, default);

        // assert — two listeners can't keep the session live, so it's closing
        live = await backend.GetState(chatId, default);
        live.Should().NotBeNull();
        live!.IsClosing.Should().BeTrue("nobody is streaming anymore, so listeners alone can't keep it live");
    }

    [Fact]
    public async Task LiveSessionShouldExposeHostAndMembers()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();

        // act — need 2 peers for Get to return non-null
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, true, default);
        await backend.OnStreamRegistered(chatId, AuthorId.New(chatId, 777_011), null, true, true, default);

        // assert — the session projects the host + the auto-registered streamer as a member
        var liveSession = await backend.Get(chatId, default);
        liveSession.Should().NotBeNull();
        liveSession!.Host.Should().Be(author.Id);
        liveSession.Conversation.Should().NotBeNull();
        var hostMember = liveSession.Members.SingleOrDefault(m => m.AuthorId == author.Id);
        hostMember.Should().NotBeNull();
        hostMember!.Group.Should().Be(MemberGroup.Host);
    }

    [Fact]
    public async Task SetRulesShouldPersistVoiceModeOverride()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, true, default);
        await backend.OnStreamRegistered(chatId, AuthorId.New(chatId, 777_012), null, true, true, default);

        // act — a controller forces transcript-only (no live voice)
        await backend.SetRules(chatId, new SessionRules { VoiceModeOverride = Users.VoiceMode.JustText }, default);

        // assert
        var liveSession = await backend.Get(chatId, default);
        liveSession!.Rules.VoiceModeOverride.Should().Be(Users.VoiceMode.JustText);
    }

    [Fact]
    public async Task MutePeerShouldSetMicMuted()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, true, default);
        await backend.OnStreamRegistered(chatId, AuthorId.New(chatId, 777_013), null, true, true, default);

        // act
        await backend.MutePeer(chatId, author.Id, true, default);

        // assert
        var liveSession = await backend.Get(chatId, default);
        var member = liveSession!.Members.Single(m => m.AuthorId == author.Id);
        member.MicMuted.Should().BeTrue();
    }

    [Fact]
    public async Task MutePeerShouldAllowSelfButRequireManageForPeers()
    {
        // arrange — Bob owns the chat (host/owner), Alice is a regular member
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var liveSessions = alice.AppServices.GetRequiredService<ILiveSessions>();

        // act
        Func<Task> selfMute = () => liveSessions.MutePeer(alice.Session, chatId, aliceAuthor!.Id, true, default);
        Func<Task> mutePeer = () => liveSessions.MutePeer(alice.Session, chatId, bobAuthor!.Id, true, default);

        // assert — a non-host participant may (un)mute themselves, but not another peer
        await selfMute.Should().NotThrowAsync();
        await mutePeer.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SessionShouldLatchOnSecondStreamer()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();

        // act — first (and only) streamer: not a session yet
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, true, default);

        // assert
        var live = await backend.GetState(chatId, default);
        live.Should().NotBeNull();
        live!.SessionStartedAt.Should().BeNull();

        // act — a second distinct peer starts streaming
        var peer2 = AuthorId.New(chatId, 777_001);
        await backend.OnStreamRegistered(chatId, peer2, null, true, true, default);

        // assert — the session latches
        live = await backend.GetState(chatId, default);
        live!.AuthorIds.Should().HaveCount(2);
        live.SessionStartedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task GetShouldReturnNullUntilSecondStreamer()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();

        // act — single streamer: conversation exists, but no session yet
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, true, default);

        // assert
        (await backend.GetState(chatId, default)).Should().NotBeNull();
        (await backend.Get(chatId, default)).Should().BeNull();

        // act — 2nd peer streams
        var peer2 = AuthorId.New(chatId, 777_002);
        await backend.OnStreamRegistered(chatId, peer2, null, true, true, default);

        // assert — the session is now exposed, started at the latch moment
        var liveSession = await backend.Get(chatId, default);
        liveSession.Should().NotBeNull();
        liveSession!.StartedAt.Should().NotBe(default);
    }

    [Fact]
    public async Task SessionShouldPersistAcrossVadGap()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();

        // act — two peers stream, latching a session
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, true, default);
        await backend.OnStreamRegistered(chatId, AuthorId.New(chatId, 777_020), null, true, true, default);

        // assert — latched, and the recording participants keep it fully live (no closing) through a gap
        var live = await backend.GetState(chatId, default);
        var latchedAt = live!.SessionStartedAt;
        latchedAt.Should().NotBeNull();
        live.IsClosing.Should().BeFalse();
        (await backend.Get(chatId, default)).Should().NotBeNull();

        // act — a fresh utterance after the gap
        await backend.OnStreamRegistered(chatId, author.Id, null, true, true, default);

        // assert — still the same live session, latch unchanged
        live = await backend.GetState(chatId, default);
        live!.IsClosing.Should().BeFalse();
        live.SessionStartedAt.Should().Be(latchedAt);
        (await backend.Get(chatId, default)).Should().NotBeNull();
    }

    [Fact]
    public async Task LiveBlockShouldEnterRangeMetaOnlyAfterLatch()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        var conversations = tester.AppServices.GetRequiredService<IConversationsBackend>();

        // act — a single streamer: the conversation state exists but is not yet a session
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, true, default);
        var live = await backend.GetState(chatId, default);
        live.Should().NotBeNull();
        var tileStart = Constants.Chat.RangeMetaEntryIdTiles.GetTile(live!.StartEntryLid).Range.Start;

        // assert — no live conversation block is injected for a solo streamer
        var metaBefore = await conversations.GetRangeMeta(chatId, tileStart, default);
        metaBefore.ConversationLidRanges.Should().NotContain(r => r.Contains(live.StartEntryLid));

        // act — a second distinct peer latches the session
        await backend.OnStreamRegistered(chatId, AuthorId.New(chatId, 777_030), null, true, true, default);

        // assert — the live block is keyed to the chat end at latch time, not to StartEntryLid (they differ
        // once the chat grows during the solo phase), and lands one recompute later: reads are consolidated.
        var latched = await backend.GetState(chatId, default);
        latched!.SessionStartedAt.Should().NotBeNull();
        var liveStartLid = latched.EffectiveVisibleStartLid;
        var liveTileStart = Constants.Chat.RangeMetaEntryIdTiles.GetTile(liveStartLid).Range.Start;
        await ComputedTest.When(async ct => {
            var metaAfter = await conversations.GetRangeMeta(chatId, liveTileStart, ct);
            metaAfter.ConversationLidRanges.Should().Contain(r => r.Contains(liveStartLid));
        });
    }

    [Fact]
    public async Task LiveBlockShouldEnterTileOnlyAfterLatch()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        var conversations = tester.AppServices.GetRequiredService<IConversationsBackend>();

        // act — a single streamer
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, true, default);
        var live = await backend.GetState(chatId, default);
        var tileRange = Constants.Chat.RangeMetaEntryIdTiles.GetTile(live!.StartEntryLid).Range;

        // assert — the synthetic live block is not injected before the latch
        var tileBefore = await conversations.GetTile(chatId, tileRange, default);
        tileBefore.Should().NotContain(c => c.Id == live.ConversationId);

        // act — a second distinct peer latches the session
        await backend.OnStreamRegistered(chatId, AuthorId.New(chatId, 777_031), null, true, true, default);

        // assert — the live block is now present, re-keyed by the latch to the chat end (VisibleStartLid)
        var latched = await backend.GetState(chatId, default);
        latched.Should().NotBeNull();
        await ComputedTest.When(async ct => {
            var tileAfter = await conversations.GetTile(chatId, tileRange, ct);
            tileAfter.Should().Contain(c => c.Id == latched!.ConversationId);
        });
    }

    [Fact]
    public async Task SilentRecorderShouldStayPresentMember()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();

        // act — two peers latch a session (the recorder has participation but no live stream)
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, true, default);
        await backend.OnStreamRegistered(chatId, AuthorId.New(chatId, 777_040), null, true, true, default);

        // assert — with the recording participation present, the silent recorder is still mic-on and not Exited
        var liveSession = await backend.Get(chatId, default);
        liveSession.Should().NotBeNull();
        var me = liveSession!.Members.Single(m => m.AuthorId == author.Id);
        me.IsMicOpen.Should().BeTrue();
        me.Group.Should().NotBe(MemberGroup.Exited);
    }

    [Fact]
    public async Task ListenerShouldNotKeepSessionAlive()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, true, default);

        // act — the participant stops recording but keeps listening: nobody is streaming now
        await backend.SetParticipation(chatId, author!.Id, ParticipationKind.AudioListen, true, default);

        // assert — a lone listener no longer keeps the session alive; it winds down
        (await backend.GetState(chatId, default))!.IsClosing.Should().BeTrue();

        // act — the listener leaves entirely
        await backend.SetParticipation(chatId, author!.Id, ParticipationKind.AudioListen, false, default);

        // assert — the now-empty call closes outright
        (await backend.GetState(chatId, default)).Should().BeNull();
    }

    [Fact]
    public async Task SetParticipationRemovalShouldNotStompANewerKind()
    {
        // A recorder stream ending must not blow away a concurrently-open listening registration for
        // the same author - _participants stores one record per author, keyed by chatId+authorId, so
        // an unconditional Remove from the ending Record registration would also delete the still-live
        // AudioListen one if nothing guards it.

        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, true, default);

        // act - the author is upgraded to listening (their recording stream is being replaced), then
        // the OLD recording stream's own teardown fires its removal after the fact
        await backend.SetParticipation(chatId, author.Id, ParticipationKind.AudioListen, true, default);
        await backend.SetParticipation(chatId, author.Id, ParticipationKind.Record, false, default);

        // assert - the listening registration survives; only a same-kind removal may clear it
        await ComputedTest.When(async ct =>
            (await backend.ListParticipants(chatId, ct)).Should().Contain(author.Id));
    }

    [Fact]
    public async Task HasRecorderShouldReflectRegistry()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();

        // act — a streamer registers
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, true, default);

        // assert — it counts as a recorder
        await ComputedTest.When(async ct => (await backend.HasRecorder(chatId, ct)).Should().BeTrue());

        // act — recording stops
        await backend.SetParticipation(chatId, author!.Id, ParticipationKind.Record, false, default);

        // assert — no recorder is left
        await ComputedTest.When(async ct => (await backend.HasRecorder(chatId, ct)).Should().BeFalse());
    }

    [Fact]
    public async Task TrailingUtteranceShouldNotResurrectRecorder()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();

        // recording starts (auto-registers as a recorder)
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, true, default);
        await ComputedTest.When(async ct => (await backend.HasRecorder(chatId, ct)).Should().BeTrue());

        // the user stops recording but keeps listening
        await backend.SetParticipation(chatId, author!.Id, ParticipationKind.AudioListen, true, default);
        await ComputedTest.When(async ct => (await backend.HasRecorder(chatId, ct)).Should().BeFalse());

        // act — a trailing utterance arrives after the switch; it must NOT flip the listener back to a recorder
        await backend.OnStreamRegistered(chatId, author.Id, null, true, true, default);

        // assert
        await ComputedTest.When(async ct => (await backend.HasRecorder(chatId, ct)).Should().BeFalse());
    }

    [Fact]
    public async Task StartCallShouldRingInvitee()
    {
        // arrange — Bob (caller) and Alice (callee) share a chat
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var backend = bob.AppServices.GetRequiredService<ILiveSessionsBackend>();

        // act — Bob rings Alice
        await backend.StartCall(
            chatId, bobAuthor!.Id, new[] { aliceAuthor!.Id }.ToApiArray(), false, default);

        // assert — a fresh call is Dialing: the session exists (Call tab works) but no live conversation
        // is surfaced yet, so SessionStartedAt stays null until someone answers.
        var state = await backend.GetState(chatId, default);
        state.Should().NotBeNull();
        state!.Kind.Should().Be(LiveSessionKind.Call);
        state.SessionStartedAt.Should().BeNull();
        // the Call tab still gets a projection while dialing, with the ring visible and no conversation
        var live = await backend.Get(chatId, default);
        live.Should().NotBeNull();
        live!.Conversation.Should().BeNull();
        live.Invites.Should().ContainSingle(i =>
            i.InviteeId == aliceAuthor.Id && i.Status == CallInviteStatus.Ringing);
    }

    [Fact]
    public async Task AcceptCallShouldJoinCall()
    {
        // arrange
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var backend = bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.StartCall(
            chatId, bobAuthor!.Id, new[] { aliceAuthor!.Id }.ToApiArray(), false, default);

        // act — Alice answers and starts listening (this is what actually registers her presence now
        // that GetListeningStream owns AudioListen participation, not AcceptCall itself)
        await backend.AcceptCall(chatId, aliceAuthor.Id, default);
        await backend.SetParticipation(chatId, aliceAuthor.Id, ParticipationKind.AudioListen, true, default);

        // assert — genuine presence promotes the invite straight to Active (SyncCallParticipantActivity,
        // driven by GetState's self-heal) rather than leaving it at merely-accepted. Poll briefly: the
        // self-heal is fire-and-forget, and GetConsolidatedParticipants needs its own ~200ms to settle.
        var status = CallInviteStatus.New;
        for (var attempt = 0; attempt < 30 && status != CallInviteStatus.Active; attempt++) {
            var polled = await backend.Get(chatId, default);
            status = polled!.Invites.Single(i => i.InviteeId == aliceAuthor.Id).Status;
            if (status != CallInviteStatus.Active)
                await Task.Delay(100);
        }
        status.Should().Be(CallInviteStatus.Active);
        (await backend.ListParticipants(chatId, default)).Should().Contain(aliceAuthor.Id);
        // the answer latches the dialing call to Connected: block now surfaced
        var state = await backend.GetState(chatId, default);
        state!.Kind.Should().Be(LiveSessionKind.Call);
        state.SessionStartedAt.Should().NotBeNull();
        state.AuthorIds.Should().Contain(aliceAuthor.Id);
    }

    [Fact]
    public async Task AcceptedCallWithNoConnectionShouldCloseAfterGraceWindow()
    {
        // The invitee accepted but never actually opened a listening or recording stream (stuck mic
        // prompt, dead network, client bug) - nothing else would ever notice, since Kind == Call forever
        // otherwise. EnforceCallConnectGrace is internal so the test can drive it directly instead of
        // waiting out the real 3s delay - see AcceptCall's scheduling call for context.

        // arrange
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var backend = (LiveSessionsBackend)bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.StartCall(chatId, bobAuthor!.Id, new[] { aliceAuthor!.Id }.ToApiArray(), false, default);

        // act - Alice accepts but her client never streams or listens
        await backend.AcceptCall(chatId, aliceAuthor.Id, default);
        (await backend.GetState(chatId, default))!.Kind.Should().Be(LiveSessionKind.Call);
        await backend.EnforceCallConnectGrace(chatId);

        // assert - the grace window found only Bob genuinely present, so the call closes
        (await backend.GetState(chatId, default)).Should().BeNull();
    }

    [Fact]
    public async Task AcceptedCallWithAListenerShouldSurviveTheGraceWindow()
    {
        // A denied/pending mic permission must not fail the grace check: listening alone is enough
        // presence, per the accept-flow reorder that starts it before the mic prompt resolves.

        // arrange
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var backend = (LiveSessionsBackend)bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.StartCall(chatId, bobAuthor!.Id, new[] { aliceAuthor!.Id }.ToApiArray(), false, default);

        // act - Alice accepts and starts listening (mic still pending/denied); Bob is already present
        // from StartCall
        await backend.AcceptCall(chatId, aliceAuthor.Id, default);
        await backend.SetParticipation(chatId, aliceAuthor.Id, ParticipationKind.AudioListen, true, default);
        await backend.EnforceCallConnectGrace(chatId);

        // assert - both are genuinely present, so the call survives the grace check
        (await backend.GetState(chatId, default)).Should().NotBeNull();
    }

    [Fact]
    public async Task AcceptShouldLatchDialingCallToConnected()
    {
        // arrange — Bob dials Alice; while ringing the session is Dialing (no block: SessionStartedAt null)
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var backend = bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        var chatsBackend = bob.AppServices.GetRequiredService<IChatsBackend>();
        await backend.StartCall(
            chatId, bobAuthor!.Id, new[] { aliceAuthor!.Id }.ToApiArray(), false, default);

        // assert — dialing: no live conversation is surfaced (SessionStartedAt gates every block path)
        (await backend.GetState(chatId, default))!.SessionStartedAt.Should().BeNull();

        // act — Alice answers
        var chatEnd = (await chatsBackend.GetLidRange(chatId, false, default)).End;
        await backend.AcceptCall(chatId, aliceAuthor.Id, default);

        var chatEndAfter = (await chatsBackend.GetLidRange(chatId, false, default)).End;

        // assert — latched: Connected, block surfaced (SessionStartedAt set), VisibleStartLid = answer's chat end
        var state = await backend.GetState(chatId, default);
        state!.Kind.Should().Be(LiveSessionKind.Call);
        state.SessionStartedAt.Should().NotBeNull();
        // AcceptCall reads the chat-end lid at answer time, which falls between our pre-answer and
        // post-answer reads (chat end only grows), so this brackets it without a concurrent-write flake.
        state.VisibleStartLid.Should().BeGreaterThanOrEqualTo(chatEnd);
        state.VisibleStartLid.Should().BeLessThanOrEqualTo(chatEndAfter);
        state.AuthorIds.Should().Contain(aliceAuthor.Id);
    }

    [Fact]
    public async Task RingIdShouldSurviveChatGrowthAcrossLatch()
    {
        // arrange — Bob dials Alice
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var backend = bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.StartCall(chatId, bobAuthor!.Id, new[] { aliceAuthor!.Id }.ToApiArray(), false, default);

        // the ring is published under this id while dialing; it must stay put for the whole call
        var dialing = await backend.GetState(chatId, default);
        var ringId = dialing!.RingConversationId;
        dialing.ConversationId.Should().Be(ringId, "during dialing the block id and ring id coincide");

        // act — a chat entry lands during the ring, advancing the chat end, then Alice answers
        await bob.CreateTextEntry(chatId, "grows the chat end during the ring");
        await backend.AcceptCall(chatId, aliceAuthor.Id, default);

        // assert — the block id moved to the answer point, but the ring id did NOT (so dismissals still match)
        var connected = await backend.GetState(chatId, default);
        connected!.RingConversationId.Should()
            .Be(ringId, "the ring id is latch-stable so DismissRing matches NotifyCall");
        connected.ConversationId.Should().NotBe(ringId, "the block id legitimately moves to the answer's chat end");
    }

    [Fact]
    public async Task DeclineShouldKeepCallWhileAnotherRings()
    {
        // arrange — Bob rings two people
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await using var carol = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        await carol.SignInAsNew("Carol");
        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        await carol.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var carolAuthor = await carol.GetOwnAuthor(chatId);
        var backend = bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.StartCall(
            chatId, bobAuthor!.Id, new[] { aliceAuthor!.Id, carolAuthor!.Id }.ToApiArray(), false, default);

        // act — Alice declines while Carol is still ringing
        await backend.DeclineCall(chatId, aliceAuthor.Id, default);

        // assert — Alice's invite is declined but the call lives on for Carol
        var live = await backend.Get(chatId, default);
        live!.Invites.Should().Contain(i => i.InviteeId == aliceAuthor.Id && i.Status == CallInviteStatus.Declined);
        live.Invites.Should().Contain(i => i.InviteeId == carolAuthor.Id && i.Status == CallInviteStatus.Ringing);
    }

    [Fact]
    public async Task AllDeclinedShouldEndCall()
    {
        // arrange — Bob rings two people, nobody joins
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await using var carol = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        await carol.SignInAsNew("Carol");
        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        await carol.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var carolAuthor = await carol.GetOwnAuthor(chatId);
        var backend = bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.StartCall(
            chatId, bobAuthor!.Id, new[] { aliceAuthor!.Id, carolAuthor!.Id }.ToApiArray(), false, default);

        // act — both invitees decline
        await backend.DeclineCall(chatId, aliceAuthor.Id, default);
        await backend.DeclineCall(chatId, carolAuthor.Id, default);

        // assert — no ring left and nobody joined, so the call is torn down
        (await backend.GetState(chatId, default)).Should().BeNull();
    }

    [Fact]
    public async Task StartCallShouldSetDialingStatus()
    {
        // arrange
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var backend = bob.AppServices.GetRequiredService<ILiveSessionsBackend>();

        // act
        await backend.StartCall(chatId, bobAuthor!.Id, new[] { aliceAuthor!.Id }.ToApiArray(), false, default);

        // assert — the caller sees Dialing right away
        var callState = await backend.GetCallState(chatId, default);
        callState.Should().NotBeNull();
        callState!.Status.Should().Be(CallStatus.Dialing);
        callState.CallerId.Should().Be(bobAuthor.Id);
    }

    [Fact]
    public async Task AcceptShouldSetAcceptedStatus()
    {
        // arrange — Bob rings Alice
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var backend = bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.StartCall(chatId, bobAuthor!.Id, new[] { aliceAuthor!.Id }.ToApiArray(), false, default);

        // act — Alice answers
        await backend.AcceptCall(chatId, aliceAuthor.Id, default);

        // assert — the caller is briefly told the call was accepted
        var callState = await backend.GetCallState(chatId, default);
        callState.Should().NotBeNull();
        callState!.Status.Should().Be(CallStatus.Connecting);
    }

    [Fact]
    public async Task DeclineShouldLeaveDeclinedStatus()
    {
        // arrange — Bob rings Alice
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var backend = bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.StartCall(chatId, bobAuthor!.Id, new[] { aliceAuthor!.Id }.ToApiArray(), false, default);

        // act — Alice declines, which ends the call
        await backend.DeclineCall(chatId, aliceAuthor.Id, default);

        // assert — the session is gone, but Bob is still told why
        (await backend.GetState(chatId, default)).Should().BeNull();
        var callState = await backend.GetCallState(chatId, default);
        callState.Should().NotBeNull();
        callState!.Status.Should().Be(CallStatus.Declined);
        callState.CallerId.Should().Be(bobAuthor.Id);

        // dismiss clears it with the session already gone, so nothing falls back to "calling"
        await backend.DismissCallStatus(chatId, default);
        (await backend.GetCallState(chatId, default)).Should().BeNull();
        (await backend.GetState(chatId, default)).Should().BeNull();
    }

    [Fact]
    public async Task CancelShouldClearStatus()
    {
        // arrange — Bob rings Alice
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var backend = bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.StartCall(chatId, bobAuthor!.Id, new[] { aliceAuthor!.Id }.ToApiArray(), false, default);

        // act — Bob hangs up before anyone answers
        await backend.CancelCall(chatId, bobAuthor.Id, default);

        // assert — hanging up myself leaves no status
        (await backend.GetCallState(chatId, default)).Should().BeNull();
    }

    [Fact]
    public async Task FreshDialingCallShouldNotBeClosedBySelfHeal()
    {
        // arrange — Bob rings Alice
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var backend = bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.StartCall(chatId, bobAuthor!.Id, new[] { aliceAuthor!.Id }.ToApiArray(), false, default);

        // act — the ring is fresh; repeated observation must not trip the no-fresh-ring finalizer
        for (var i = 0; i < 3; i++) {
            var state = await backend.GetState(chatId, default);
            state.Should().NotBeNull("a still-ringing dialing call must stay alive");
            state!.IsDialing.Should().BeTrue();
        }
        (await backend.GetCallState(chatId, default))!.Status.Should()
            .Be(CallStatus.Dialing, "nobody has failed to answer yet");
    }

    [Fact]
    public async Task CallStatusShouldGoToTheCallerOnly()
    {
        // arrange — Bob rings Alice
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var backend = bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.StartCall(chatId, bobAuthor!.Id, new[] { aliceAuthor!.Id }.ToApiArray(), false, default);

        // act — Alice declines
        await backend.DeclineCall(chatId, aliceAuthor.Id, default);

        // assert — the session-scoped facade (what the UI calls) shows it to Bob and hides it from Alice
        var bobSessions = bob.AppServices.GetRequiredService<ILiveSessions>();
        var aliceSessions = alice.AppServices.GetRequiredService<ILiveSessions>();
        (await bobSessions.GetCallStatus(bob.Session, chatId, default)).Should().Be(CallStatus.Declined);
        (await aliceSessions.GetCallStatus(alice.Session, chatId, default)).Should().Be(CallStatus.None);
    }

    [Fact]
    public async Task CallStatusShouldInvalidateAnAlreadyObservedValue()
    {
        // arrange — Bob rings Alice; Bob is already observing the status, like the banner is
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var backend = bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.StartCall(chatId, bobAuthor!.Id, new[] { aliceAuthor!.Id }.ToApiArray(), false, default);

        // capture on the session-scoped facade — exactly what the client's banner subscribes to over RPC
        var sessions = bob.AppServices.GetRequiredService<ILiveSessions>();
        var cStatus = await Computed.Capture(() => sessions.GetCallStatus(bob.Session, chatId, default));
        cStatus.Value.Should().Be(CallStatus.Dialing);

        // act — Alice declines
        await backend.DeclineCall(chatId, aliceAuthor.Id, default);

        // assert — the captured computed flips Dialing → Declined on its own, without a fresh Capture
        await ComputedTest.When(async ct => {
            var status = await sessions.GetCallStatus(bob.Session, chatId, ct);
            status.Should().Be(CallStatus.Declined);
        }, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CallStateShouldInvalidateWheneverStateDoes()
    {
        // Regression test for the root cause behind 9e0b87186c: GetCallState used to invalidate
        // completely independently of GetState/Kind, so an RPC client's two subscriptions could
        // observe them out of order. GetCallState now depends on GetState, so any invalidation of
        // the session state also invalidates the call state - even one that never touches CallState.

        // arrange
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var backend = bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.StartCall(chatId, bobAuthor!.Id, new[] { aliceAuthor!.Id }.ToApiArray(), false, default);

        var cCallState = await Computed.Capture(() => backend.GetCallState(chatId, default));
        cCallState.Value!.Status.Should().Be(CallStatus.Dialing);

        // act - SetRules never touches CallState at all, only LiveSessionState
        await backend.SetRules(chatId, new SessionRules { VoiceModeOverride = Users.VoiceMode.JustText }, default);

        // assert - GetCallState still invalidates, because it now depends on GetState
        await cCallState.WhenInvalidated(default).WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task CancelCallShouldEndTheCall()
    {
        // arrange
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var backend = bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.StartCall(
            chatId, bobAuthor!.Id, new[] { aliceAuthor!.Id }.ToApiArray(), false, default);

        // act — Bob cancels before Alice answers
        await backend.CancelCall(chatId, bobAuthor.Id, default);

        // assert — the unanswered call is torn down
        (await backend.GetState(chatId, default)).Should().BeNull();
    }

    [Fact]
    public async Task DeclineAfterAcceptIsRejectedAsInvalidTransition()
    {
        // arrange
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var backend = bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.StartCall(chatId, bobAuthor!.Id, new[] { aliceAuthor!.Id }.ToApiArray(), false, default);
        await backend.AcceptCall(chatId, aliceAuthor.Id, default);

        // act - a stale Decline arrives after Alice already accepted
        await backend.DeclineCall(chatId, aliceAuthor.Id, default);

        // assert - no-op, stays Accepted (today's behavior, must not regress)
        var live = await backend.Get(chatId, default);
        live!.Invites.Single(i => i.InviteeId == aliceAuthor.Id).Status.Should().Be(CallInviteStatus.Accepted);
    }

    [Fact]
    public async Task CancelCallAfterActiveIsRejectedAsInvalidTransition()
    {
        // arrange - a connected call
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var backend = bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.StartCall(chatId, bobAuthor!.Id, new[] { aliceAuthor!.Id }.ToApiArray(), false, default);
        await backend.AcceptCall(chatId, aliceAuthor.Id, default);
        await backend.SetParticipation(chatId, bobAuthor.Id, ParticipationKind.Record, true, default);
        await backend.SetParticipation(chatId, aliceAuthor.Id, ParticipationKind.Record, true, default);
        await backend.Get(chatId, default); // let GetState's self-heal promote both invites to Active

        // act - CancelCall arrives late, after the call is genuinely connected
        await backend.CancelCall(chatId, bobAuthor.Id, default);

        // assert - the session is untouched by CancelCall; it's still there and still Active
        var state = await backend.GetState(chatId, default);
        state.Should().NotBeNull();
    }

    [Fact]
    public async Task StreamBeforeAcceptShouldLatchDialingCallToConnected()
    {
        // A dialing call reaching the 2-party stream latch (both parties stream before a formal Accept)
        // must become Connected - never left as Dialing with SessionStartedAt set (invariant).

        // arrange
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var backend = bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.StartCall(
            chatId, bobAuthor!.Id, new[] { aliceAuthor!.Id }.ToApiArray(), false, default);
        var dialingState = await backend.GetState(chatId, default);
        dialingState!.Kind.Should().Be(LiveSessionKind.Call);
        dialingState.SessionStartedAt.Should().BeNull();

        // act — both parties stream (no explicit AcceptCall)
        await backend.OnStreamRegistered(chatId, bobAuthor.Id, null, false, true, default);
        await backend.OnStreamRegistered(chatId, aliceAuthor.Id, null, false, true, default);

        // assert — invariant holds: latched → Connected, not Dialing-with-SessionStartedAt
        var state = await backend.GetState(chatId, default);
        state!.SessionStartedAt.Should().NotBeNull();
        state.Kind.Should().Be(LiveSessionKind.Call);
    }

    [Fact]
    public async Task StartCallShouldPromoteExistingSession()
    {
        // arrange — an ambient live session is already running when a call starts
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var backend = bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.OnStreamRegistered(chatId, bobAuthor!.Id, null, true, true, default);
        (await backend.GetState(chatId, default))!.Kind.Should().Be(LiveSessionKind.Ambient);

        // act — Bob rings Alice while that session is live
        await backend.StartCall(chatId, bobAuthor.Id, new[] { aliceAuthor!.Id }.ToApiArray(), false, default);

        // assert — promoting an unlatched (solo) ambient session gives a Dialing call: ring/close paths
        // apply (via IsCall) but no block is surfaced until someone answers.
        var state = await backend.GetState(chatId, default);
        state!.Kind.Should().Be(LiveSessionKind.Call);
        state.SessionStartedAt.Should().BeNull();
        state.Host.Should().Be(bobAuthor.Id);
    }

    [Fact]
    public async Task StartCallOnLatchedSessionShouldStayConnected()
    {
        // arrange — a 2-party ambient session is already latched (block visible) when a call starts
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await bob.CreateChat(true);
        await alice.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var backend = bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.OnStreamRegistered(chatId, bobAuthor!.Id, null, true, true, default);
        await backend.OnStreamRegistered(chatId, aliceAuthor!.Id, null, true, true, default);
        var latched = await backend.GetState(chatId, default);
        latched!.SessionStartedAt.Should().NotBeNull("two streamers latched the ambient session");
        var startedAt = latched.SessionStartedAt;

        // act — Bob rings a third-party author id while that session is live
        await backend.StartCall(
            chatId, bobAuthor.Id, new[] { AuthorId.New(chatId, 777_055) }.ToApiArray(), false, default);

        // assert — monotonic: it stays a connected Call with its latch preserved (block stays)
        var state = await backend.GetState(chatId, default);
        state!.Kind.Should().Be(LiveSessionKind.Call);
        state.SessionStartedAt.Should().Be(startedAt);
        // an already-latched session isn't newly Dialing, so no CallState should be written for it
        (await backend.GetCallState(chatId, default)).Should().BeNull();
    }

    [Fact]
    public async Task LatchShouldSetVisibleStartLidToChatEnd()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        var chatsBackend = tester.AppServices.GetRequiredService<IChatsBackend>();

        // act — a second peer latches the session
        var chatEnd = (await chatsBackend.GetLidRange(chatId, false, default)).End;
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, true, default);
        await backend.OnStreamRegistered(chatId, AuthorId.New(chatId, 777_021), null, true, true, default);
        var chatEndAfter = (await chatsBackend.GetLidRange(chatId, false, default)).End;

        // assert — VisibleStartLid is pinned to the chat end at latch time. The join system entry
        // lands asynchronously, so chat end may grow across the latch: bracket it instead of
        // matching a single read (chat end only grows).
        var live = await backend.GetState(chatId, default);
        live.Should().NotBeNull();
        live!.SessionStartedAt.Should().NotBeNull();
        live.VisibleStartLid.Should().BeGreaterThanOrEqualTo(chatEnd);
        live.VisibleStartLid.Should().BeLessThanOrEqualTo(chatEndAfter);
        live.VisibleStartLid.Should().BeGreaterThan(0);
        live.EffectiveVisibleStartLid.Should().Be(live.VisibleStartLid);
    }

    [Fact]
    public async Task CloseNowShouldKeepLatchedTranscriptionSessionClosing()
    {
        // arrange — a latched transcription session (2 peers)
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        var otherId = AuthorId.New(chatId, 777_022);
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, true, default);
        await backend.OnStreamRegistered(chatId, otherId, null, true, true, default);
        (await backend.GetState(chatId, default))!.SessionStartedAt.Should().NotBeNull();

        // act — everyone leaves
        await backend.SetParticipation(chatId, otherId, ParticipationKind.Record, false, default);
        await backend.SetParticipation(chatId, author.Id, ParticipationKind.Record, false, default);

        // assert — it doesn't vanish instantly; the flow finalizes it, so it stays live-but-closing
        var live = await backend.GetState(chatId, default);
        live.Should().NotBeNull();
        live!.IsClosing.Should().BeTrue();
    }

    [Fact]
    public async Task FinalizeSessionShouldMaterializeContextRange()
    {
        // arrange — a chat with a few entries, then a latched transcription session
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var commander = tester.Commander;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        var conversationsBackend = tester.AppServices.GetRequiredService<IConversationsBackend>();

        var entries = new List<ChatEntry>();
        foreach (var text in new[] { "one", "two", "three" }) {
            var entry = await commander.Call(new Chats_UpsertEntry {
                Session = session,
                ChatId = chatId,
                LocalId = null,
                Text = text,
            });
            entries.Add(entry);
        }
        var contextStart = entries[0].LocalId;
        var endEntryLid = entries[^1].LocalId;

        var otherId = AuthorId.New(chatId, 777_024);
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, true, default);
        await backend.OnStreamRegistered(chatId, otherId, null, true, true, default);

        await backend.SetContextStart(chatId, contextStart, default);
        await backend.UpdateSummary(chatId, new LiveSessionSummary {
            Title = "Recap",
            Description = "A description",
            Summary = "A summary",
            EndEntryLid = endEntryLid,
            MessageCount = 8,
            AuthorIds = [author.Id],
            IsExpandedByDefault = true,
        }, default);

        // everyone leaves -> the session is marked closing
        await backend.SetParticipation(chatId, otherId, ParticipationKind.Record, false, default);
        await backend.SetParticipation(chatId, author.Id, ParticipationKind.Record, false, default);

        // act — finalize materializes the conversation at the context start, then drops the live state
        await backend.FinalizeSession(chatId, default);

        // assert
        (await backend.GetState(chatId, default)).Should().BeNull();
        var materialized = await conversationsBackend.Get(ConversationId.New(chatId, contextStart), default);
        materialized.Should().NotBeNull();
        materialized!.Title.Should().Be("Recap");
        materialized.IsExpandedByDefault.Should().BeTrue();
        materialized.EndEntryLid.Should().Be(endEntryLid);
    }

    [Fact]
    public async Task RangeMetaShouldKeepPreLatchConversationsVisible()
    {
        // arrange — transcription starts solo at e0, a conversation is persisted over [e0, e2] before the
        // session latches (V = chat end after e3), so it sits in [StartEntryLid, VisibleStartLid).
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var commander = tester.Commander;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        var conversationsBackend = tester.AppServices.GetRequiredService<IConversationsBackend>();

        var e0 = await commander.Call(new Chats_UpsertEntry {
            Session = session,
            ChatId = chatId,
            LocalId = null,
            Text = "e0",
        });
        // solo, StartEntryLid = e0
        await backend.OnStreamRegistered(chatId, author!.Id, e0.LocalId, true, true, default);
        await commander.Call(new Chats_UpsertEntry { Session = session, ChatId = chatId, LocalId = null, Text = "e1" });
        var e2 = await commander.Call(new Chats_UpsertEntry {
            Session = session,
            ChatId = chatId,
            LocalId = null,
            Text = "e2",
        });

        var preLatch = new Conversation(ConversationId.New(chatId, e0.LocalId), 1) {
            Title = "Earlier", Description = "d", Summary = "s", MessageCount = 3,
            EndEntryLid = e2.LocalId,
            StartsAt = e0.BeginsAt, EndsAt = e2.BeginsAt,
        };
        await commander.Call(new ConversationBackend_Materialize(preLatch));

        await commander.Call(new Chats_UpsertEntry { Session = session, ChatId = chatId, LocalId = null, Text = "e3" });
        await backend.OnStreamRegistered(chatId, AuthorId.New(chatId, 777_025), null, true, true, default); // latch
        (await backend.GetState(chatId, default))!.SessionStartedAt.Should().NotBeNull();

        // act
        var idTileStart = Constants.Chat.RangeMetaEntryIdTiles.GetTile(e0.LocalId).Range.Start;
        var meta = await conversationsBackend.GetRangeMeta(chatId, idTileStart, default);

        // assert — the pre-latch conversation's exact range survives; the live range no longer swallows it
        meta.ConversationLidRanges.Should().Contain(new Range<long>(e0.LocalId, e2.LocalId + 1));
    }

    [Fact]
    public void SummaryFlowNameShouldMatchConstant()
    {
        // The streaming backend wakes the flow by this string name (it can't reference the flow type);
        // if the flow is renamed, this guards that LiveFlows.SummaryFlowName is updated with it.

        // arrange
        var flowHub = AppHost.Services.GetRequiredService<ActualChat.Flows.FlowHub>();

        // act
        var name = flowHub.NewId<ActualChat.Chat.Flows.LiveConversationSummaryFlow>("x").Name.Value;

        // assert
        name.Should().Be(LiveFlows.SummaryFlowName);
    }

    [Fact]
    public async Task DeclineShouldRecordDeclinedOutcome()
    {
        // arrange — Bob rings two people
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await using var carol = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        await carol.SignInAsNew("Carol");
        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        await carol.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var carolAuthor = await carol.GetOwnAuthor(chatId);
        var backend = bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.StartCall(
            chatId, bobAuthor!.Id, new[] { aliceAuthor!.Id, carolAuthor!.Id }.ToApiArray(), false, default);

        // act — Alice declines while Carol is still ringing, so the call is not abandoned
        await backend.DeclineCall(chatId, aliceAuthor.Id, default);

        // assert — Carol is still ringing, so the call isn't abandoned and the state is never closed
        var state = await backend.GetState(chatId, default);
        state!.Outcome.Should().Be(CallOutcome.Declined);
    }

    [Fact]
    public async Task DeclinedOutcomeShouldOutrankALaterNoAnswer()
    {
        // Every real caller that would record NoAnswer (ExpireRings, once the call is fully abandoned)
        // also closes the call in the same operation, which drops the Redis state before a test could
        // read it back - so the precedence guard is pinned by calling the guarded write directly instead
        // of driving a real ring timeout through to its unobservable close.

        // arrange — Bob rings two people; Alice declines, leaving a live, non-abandoned session
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await using var carol = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        await carol.SignInAsNew("Carol");
        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        await carol.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var carolAuthor = await carol.GetOwnAuthor(chatId);
        var backend = (LiveSessionsBackend)bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.StartCall(
            chatId, bobAuthor!.Id, new[] { aliceAuthor!.Id, carolAuthor!.Id }.ToApiArray(), false, default);
        await backend.DeclineCall(chatId, aliceAuthor.Id, default);
        var declined = await backend.GetState(chatId, default);
        declined!.Outcome.Should().Be(CallOutcome.Declined);

        // act — Carol's ring times out: this is the exact write ExpireRings would make if it could
        await backend.SetOutcome(chatId, declined, CallOutcome.NoAnswer);

        // assert — the earlier decline still wins
        var state = await backend.GetState(chatId, default);
        state!.Outcome.Should().Be(CallOutcome.Declined);
    }

    [Fact]
    public async Task StartCallShouldRememberHasVideo()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var (chatId, bobAuthor, aliceAuthor) = await NewTwoPartyCall(tester);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();

        // act
        await backend.StartCall(chatId, bobAuthor.Id, new[] { aliceAuthor.Id }.ToApiArray(), true, default);

        // assert
        var state = await backend.GetState(chatId, default);
        state!.HasVideo.Should().BeTrue();
    }

    [Fact]
    public async Task PresenceDropBelowTwoShouldCloseTheCall()
    {
        // The mid-call symmetric-hangup path: SetParticipation is what the connection-lifetime hooks
        // (LiveAudioStreams, AudioStreamingBackend) call when a stream's connection drops, and it's also
        // what an explicit hang-up goes through - either way it must enforce the ">= 2" invariant.

        // arrange - Bob records (stays live), Alice listens then drops. This isolates the new
        // shouldCloseAsCall path: the old emptiedByLeave wouldn't fire, but the >=2 check does.
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var backend = bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.StartCall(
            chatId, bobAuthor!.Id, new[] { aliceAuthor!.Id }.ToApiArray(), false, default);
        await backend.AcceptCall(chatId, aliceAuthor.Id, default);
        await backend.SetParticipation(
            chatId, bobAuthor.Id, ParticipationKind.Record, true, default);
        await backend.SetParticipation(
            chatId, aliceAuthor.Id, ParticipationKind.AudioListen, true, default);

        // act - Alice's listening stream drops (connection lost), leaving Bob as the sole participant
        await backend.SetParticipation(
            chatId, aliceAuthor.Id, ParticipationKind.AudioListen, false, default);

        // assert - the call closes on the new shouldCloseAsCall path since ParticipantCount drops
        // below 2, regardless of IsSessionLive (which would still be true due to Bob recording)
        (await backend.GetState(chatId, default)).Should().BeNull();
    }

    [Fact]
    public async Task PresenceDropOnAnAmbientSessionShouldNotCloseIt()
    {
        // The new invariant is scoped to Kind == Call only - an Ambient live conversation losing a
        // listener while a recorder stays on must keep running exactly as before.

        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var authors = tester.AppServices.GetRequiredService<IAuthors>();
        var author = await authors.GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, true, default);
        var listenerId = AuthorId.New(chatId, 777_050);
        await backend.SetParticipation(
            chatId, listenerId, ParticipationKind.AudioListen, true, default);

        // act - the listener leaves; the recorder is still streaming
        await backend.SetParticipation(
            chatId, listenerId, ParticipationKind.AudioListen, false, default);

        // assert - unaffected: still live, not closing
        var live = await backend.GetState(chatId, default);
        live.Should().NotBeNull();
        live!.IsClosing.Should().BeFalse();
    }

    [Fact]
    public async Task AcceptCallRecomputesStatusToConnecting()
    {
        // arrange
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var backend = bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.StartCall(chatId, bobAuthor!.Id, new[] { aliceAuthor!.Id }.ToApiArray(), false, default);

        // act
        await backend.AcceptCall(chatId, aliceAuthor.Id, default);

        // assert
        var callState = await backend.GetCallState(chatId, default);
        callState!.Status.Should().Be(CallStatus.Connecting);
    }

    [Fact]
    public async Task ExpireRingsRecomputesStatusToNoAnswer()
    {
        // This drives a genuine ring timeout (RingTimeout = Constants.Call.RingTimeout = 20s) rather
        // than calling ExpireRings before the ring is actually stale - there's no clock-injection seam
        // for this backend's Redis-timestamp-based timeout, so the real ~21s wait is the honest way to
        // exercise the abandon-check block's RecomputeCallStatus call end-to-end.

        // arrange
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var backend = (LiveSessionsBackend)bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.StartCall(chatId, bobAuthor!.Id, new[] { aliceAuthor!.Id }.ToApiArray(), false, default);

        // act - wait past the real ring timeout, then drive the expiry check
        await Task.Delay(TimeSpan.FromSeconds(21));
        await backend.ExpireRings(chatId);

        // assert - the call is fully abandoned (nobody ever answered), so RecomputeCallStatus inside
        // ExpireRings' abandon-check block lands NoAnswer - the session itself is gone (CloseCall),
        // but the caller-facing CallState survives to explain why
        (await backend.GetState(chatId, default)).Should().BeNull();
        var callState = await backend.GetCallState(chatId, default);
        callState!.Status.Should().Be(CallStatus.NoAnswer);
    }

    [Fact]
    public async Task SecondInviteeAcceptingDoesNotErrorOrRegressStatus()
    {
        // AcceptCall's RecomputeCallStatus call only runs on the first accept - the branch is gated on
        // SessionStartedAt being null, which the first accept's latch already clears - so this does NOT
        // prove multi-invite fact-folding (that needs SyncCallParticipantActivity, which is Task 5's
        // job). What this guards: a second accept in an already-connected group call must not throw
        // and must not regress CallStatus back toward Dialing.

        // arrange - a group call: Owner calls Moderator and Member
        await using var owner = AppHost.NewBlazorTester(Out);
        await using var member1 = AppHost.NewBlazorTester(Out);
        await using var member2 = AppHost.NewBlazorTester(Out);
        await owner.SignInAsUniqueBob();
        await member1.SignInAsUniqueAlice();
        await member2.SignInAsUniqueAlice();
        var (chatId, inviteId) = await owner.CreateChat(false);
        var member1Author = await member1.JoinChat(chatId, inviteId);
        var member2Author = await member2.JoinChat(chatId, inviteId);
        var ownerAuthor = await owner.GetOwnAuthor(chatId);
        var backend = owner.AppServices.GetRequiredService<ILiveSessionsBackend>();
        var invitees = new[] { member1Author.Id, member2Author.Id }.ToApiArray();
        await backend.StartCall(chatId, ownerAuthor!.Id, invitees, false, default);

        // act
        await backend.AcceptCall(chatId, member1Author.Id, default);
        Func<Task> secondAccept = () => backend.AcceptCall(chatId, member2Author.Id, default);

        // assert - the second accept doesn't throw, and status hasn't regressed to Dialing
        await secondAccept.Should().NotThrowAsync();
        var callState = await backend.GetCallState(chatId, default);
        callState!.Status.Should().Be(CallStatus.Connecting);
    }

    [Fact]
    public void DeriveReturnsDialingWithNoFactsYet()
    {
        var status = LiveSessionsBackend.Derive(callState: null, invites: []);
        status.Should().Be(CallStatus.Dialing);
    }

    [Fact]
    public void DeriveReturnsConnectingWhenAnInviteeAccepted()
    {
        var chatId = ChatId.Parse(GroupChatId.New().Value);
        var invite = new CallInvite {
            InviteeId = AuthorId.New(chatId, 1),
            Status = CallInviteStatus.Accepted
        };
        var status = LiveSessionsBackend.Derive(callState: null, invites: [invite]);
        status.Should().Be(CallStatus.Connecting);
    }

    [Fact]
    public void DeriveReturnsActiveWhenTwoAreGenuinelyPresent()
    {
        var chatId = ChatId.Parse(GroupChatId.New().Value);
        var callState = new CallState {
            CallerId = AuthorId.New(chatId, 1),
            CallerActiveAt = Moment.Now
        };
        var invite = new CallInvite {
            InviteeId = AuthorId.New(chatId, 2),
            Status = CallInviteStatus.Active
        };
        var status = LiveSessionsBackend.Derive(callState, invites: [invite]);
        status.Should().Be(CallStatus.Active);
    }

    [Fact]
    public void DeriveReturnsEndedOnceItWasActiveEvenIfNoLongerActive()
    {
        var chatId = ChatId.Parse(GroupChatId.New().Value);
        var callState = new CallState {
            CallerId = AuthorId.New(chatId, 1),
            CallerActiveAt = Moment.Now - TimeSpan.FromMinutes(1),
            CallerEndedAt = Moment.Now,
        };
        var invite = new CallInvite {
            InviteeId = AuthorId.New(chatId, 2),
            Status = CallInviteStatus.Ended
        };
        var status = LiveSessionsBackend.Derive(callState, invites: [invite]);
        status.Should().Be(CallStatus.Ended);
    }

    [Fact]
    public void DeriveReturnsCanceledWhenCallerCanceledBeforeEverConnecting()
    {
        var chatId = ChatId.Parse(GroupChatId.New().Value);
        var callState = new CallState {
            CallerId = AuthorId.New(chatId, 1),
            CanceledAt = Moment.Now
        };
        var status = LiveSessionsBackend.Derive(callState, invites: []);
        status.Should().Be(CallStatus.Canceled);
    }

    [Fact]
    public void DeriveReturnsDeclinedWhenAnInviteeDeclinedAndNoneEverAccepted()
    {
        var chatId = ChatId.Parse(GroupChatId.New().Value);
        var invite = new CallInvite {
            InviteeId = AuthorId.New(chatId, 1),
            Status = CallInviteStatus.Declined
        };
        var status = LiveSessionsBackend.Derive(callState: null, invites: [invite]);
        status.Should().Be(CallStatus.Declined);
    }

    [Fact]
    public void DeriveReturnsNoAnswerWhenEveryInviteeMissed()
    {
        var chatId = ChatId.Parse(GroupChatId.New().Value);
        var invite1 = new CallInvite {
            InviteeId = AuthorId.New(chatId, 1),
            Status = CallInviteStatus.Missed
        };
        var invite2 = new CallInvite {
            InviteeId = AuthorId.New(chatId, 2),
            Status = CallInviteStatus.Missed
        };
        var status = LiveSessionsBackend.Derive(callState: null, invites: [invite1, invite2]);
        status.Should().Be(CallStatus.NoAnswer);
    }

    [Fact]
    public async Task SyncMarksInviteeActiveThenEndedFromRealPresence()
    {
        // arrange
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var backend = (LiveSessionsBackend)bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.StartCall(chatId, bobAuthor!.Id, new[] { aliceAuthor!.Id }.ToApiArray(), false, default);
        await backend.AcceptCall(chatId, aliceAuthor.Id, default);
        // A third, uninvited stream keeps overall presence at >= 2 once Alice's own drops - this isolates
        // the per-invite Active/Ended transition under test from the unrelated ">=2 participants" whole-
        // call-close invariant (PresenceDropBelowTwoShouldCloseTheCall), which would otherwise tear the
        // call down the moment Alice's presence deactivates below.
        var boosterId = AuthorId.New(chatId, 999_001);
        await backend.SetParticipation(chatId, boosterId, ParticipationKind.Record, true, default);

        // act - Alice's presence genuinely registers. GetConsolidatedParticipants carries a real 200ms
        // ConsolidationDelay before an outside observer sees a presence change land, so poll for it to
        // settle rather than read a still-stale consolidated snapshot (same idea as
        // ExpireRingsRecomputesStatusToNoAnswer's real ring-timeout wait, just a much shorter window).
        await backend.SetParticipation(chatId, aliceAuthor.Id, ParticipationKind.Record, true, default);
        await WaitForParticipantPresence(backend, chatId, aliceAuthor.Id, isPresent: true);
        var state = await backend.GetState(chatId, default);
        await backend.SyncCallParticipantActivity(chatId, state!, default);

        // assert - promoted straight to Active, not stuck at Accepted
        var live = await backend.Get(chatId, default);
        live!.Invites.Single(i => i.InviteeId == aliceAuthor.Id).Status.Should().Be(CallInviteStatus.Active);

        // act - Alice's presence deactivates
        await backend.SetParticipation(chatId, aliceAuthor.Id, ParticipationKind.Record, false, default);
        await WaitForParticipantPresence(backend, chatId, aliceAuthor.Id, isPresent: false);
        state = await backend.GetState(chatId, default);
        await backend.SyncCallParticipantActivity(chatId, state!, default);

        // assert - Ended, not reverted to Accepted or left at Active
        live = await backend.Get(chatId, default);
        live!.Invites.Single(i => i.InviteeId == aliceAuthor.Id).Status.Should().Be(CallInviteStatus.Ended);
    }

    [Fact]
    public async Task SyncNeverReactivatesAnEndedInvitee()
    {
        // arrange - same setup as above, driven straight to Ended
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var backend = (LiveSessionsBackend)bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.StartCall(chatId, bobAuthor!.Id, new[] { aliceAuthor!.Id }.ToApiArray(), false, default);
        await backend.AcceptCall(chatId, aliceAuthor.Id, default);
        // See SyncMarksInviteeActiveThenEndedFromRealPresence - keeps overall presence >= 2 once Alice
        // drops, and polling for each presence change to land lets GetConsolidatedParticipants' real
        // 200ms ConsolidationDelay settle, instead of asserting on a still-stale consolidated snapshot.
        var boosterId = AuthorId.New(chatId, 999_001);
        await backend.SetParticipation(chatId, boosterId, ParticipationKind.Record, true, default);
        await backend.SetParticipation(chatId, aliceAuthor.Id, ParticipationKind.Record, true, default);
        await WaitForParticipantPresence(backend, chatId, aliceAuthor.Id, isPresent: true);
        var state = await backend.GetState(chatId, default);
        await backend.SyncCallParticipantActivity(chatId, state!, default);
        await backend.SetParticipation(chatId, aliceAuthor.Id, ParticipationKind.Record, false, default);
        await WaitForParticipantPresence(backend, chatId, aliceAuthor.Id, isPresent: false);
        state = await backend.GetState(chatId, default);
        await backend.SyncCallParticipantActivity(chatId, state!, default);

        // act - Alice's presence somehow comes back (e.g. a stray late heartbeat)
        await backend.SetParticipation(chatId, aliceAuthor.Id, ParticipationKind.Record, true, default);
        await WaitForParticipantPresence(backend, chatId, aliceAuthor.Id, isPresent: true);
        state = await backend.GetState(chatId, default);
        await backend.SyncCallParticipantActivity(chatId, state!, default);

        // assert - stays Ended, not resurrected to Active
        var live = await backend.Get(chatId, default);
        live!.Invites.Single(i => i.InviteeId == aliceAuthor.Id).Status.Should().Be(CallInviteStatus.Ended);
    }

    [Fact]
    public async Task SyncDoesNotEndAStillDialingCall()
    {
        // Regression guard: EnsureParticipant registers the caller as a fresh participant the moment
        // they dial (StartCall), well before anyone answers. Without SyncCallerActivity's >= 2 fresh-
        // participants gate, the very first self-heal tick would flip CallerActiveAt on the caller alone,
        // and Derive would then see a lone-ever-active party and report the whole call Ended before the
        // invitee even had a chance to pick up.

        // arrange
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var backend = (LiveSessionsBackend)bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.StartCall(chatId, bobAuthor!.Id, new[] { aliceAuthor!.Id }.ToApiArray(), false, default);

        // act - nobody has answered yet; drive the sync directly, as GetState's self-heal would
        var state = await backend.GetState(chatId, default);
        await backend.SyncCallParticipantActivity(chatId, state!, default);

        // assert - still Dialing, not prematurely Ended
        var callState = await backend.GetCallState(chatId, default);
        callState!.Status.Should().Be(CallStatus.Dialing);
    }

    [Fact]
    public async Task SyncDoesNotEndAStillDialingCallWithAnUnrelatedBystanderFresh()
    {
        // Regression guard for a roster-scope bug: an unrelated fresh participant - present in the same
        // chat for a reason that has nothing to do with this call (an already-latched Ambient session,
        // or just another member's own stream) - must not satisfy the >= 2 gate on its own. Only fresh
        // members of THIS call's own roster (the caller and its invitees) may count toward it.

        // arrange - Bob rings Alice; Carol is independently fresh in the same chat, unrelated to the ring
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await using var carol = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        await carol.SignInAsNew("Carol");
        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        await carol.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var carolAuthor = await carol.GetOwnAuthor(chatId);
        var backend = (LiveSessionsBackend)bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.StartCall(chatId, bobAuthor!.Id, new[] { aliceAuthor!.Id }.ToApiArray(), false, default);

        // act - Carol streams for a reason unrelated to the ring (she was never invited to this call);
        // nobody has answered Bob's ring yet
        await backend.SetParticipation(chatId, carolAuthor!.Id, ParticipationKind.Record, true, default);
        await WaitForParticipantPresence(backend, chatId, carolAuthor.Id, isPresent: true);
        var state = await backend.GetState(chatId, default);
        await backend.SyncCallParticipantActivity(chatId, state!, default);

        // assert - still Dialing: Carol's unrelated presence must not satisfy the roster-scoped gate
        var callState = await backend.GetCallState(chatId, default);
        callState!.Status.Should().Be(CallStatus.Dialing);
    }

    [Fact]
    public async Task GetStateSelfHealSyncsCallParticipantActivity()
    {
        // arrange
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var backend = bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.StartCall(chatId, bobAuthor!.Id, new[] { aliceAuthor!.Id }.ToApiArray(), false, default);
        await backend.AcceptCall(chatId, aliceAuthor.Id, default);
        await backend.SetParticipation(chatId, aliceAuthor.Id, ParticipationKind.Record, true, default);

        // act - GetState's own self-heal should pick this up without any direct sync call; the sync it
        // fires is fire-and-forget (matching ExpireRings), so poll briefly rather than assert right after
        // a single GetState call, which would race the background task.
        var status = CallInviteStatus.New;
        for (var attempt = 0; attempt < 50 && status != CallInviteStatus.Active; attempt++) {
            await backend.GetState(chatId, default);
            var live = await backend.Get(chatId, default);
            status = live!.Invites.Single(i => i.InviteeId == aliceAuthor.Id).Status;
            if (status != CallInviteStatus.Active)
                await Task.Delay(100);
        }

        // assert
        status.Should().Be(CallInviteStatus.Active);
    }

    private static async Task WaitForParticipantPresence(
        ILiveSessionsBackend backend, ChatId chatId, AuthorId authorId, bool isPresent)
    {
        // GetConsolidatedParticipants carries a real ~200ms ConsolidationDelay before an outside
        // observer sees a presence change land - poll rather than assert on a still-stale snapshot.
        for (var attempt = 0; attempt < 30; attempt++) {
            var participants = await backend.ListParticipants(chatId, default);
            if (participants.Contains(authorId) == isPresent)
                return;
            await Task.Delay(100);
        }
    }

    private static async Task<(ChatId ChatId, AuthorFull Bob, AuthorFull Alice)> NewTwoPartyCall(
        IWebTester tester)
    {
        var bob = await tester.SignInAsUniqueBob();
        var alice = await tester.SignInAsUniqueAlice();
        await tester.SignIn(bob);
        ChatId chatId = PeerChatId.New(bob.Id, alice.Id);
        var authors = tester.AppServices.GetRequiredService<IAuthorsBackend>();
        var bobAuthor = await authors.EnsureJoined(chatId, bob.Id, default);
        var aliceAuthor = await authors.EnsureJoined(chatId, alice.Id, default);
        return (chatId, bobAuthor, aliceAuthor);
    }
}
