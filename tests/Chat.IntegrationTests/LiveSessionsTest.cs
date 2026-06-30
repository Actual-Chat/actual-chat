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
        var account = await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        author.Should().NotBeNull();
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();

        // act — a streamer registers (auto-joins the registry as a recorder)
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, default);

        // assert
        var live = await backend.Get(chatId, default);
        live.Should().NotBeNull();
        live!.TranscriptionOn.Should().BeTrue();
        live.AuthorIds.Should().Contain(author.Id);
        live.IsClosing.Should().BeFalse();

        // act — the voice stream ends (VAD silence) but the mic stays on (still recording)
        await backend.OnStreamsChanged(chatId, default);

        // assert — recording keeps the session live through the speech gap
        (await backend.Get(chatId, default))!.IsClosing.Should().BeFalse();

        // act — recording stops (mic off)
        await backend.SetParticipation(chatId, account.Id, ParticipationKind.Record, false, default);

        // assert — now it winds down (enters the close grace)
        live = await backend.Get(chatId, default);
        live.Should().NotBeNull();
        live!.IsClosing.Should().BeTrue();

        // act
        await backend.Close(chatId, default);

        // assert
        (await backend.Get(chatId, default)).Should().BeNull();
    }

    [Fact]
    public async Task PhoneModeStaysLiveWhileRecordingThenCloses()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();

        // act
        await backend.OnStreamRegistered(chatId, author!.Id, null, false, default);

        // assert
        (await backend.Get(chatId, default)).Should().NotBeNull();

        // act — VAD gap: the voice stream ends, mic still on → recording keeps the call live
        await backend.OnStreamsChanged(chatId, default);

        // assert — not closing (a silence between utterances doesn't flap the call)
        (await backend.Get(chatId, default))!.IsClosing.Should().BeFalse();

        // act — recording stops → the close grace begins
        await backend.SetParticipation(chatId, account.Id, ParticipationKind.Record, false, default);

        // assert — present, marked closing, finalization deferred to the grace timeout
        var live = await backend.Get(chatId, default);
        live.Should().NotBeNull();
        live!.IsClosing.Should().BeTrue();
        live.ClosingAt.Should().NotBeNull();

        // act — explicit close removes it (stands in for the post-grace SelfClose)
        await backend.Close(chatId, default);

        // assert
        (await backend.Get(chatId, default)).Should().BeNull();
    }

    [Fact]
    public async Task ParticipationIsTracked()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.OnStreamRegistered(chatId, author!.Id, null, false, default);
        var userId = account.Id;

        // act + assert — a streamer is auto-registered as a participant (recorders join the registry)
        await ComputedTest.When(async ct => (await backend.ListParticipants(chatId, ct)).Contains(userId).Should().BeTrue());

        // an explicit leave removes them
        await backend.SetParticipation(chatId, userId, ParticipationKind.AudioListen, false, default);
        await ComputedTest.When(async ct => (await backend.ListParticipants(chatId, ct)).Contains(userId).Should().BeFalse());

        // and they can re-join
        await backend.SetParticipation(chatId, userId, ParticipationKind.AudioListen, true, default);
        await ComputedTest.When(async ct => (await backend.ListParticipants(chatId, ct)).Contains(userId).Should().BeTrue());
    }

    [Fact]
    public async Task ClosingTransitionStampsClosingAt()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, default);

        // act — recording stops with no streams → transitions to closing and stamps ClosingAt
        await backend.SetParticipation(chatId, account.Id, ParticipationKind.Record, false, default);

        // assert — ClosingAt drives the self-heal timeout that vanishes a flow-less conversation
        var live = await backend.Get(chatId, default);
        live.Should().NotBeNull();
        live!.IsClosing.Should().BeTrue();
        live.ClosingAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RejoinClearsClosingState()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, default);
        await backend.SetParticipation(chatId, account.Id, ParticipationKind.Record, false, default);
        (await backend.Get(chatId, default))!.IsClosing.Should().BeTrue();

        // act — recording resumes before finalization
        await backend.OnStreamRegistered(chatId, author.Id, null, true, default);

        // assert — re-open clears both the closing flag and the timeout stamp
        var live = await backend.Get(chatId, default);
        live.Should().NotBeNull();
        live!.IsClosing.Should().BeFalse();
        live.ClosingAt.Should().BeNull();
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

        // act — need 2 peers for GetLiveSession to return non-null
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, default);
        await backend.OnStreamRegistered(chatId, AuthorId.New(chatId, 777_011), null, true, default);

        // assert — the session projects the host + the auto-registered streamer as a member
        var liveSession = await backend.GetLiveSession(chatId, default);
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
        var liveSession = await backend.GetLiveSession(chatId, default);
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
        var liveSession = await backend.GetLiveSession(chatId, default);
        var member = liveSession!.Members.Single(m => m.AuthorId == author.Id);
        member.MicMuted.Should().BeTrue();
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
        var live = await backend.Get(chatId, default);
        live.Should().NotBeNull();
        live!.SessionStartedAt.Should().BeNull();

        // act — a second distinct peer starts streaming
        var peer2 = AuthorId.New(chatId, 777_001);
        await backend.OnStreamRegistered(chatId, peer2, null, true, default);

        // assert — the session latches
        live = await backend.Get(chatId, default);
        live!.AuthorIds.Should().HaveCount(2);
        live.SessionStartedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task GetLiveSessionNullUntilSecondStreamer()
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
        (await backend.Get(chatId, default)).Should().NotBeNull();
        (await backend.GetLiveSession(chatId, default)).Should().BeNull();

        // act — 2nd peer streams
        var peer2 = AuthorId.New(chatId, 777_002);
        await backend.OnStreamRegistered(chatId, peer2, null, true, default);

        // assert — the session is now exposed, started at the latch moment
        var liveSession = await backend.GetLiveSession(chatId, default);
        liveSession.Should().NotBeNull();
        liveSession!.StartedAt.Should().NotBe(default);
    }

    [Fact]
    public async Task SessionPersistsAcrossVadGap()
    {
        // arrange — two peers stream, so a session latches
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, default);
        await backend.OnStreamRegistered(chatId, AuthorId.New(chatId, 777_020), null, true, default);
        var latchedAt = (await backend.Get(chatId, default))!.SessionStartedAt;
        latchedAt.Should().NotBeNull();

        // act — VAD silence: all voice streams end, but the peers keep their mics on (recording)
        await backend.OnStreamsChanged(chatId, default);

        // assert — recording keeps the session fully live (no closing) through the gap
        var afterGap = await backend.Get(chatId, default);
        afterGap!.IsClosing.Should().BeFalse();           // recording holds it open
        afterGap.SessionStartedAt.Should().Be(latchedAt); // latch unchanged during the gap
        (await backend.GetLiveSession(chatId, default)).Should().NotBeNull(); // session still exposed

        // a fresh utterance changes nothing — still the same live session
        await backend.OnStreamRegistered(chatId, author.Id, null, true, default);
        var live = await backend.Get(chatId, default);
        live!.IsClosing.Should().BeFalse();
        live.SessionStartedAt.Should().Be(latchedAt);
        (await backend.GetLiveSession(chatId, default)).Should().NotBeNull();
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
        var live = await backend.Get(chatId, default);
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
        var live = await backend.Get(chatId, default);
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
        // arrange — two peers latch a session
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, default);
        await backend.OnStreamRegistered(chatId, AuthorId.New(chatId, 777_040), null, true, default);

        // act — voice streams end (silence) but the recording participation stays
        await backend.OnStreamsChanged(chatId, default);

        // assert — the silent recorder is still mic-on and not Exited
        var liveSession = await backend.GetLiveSession(chatId, default);
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
        var account = await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, default);

        // act — the participant stops recording but keeps listening: still present, stays alive
        await backend.SetParticipation(chatId, account.Id, ParticipationKind.AudioListen, true, default);

        // assert — a listener keeps the session alive
        (await backend.Get(chatId, default))!.IsClosing.Should().BeFalse();

        // act — the listener leaves entirely: nobody is even listening now
        await backend.SetParticipation(chatId, account.Id, ParticipationKind.AudioListen, false, default);

        // assert — the session winds down
        var live = await backend.Get(chatId, default);
        live.Should().NotBeNull();
        live!.IsClosing.Should().BeTrue();
    }

    [Fact]
    public async Task HasRecorderReflectsRegistry()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();

        // act + assert — a streamer is a recorder
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, default);
        await ComputedTest.When(async ct => (await backend.HasRecorder(chatId, ct)).Should().BeTrue());

        // recording stops → no recorder
        await backend.SetParticipation(chatId, account.Id, ParticipationKind.Record, false, default);
        await ComputedTest.When(async ct => (await backend.HasRecorder(chatId, ct)).Should().BeFalse());
    }

    [Fact]
    public async Task TrailingUtteranceDoesNotResurrectRecorder()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();

        // recording starts (auto-registers as a recorder)
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, default);
        await ComputedTest.When(async ct => (await backend.HasRecorder(chatId, ct)).Should().BeTrue());

        // the user stops recording but keeps listening
        await backend.SetParticipation(chatId, account.Id, ParticipationKind.AudioListen, true, default);
        await ComputedTest.When(async ct => (await backend.HasRecorder(chatId, ct)).Should().BeFalse());

        // act — a trailing utterance arrives after the switch; it must NOT flip the listener back to a recorder
        await backend.OnStreamRegistered(chatId, author.Id, null, true, default);

        // assert
        await ComputedTest.When(async ct => (await backend.HasRecorder(chatId, ct)).Should().BeFalse());
    }
}
