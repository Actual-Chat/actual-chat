using ActualChat.Live;
using ActualChat.Streaming;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class LiveSessionsTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task SessionStaysLiveWhileRecordingThenCloses()
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
    public async Task PhoneModeStaysLiveWhileRecordingThenCloses()
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
    public async Task ParticipationIsTracked()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.OnStreamRegistered(chatId, author!.Id, null, false, true, default);
        var authorId = author.Id;

        // act + assert — a streamer is auto-registered as a participant (recorders join the registry)
        await ComputedTest.When(async ct => (await backend.ListParticipants(chatId, ct)).Contains(authorId).Should().BeTrue());

        // an explicit leave removes them
        await backend.SetParticipation(chatId, authorId, ParticipationKind.AudioListen, false, default);
        await ComputedTest.When(async ct => (await backend.ListParticipants(chatId, ct)).Contains(authorId).Should().BeFalse());

        // and they can re-join
        await backend.SetParticipation(chatId, authorId, ParticipationKind.AudioListen, true, default);
        await ComputedTest.When(async ct => (await backend.ListParticipants(chatId, ct)).Contains(authorId).Should().BeTrue());
    }

    [Fact]
    public async Task ExplicitLeaveClosesImmediately()
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
        (await backend.ListParticipants(chatId, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task RejoinAfterCloseStartsFreshSession()
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
    public async Task LeaveWithOthersPresentKeepsSessionLive()
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
    public async Task RecorderLeavingWithOnlyListenerLeftClosesSession()
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
        await backend.OnStreamRegistered(chatId, bobAuthor!.Id, null, true, default);
        await backend.OnStreamRegistered(chatId, aliceAuthor!.Id, null, true, default);
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
    public async Task RecorderDowngradingToListenerMarksSessionClosing()
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
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, default);
        (await backend.GetState(chatId, default))!.IsClosing.Should().BeFalse();

        // act — recording stops but the same author stays on as a listener (mic off, still listening)
        await backend.SetParticipation(chatId, author!.Id, ParticipationKind.AudioListen, true, default);

        // assert — no recorder is left, so the session is marked closing
        var live = await backend.GetState(chatId, default);
        live.Should().NotBeNull();
        live!.IsClosing.Should().BeTrue("a lone listener can't keep the session live");
    }

    [Fact]
    public async Task LastRecorderDowngradingToListenerClosesSession()
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
        await backend.OnStreamRegistered(chatId, bobAuthor!.Id, null, true, default);
        await backend.OnStreamRegistered(chatId, aliceAuthor!.Id, null, true, default);

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
    public async Task LiveSessionExposesHostAndMembers()
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
    public async Task SetRulesPersistsVoiceModeOverride()
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
    public async Task MutePeerSetsMicMuted()
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
    public async Task MutePeerAllowsSelfButRequiresManageForPeers()
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

        // act + assert — a non-host participant may (un)mute themselves
        Func<Task> selfMute = () => liveSessions.MutePeer(alice.Session, chatId, aliceAuthor!.Id, true, default);
        await selfMute.Should().NotThrowAsync();

        // but may not mute another peer
        Func<Task> mutePeer = () => liveSessions.MutePeer(alice.Session, chatId, bobAuthor!.Id, true, default);
        await mutePeer.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SessionLatchesOnSecondStreamer()
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
    public async Task GetNullUntilSecondStreamer()
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
    public async Task SessionPersistsAcrossVadGap()
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
    public async Task LiveBlockEntersRangeMetaOnlyAfterLatch()
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
        var tileStart = Constants.Chat.ServerIdTileStack.LastLayer.GetTile(live!.StartEntryLid).Range.Start;

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
        var liveTileStart = Constants.Chat.ServerIdTileStack.LastLayer.GetTile(liveStartLid).Range.Start;
        await ComputedTest.When(async ct => {
            var metaAfter = await conversations.GetRangeMeta(chatId, liveTileStart, ct);
            metaAfter.ConversationLidRanges.Should().Contain(r => r.Contains(liveStartLid));
        });
    }

    [Fact]
    public async Task LiveBlockEntersTileOnlyAfterLatch()
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
        var tileRange = Constants.Chat.ServerIdTileStack.LastLayer.GetTile(live!.StartEntryLid).Range;

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
    public async Task SilentRecorderStaysPresentMember()
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
    public async Task ListenerDoesNotKeepSessionAlive()
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
    public async Task HasRecorderReflectsRegistry()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();

        // act + assert — a streamer is a recorder
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, true, default);
        await ComputedTest.When(async ct => (await backend.HasRecorder(chatId, ct)).Should().BeTrue());

        // recording stops → no recorder
        await backend.SetParticipation(chatId, author!.Id, ParticipationKind.Record, false, default);
        await ComputedTest.When(async ct => (await backend.HasRecorder(chatId, ct)).Should().BeFalse());
    }

    [Fact]
    public async Task TrailingUtteranceDoesNotResurrectRecorder()
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
    public async Task StartCallRingsInvitee()
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
        state!.Kind.Should().Be(LiveSessionKind.Dialing);
        state.SessionStartedAt.Should().BeNull();
        // the Call tab still gets a projection while dialing, with the ring visible and no conversation
        var live = await backend.Get(chatId, default);
        live.Should().NotBeNull();
        live!.Conversation.Should().BeNull();
        live.Invites.Should().ContainSingle(i =>
            i.InviteeId == aliceAuthor.Id && i.Status == CallInviteStatus.Ringing);
    }

    [Fact]
    public async Task AcceptCallJoinsCall()
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

        // act — Alice answers
        await backend.AcceptCall(chatId, aliceAuthor.Id, default);

        // assert — the invite is accepted and Alice is now a participant
        var live = await backend.Get(chatId, default);
        live!.Invites.Should().ContainSingle(i =>
            i.InviteeId == aliceAuthor.Id && i.Status == CallInviteStatus.Accepted);
        (await backend.ListParticipants(chatId, default)).Should().Contain(aliceAuthor.Id);
        // the answer latches the dialing call to Connected: block now surfaced
        var state = await backend.GetState(chatId, default);
        state!.Kind.Should().Be(LiveSessionKind.Call);
        state.SessionStartedAt.Should().NotBeNull();
        state.AuthorIds.Should().Contain(aliceAuthor.Id);
    }

    [Fact]
    public async Task AcceptLatchesDialingCallToConnected()
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
    public async Task RingIdSurvivesChatGrowthAcrossLatch()
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
    public async Task DeclineKeepsCallWhileAnotherRings()
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
    public async Task AllDeclinedEndsCall()
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
    public async Task StartCallSetsDialingStatus()
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
    public async Task AcceptSetsAcceptedStatus()
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
        callState!.Status.Should().Be(CallStatus.Accepted);
    }

    [Fact]
    public async Task DeclineLeavesDeclinedStatus()
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
    public async Task CancelClearsStatus()
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
    public async Task FreshDialingCallIsNotClosedBySelfHeal()
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
    public async Task CallStatusGoesToTheCallerOnly()
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
    public async Task CallStatusInvalidatesAnAlreadyObservedValue()
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
    public async Task CancelCallEndsTheCall()
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
    public async Task StreamBeforeAcceptLatchesDialingCallToConnected()
    {
        // A dialing call reaching the 2-party stream latch (both parties stream before a formal Accept)
        // must become Connected - never left as Dialing with SessionStartedAt set (invariant).
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
        (await backend.GetState(chatId, default))!.Kind.Should().Be(LiveSessionKind.Dialing);

        // act — both parties stream (no explicit AcceptCall)
        await backend.OnStreamRegistered(chatId, bobAuthor.Id, null, false, default);
        await backend.OnStreamRegistered(chatId, aliceAuthor.Id, null, false, default);

        // assert — invariant holds: latched → Connected, not Dialing-with-SessionStartedAt
        var state = await backend.GetState(chatId, default);
        state!.SessionStartedAt.Should().NotBeNull();
        state.Kind.Should().Be(LiveSessionKind.Call);
    }

    [Fact]
    public async Task LeaveCallEndsCallBelowTwo()
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
        await backend.AcceptCall(chatId, aliceAuthor.Id, default);

        // act — one of the two participants hangs up
        await backend.LeaveCall(chatId, aliceAuthor.Id, default);

        // assert — a call needs two, so dropping below that closes it
        (await backend.GetState(chatId, default)).Should().BeNull();
    }

    [Fact]
    public async Task StartCallPromotesExistingSession()
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
        state!.Kind.Should().Be(LiveSessionKind.Dialing);
        state.SessionStartedAt.Should().BeNull();
        state.Host.Should().Be(bobAuthor.Id);
    }

    [Fact]
    public async Task StartCallOnLatchedSessionStaysConnected()
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
        await backend.OnStreamRegistered(chatId, bobAuthor!.Id, null, true, default);
        await backend.OnStreamRegistered(chatId, aliceAuthor!.Id, null, true, default);
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
    }

    [Fact]
    public async Task LatchSetsVisibleStartLidToChatEnd()
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
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, default);
        await backend.OnStreamRegistered(chatId, AuthorId.New(chatId, 777_021), null, true, default);
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
    public async Task CloseNowKeepsLatchedTranscriptionSessionClosing()
    {
        // arrange — a latched transcription session (2 peers)
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        var otherId = AuthorId.New(chatId, 777_022);
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, default);
        await backend.OnStreamRegistered(chatId, otherId, null, true, default);
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
    public async Task FinalizeSessionMaterializesContextRange()
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
            var entry = await commander.Call(new Chats_UpsertEntry(session, chatId, null) { Text = text });
            entries.Add(entry);
        }
        var contextStart = entries[0].LocalId;
        var endEntryLid = entries[^1].LocalId;

        var otherId = AuthorId.New(chatId, 777_024);
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, default);
        await backend.OnStreamRegistered(chatId, otherId, null, true, default);

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
    public async Task RangeMetaKeepsPreLatchConversationsVisible()
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

        var e0 = await commander.Call(new Chats_UpsertEntry(session, chatId, null) { Text = "e0" });
        await backend.OnStreamRegistered(chatId, author!.Id, e0.LocalId, true, default); // solo, StartEntryLid = e0
        await commander.Call(new Chats_UpsertEntry(session, chatId, null) { Text = "e1" });
        var e2 = await commander.Call(new Chats_UpsertEntry(session, chatId, null) { Text = "e2" });

        var preLatch = new Conversation(ConversationId.New(chatId, e0.LocalId), 1) {
            Title = "Earlier", Description = "d", Summary = "s", MessageCount = 3,
            EndEntryLid = e2.LocalId,
            StartsAt = e0.BeginsAt, EndsAt = e2.BeginsAt,
        };
        await commander.Call(new ConversationBackend_Materialize(preLatch));

        await commander.Call(new Chats_UpsertEntry(session, chatId, null) { Text = "e3" });
        await backend.OnStreamRegistered(chatId, AuthorId.New(chatId, 777_025), null, true, default); // latch
        (await backend.GetState(chatId, default))!.SessionStartedAt.Should().NotBeNull();

        // act
        var idTileStart = Constants.Chat.ServerIdTileStack.LastLayer.GetTile(e0.LocalId).Range.Start;
        var meta = await conversationsBackend.GetRangeMeta(chatId, idTileStart, default);

        // assert — the pre-latch conversation's exact range survives; the live range no longer swallows it
        meta.ConversationLidRanges.Should().Contain(new Range<long>(e0.LocalId, e2.LocalId + 1));
    }

    [Fact]
    public void SummaryFlowNameMatchesConstant()
    {
        // The streaming backend wakes the flow by this string name (it can't reference the flow type);
        // if the flow is renamed, this guards that LiveFlows.SummaryFlowName is updated with it.
        var flowHub = AppHost.Services.GetRequiredService<ActualChat.Flows.FlowHub>();
        var name = flowHub.NewId<ActualChat.Chat.Flows.LiveConversationSummaryFlow>("x").Name.Value;
        name.Should().Be(LiveFlows.SummaryFlowName);
    }
}
