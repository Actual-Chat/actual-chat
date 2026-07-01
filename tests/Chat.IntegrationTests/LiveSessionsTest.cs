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
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, default);

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
        await backend.OnStreamRegistered(chatId, author!.Id, null, false, default);

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
        await backend.OnStreamRegistered(chatId, author!.Id, null, false, default);
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
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, default);

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
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, default);
        await backend.SetParticipation(chatId, author!.Id, ParticipationKind.Record, false, default);
        (await backend.GetState(chatId, default)).Should().BeNull();

        // act — recording resumes after the close
        await backend.OnStreamRegistered(chatId, author.Id, null, true, default);

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
        await backend.OnStreamRegistered(chatId, bobAuthor!.Id, null, true, default);
        await backend.OnStreamRegistered(chatId, aliceAuthor!.Id, null, true, default);

        // act — one of two participants leaves
        await backend.SetParticipation(chatId, aliceAuthor!.Id, ParticipationKind.Record, false, default);

        // assert — the other keeps the call alive (not closing, not closed)
        var live = await backend.GetState(chatId, default);
        live.Should().NotBeNull();
        live!.IsClosing.Should().BeFalse();

        // act — the last participant leaves too
        await backend.SetParticipation(chatId, bobAuthor!.Id, ParticipationKind.Record, false, default);

        // assert — now the empty call closes
        (await backend.GetState(chatId, default)).Should().BeNull();
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
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, default);
        await backend.OnStreamRegistered(chatId, AuthorId.New(chatId, 777_011), null, true, default);

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
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, default);
        await backend.OnStreamRegistered(chatId, AuthorId.New(chatId, 777_012), null, true, default);

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
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, default);
        await backend.OnStreamRegistered(chatId, AuthorId.New(chatId, 777_013), null, true, default);

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
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, default);

        // assert
        var live = await backend.GetState(chatId, default);
        live.Should().NotBeNull();
        live!.SessionStartedAt.Should().BeNull();

        // act — a second distinct peer starts streaming
        var peer2 = AuthorId.New(chatId, 777_001);
        await backend.OnStreamRegistered(chatId, peer2, null, true, default);

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
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, default);

        // assert
        (await backend.GetState(chatId, default)).Should().NotBeNull();
        (await backend.Get(chatId, default)).Should().BeNull();

        // act — 2nd peer streams
        var peer2 = AuthorId.New(chatId, 777_002);
        await backend.OnStreamRegistered(chatId, peer2, null, true, default);

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
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, default);
        await backend.OnStreamRegistered(chatId, AuthorId.New(chatId, 777_020), null, true, default);

        // assert — latched, and the recording participants keep it fully live (no closing) through a gap
        var live = await backend.GetState(chatId, default);
        var latchedAt = live!.SessionStartedAt;
        latchedAt.Should().NotBeNull();
        live.IsClosing.Should().BeFalse();
        (await backend.Get(chatId, default)).Should().NotBeNull();

        // act — a fresh utterance after the gap
        await backend.OnStreamRegistered(chatId, author.Id, null, true, default);

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
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, default);
        var live = await backend.GetState(chatId, default);
        live.Should().NotBeNull();
        var tileStart = Constants.Chat.ServerIdTileStack.LastLayer.GetTile(live!.StartEntryLid).Range.Start;

        // assert — no live conversation block is injected for a solo streamer
        var metaBefore = await conversations.GetRangeMeta(chatId, tileStart, default);
        metaBefore.ConversationLidRanges.Should().NotContain(r => r.Contains(live.StartEntryLid));

        // act — a second distinct peer latches the session
        await backend.OnStreamRegistered(chatId, AuthorId.New(chatId, 777_030), null, true, default);

        // assert — the live block now appears in the range meta
        var metaAfter = await conversations.GetRangeMeta(chatId, tileStart, default);
        metaAfter.ConversationLidRanges.Should().Contain(r => r.Contains(live.StartEntryLid));
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
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, default);
        var live = await backend.GetState(chatId, default);
        var tileRange = Constants.Chat.ServerIdTileStack.LastLayer.GetTile(live!.StartEntryLid).Range;

        // assert — the synthetic live block is not injected before the latch
        var tileBefore = await conversations.GetTile(chatId, tileRange, default);
        tileBefore.Should().NotContain(c => c.Id == live.ConversationId);

        // act — a second distinct peer latches the session
        await backend.OnStreamRegistered(chatId, AuthorId.New(chatId, 777_031), null, true, default);

        // assert — the live block is now present
        var tileAfter = await conversations.GetTile(chatId, tileRange, default);
        tileAfter.Should().Contain(c => c.Id == live.ConversationId);
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
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, default);
        await backend.OnStreamRegistered(chatId, AuthorId.New(chatId, 777_040), null, true, default);

        // assert — with the recording participation present, the silent recorder is still mic-on and not Exited
        var liveSession = await backend.Get(chatId, default);
        liveSession.Should().NotBeNull();
        var me = liveSession!.Members.Single(m => m.AuthorId == author.Id);
        me.IsMicOpen.Should().BeTrue();
        me.Group.Should().NotBe(MemberGroup.Exited);
    }

    [Fact]
    public async Task ListenerKeepsSessionAliveUntilEmpty()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, default);

        // act — the participant stops recording but keeps listening: still present, stays alive
        await backend.SetParticipation(chatId, author!.Id, ParticipationKind.AudioListen, true, default);

        // assert — a listener keeps the session alive
        (await backend.GetState(chatId, default))!.IsClosing.Should().BeFalse();

        // act — the listener leaves entirely: nobody is even listening now
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
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, default);
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
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, default);
        await ComputedTest.When(async ct => (await backend.HasRecorder(chatId, ct)).Should().BeTrue());

        // the user stops recording but keeps listening
        await backend.SetParticipation(chatId, author!.Id, ParticipationKind.AudioListen, true, default);
        await ComputedTest.When(async ct => (await backend.HasRecorder(chatId, ct)).Should().BeFalse());

        // act — a trailing utterance arrives after the switch; it must NOT flip the listener back to a recorder
        await backend.OnStreamRegistered(chatId, author.Id, null, true, default);

        // assert
        await ComputedTest.When(async ct => (await backend.HasRecorder(chatId, ct)).Should().BeFalse());
    }
}
