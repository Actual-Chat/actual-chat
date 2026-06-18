using ActualChat.Live;
using ActualChat.Streaming;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class LiveSessionsTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task TranscriptionConversationStartsThenMarksClosing()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        author.Should().NotBeNull();
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();

        // act
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, default);

        // assert
        var live = await backend.Get(chatId, default);
        live.Should().NotBeNull();
        live!.TranscriptionOn.Should().BeTrue();
        live.AuthorIds.Should().Contain(author.Id);
        live.IsClosing.Should().BeFalse();

        // act — no live streams remain, so close detection marks it closing (the flow finalizes it)
        await backend.OnStreamsChanged(chatId, default);

        // assert
        live = await backend.Get(chatId, default);
        live.Should().NotBeNull();
        live!.IsClosing.Should().BeTrue();

        // act
        await backend.Close(chatId, default);

        // assert
        (await backend.Get(chatId, default)).Should().BeNull();
    }

    [Fact]
    public async Task PhoneModeRoutesThroughCloseGrace()
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

        // assert
        (await backend.Get(chatId, default)).Should().NotBeNull();

        // act — no streams remain; phone mode now uses the same close grace as transcription
        // (it does NOT vanish immediately, so a VAD gap between utterances doesn't flap the call)
        await backend.OnStreamsChanged(chatId, default);

        // assert — still present, marked closing, finalization deferred to the grace timeout
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

        // act + assert — a streamer is auto-registered as a participant (recorders join the registry)
        (await backend.IsParticipant(chatId, account.Id, default)).Should().BeTrue();

        // an explicit leave removes them
        await backend.SetParticipation(chatId, account.Id, ParticipationKind.AudioListen, false, default);
        (await backend.IsParticipant(chatId, account.Id, default)).Should().BeFalse();

        // and they can re-join
        await backend.SetParticipation(chatId, account.Id, ParticipationKind.AudioListen, true, default);
        (await backend.IsParticipant(chatId, account.Id, default)).Should().BeTrue();
    }

    [Fact]
    public async Task ClosingTransitionStampsClosingAt()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, default);

        // act — no live streams remain, so it transitions to closing and stamps ClosingAt
        await backend.OnStreamsChanged(chatId, default);

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
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>().GetOwn(session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.OnStreamRegistered(chatId, author!.Id, null, true, default);
        await backend.OnStreamsChanged(chatId, default);
        (await backend.Get(chatId, default))!.IsClosing.Should().BeTrue();

        // act — a stream registers again before finalization
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
    public async Task MutePeerSetsForcedMuted()
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
        member.ForcedMuted.Should().BeTrue();
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

        // act — VAD silence: all streams end, then a peer speaks again within the grace window
        await backend.OnStreamsChanged(chatId, default);
        var closing = await backend.Get(chatId, default);
        closing!.IsClosing.Should().BeTrue();           // in the close-grace, not removed
        closing.SessionStartedAt.Should().Be(latchedAt); // latch unchanged during the gap
        (await backend.GetLiveSession(chatId, default)).Should().NotBeNull(); // session still exposed

        await backend.OnStreamRegistered(chatId, author.Id, null, true, default);

        // assert — reactivated; same latch, still a session
        var live = await backend.Get(chatId, default);
        live!.IsClosing.Should().BeFalse();
        live.SessionStartedAt.Should().Be(latchedAt);
        (await backend.GetLiveSession(chatId, default)).Should().NotBeNull();
    }
}
