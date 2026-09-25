using ActualChat.Live;
using ActualChat.Streaming;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class CallModerationTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester Owner => field ??= fixture.AppHost.NewWebClientTester(Out);
    private WebClientTester Moderator => field ??= fixture.AppHost.NewWebClientTester(Out);
    private WebClientTester Member => field ??= fixture.AppHost.NewWebClientTester(Out);

    protected override async Task DisposeAsync()
    {
        await Owner.DisposeSilentlyAsync();
        await Moderator.DisposeSilentlyAsync();
        await Member.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task OwnerCanMuteAnyone()
    {
        // arrange
        var call = await ArrangeCall();

        // act
        await LiveSessions(Owner).MutePeer(Owner.Session, call.ChatId, call.MemberAuthorId, true, default);
        await LiveSessions(Owner).MutePeer(Owner.Session, call.ChatId, call.ModeratorAuthorId, true, default);

        // assert
        (await IsMuted(call.ChatId, call.MemberAuthorId)).Should().BeTrue();
        (await IsMuted(call.ChatId, call.ModeratorAuthorId)).Should().BeTrue();
    }

    [Fact]
    public async Task ModeratorCanMutePlainMemberButNotOwner()
    {
        // arrange
        var call = await ArrangeCall();
        await PromoteToModerator(call.ModeratorAuthorId);

        // act
        await LiveSessions(Moderator).MutePeer(Moderator.Session, call.ChatId, call.MemberAuthorId, true, default);
        var muteOwner = () => LiveSessions(Moderator)
            .MutePeer(Moderator.Session, call.ChatId, call.OwnerAuthorId, true, default);

        // assert
        (await IsMuted(call.ChatId, call.MemberAuthorId)).Should().BeTrue();
        await muteOwner.Should().ThrowAsync<Exception>();
        (await IsMuted(call.ChatId, call.OwnerAuthorId, false)).Should().BeFalse();
    }

    [Fact]
    public async Task PlainMemberCannotMuteOthers()
    {
        // arrange
        var call = await ArrangeCall();

        // act
        var muteModerator = () => LiveSessions(Member)
            .MutePeer(Member.Session, call.ChatId, call.ModeratorAuthorId, true, default);

        // assert
        await muteModerator.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task HostCanMuteOwner()
    {
        // arrange - the host runs the call, so their authority outranks Owner immunity
        var call = await ArrangeCall();
        await Backend(Owner).SetHost(call.ChatId, call.MemberAuthorId, default);

        // act
        await LiveSessions(Member).MutePeer(Member.Session, call.ChatId, call.OwnerAuthorId, true, default);

        // assert
        (await IsMuted(call.ChatId, call.OwnerAuthorId)).Should().BeTrue();
    }

    [Fact]
    public async Task ModeratorMuteAllLeavesOwnersUnmuted()
    {
        // arrange
        var call = await ArrangeCall();
        await PromoteToModerator(call.ModeratorAuthorId);

        // act
        await LiveSessions(Moderator).MuteAll(Moderator.Session, call.ChatId, true, default);

        // assert
        (await IsMuted(call.ChatId, call.MemberAuthorId)).Should().BeTrue();
        (await IsMuted(call.ChatId, call.OwnerAuthorId, false)).Should().BeFalse();
        (await IsMuted(call.ChatId, call.ModeratorAuthorId, false)).Should().BeFalse();
    }

    [Fact]
    public async Task OwnerMuteAllMutesEveryoneElse()
    {
        // arrange
        var call = await ArrangeCall();

        // act
        await LiveSessions(Owner).MuteAll(Owner.Session, call.ChatId, true, default);

        // assert
        (await IsMuted(call.ChatId, call.MemberAuthorId)).Should().BeTrue();
        (await IsMuted(call.ChatId, call.ModeratorAuthorId)).Should().BeTrue();
        (await IsMuted(call.ChatId, call.OwnerAuthorId, false)).Should().BeFalse();
    }

    [Fact]
    public async Task ModeratorCanSetRulesButPlainMemberCannot()
    {
        // arrange
        var call = await ArrangeCall();
        await PromoteToModerator(call.ModeratorAuthorId);
        var rules = SessionRules.Default with { VideoAllowed = false };

        // act
        await LiveSessions(Moderator).SetRules(Moderator.Session, call.ChatId, rules, default);
        var memberSetRules = () => LiveSessions(Member).SetRules(Member.Session, call.ChatId, rules, default);

        // assert
        var state = await Backend(Owner).GetState(call.ChatId, default);
        state!.Rules.VideoAllowed.Should().BeFalse();
        await memberSetRules.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task ModeratorCannotReassignHost()
    {
        // arrange - otherwise a Moderator could grant themselves the host's Owner-muting power
        var call = await ArrangeCall();
        await PromoteToModerator(call.ModeratorAuthorId);

        // act
        var setHost = () => LiveSessions(Moderator)
            .SetHost(Moderator.Session, call.ChatId, call.ModeratorAuthorId, default);

        // assert
        await setHost.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task OwnerCanReassignHostAndThereIsOnlyOne()
    {
        // arrange
        var call = await ArrangeCall();

        // act
        await LiveSessions(Owner).SetHost(Owner.Session, call.ChatId, call.MemberAuthorId, default);

        // assert
        var state = await Backend(Owner).GetState(call.ChatId, default);
        state!.Host.Should().Be(call.MemberAuthorId);
    }

    [Fact]
    public async Task SettingNonParticipantAsHostIsRejected()
    {
        // arrange
        var call = await ArrangeCall();
        var outsiderAuthorId = AuthorId.New(call.ChatId, 4242);

        // act
        var setHost = () => LiveSessions(Owner).SetHost(Owner.Session, call.ChatId, outsiderAuthorId, default);

        // assert
        await setHost.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task HostLeavingReassignsHostToAnOwner()
    {
        // arrange
        var call = await ArrangeCall();
        await Backend(Owner).SetHost(call.ChatId, call.MemberAuthorId, default);

        // act
        await Backend(Owner)
            .SetParticipation(call.ChatId, call.MemberAuthorId, ParticipationKind.Record, false, default);

        // assert
        var state = await Backend(Owner).GetState(call.ChatId, default);
        state!.Host.Should().Be(call.OwnerAuthorId);
    }

    [Fact]
    public async Task SetHostInvalidatesBothStateAndGet()
    {
        // arrange - Host lives on the state, so InvalidateGet alone would leave GetState stale
        var call = await ArrangeCall();
        var backend = Backend(Owner);
        var stateComputed = await Computed.Capture(() => backend.GetState(call.ChatId, default));
        var getComputed = await Computed.Capture(() => backend.Get(call.ChatId, default));
        stateComputed.Value!.Host.Should().Be(call.OwnerAuthorId);

        // act
        await LiveSessions(Owner).SetHost(Owner.Session, call.ChatId, call.MemberAuthorId, default);

        // assert
        stateComputed = await stateComputed
            .When(x => x!.Host == call.MemberAuthorId)
            .WaitAsync(TimeSpan.FromSeconds(10));
        stateComputed.Value!.Host.Should().Be(call.MemberAuthorId);
        getComputed = await getComputed
            .When(x => x!.Host == call.MemberAuthorId)
            .WaitAsync(TimeSpan.FromSeconds(10));
        getComputed.Value!.Host.Should().Be(call.MemberAuthorId);
    }

    [Fact]
    public async Task MutePeerInvalidatesLiveSession()
    {
        // arrange
        var call = await ArrangeCall();
        var backend = Backend(Owner);
        var computed = await Computed.Capture(() => backend.Get(call.ChatId, default));
        MicMuted(computed.Value, call.MemberAuthorId).Should().BeFalse();

        // act
        await LiveSessions(Owner).MutePeer(Owner.Session, call.ChatId, call.MemberAuthorId, true, default);

        // assert
        computed = await computed
            .When(x => MicMuted(x, call.MemberAuthorId))
            .WaitAsync(TimeSpan.FromSeconds(10));
        MicMuted(computed.Value, call.MemberAuthorId).Should().BeTrue();
    }

    [Fact]
    public async Task PromotingModeratorInvalidatesMuteAuthority()
    {
        // arrange
        var call = await ArrangeCall();
        var muteMember = () => LiveSessions(Moderator)
            .MutePeer(Moderator.Session, call.ChatId, call.MemberAuthorId, true, default);
        await muteMember.Should().ThrowAsync<Exception>();

        // act
        await PromoteToModerator(call.ModeratorAuthorId);
        await WaitForModerate(Moderator, call.ChatId);

        // assert
        await muteMember.Should().NotThrowAsync();
        (await IsMuted(call.ChatId, call.MemberAuthorId)).Should().BeTrue();
    }

    [Fact]
    public async Task PlainMemberCanRaiseAndLowerOwnHand()
    {
        // arrange
        var call = await ArrangeCall();

        // act
        await LiveSessions(Member).SetHandRaised(Member.Session, call.ChatId, call.MemberAuthorId, true, default);
        await WhenHandRaised(call.ChatId, call.MemberAuthorId);
        await LiveSessions(Member).SetHandRaised(Member.Session, call.ChatId, call.MemberAuthorId, false, default);

        // assert
        await WhenHandRaised(call.ChatId, call.MemberAuthorId, false);
    }

    [Fact]
    public async Task ModeratorCanLowerAnothersHandButPlainMemberCannot()
    {
        // arrange
        var call = await ArrangeCall();
        await PromoteToModerator(call.ModeratorAuthorId);
        await WaitForModerate(Moderator, call.ChatId);
        await LiveSessions(Owner).SetHandRaised(Owner.Session, call.ChatId, call.OwnerAuthorId, true, default);
        await LiveSessions(Member).SetHandRaised(Member.Session, call.ChatId, call.MemberAuthorId, true, default);

        // act
        var memberLowersOwner = () => LiveSessions(Member)
            .SetHandRaised(Member.Session, call.ChatId, call.OwnerAuthorId, false, default);
        await LiveSessions(Moderator)
            .SetHandRaised(Moderator.Session, call.ChatId, call.MemberAuthorId, false, default);

        // assert
        await memberLowersOwner.Should().ThrowAsync<Exception>();
        await WhenHandRaised(call.ChatId, call.OwnerAuthorId);
        await WhenHandRaised(call.ChatId, call.MemberAuthorId, false);
    }

    [Fact]
    public async Task NobodyCanRaiseAnothersHand()
    {
        // arrange
        var call = await ArrangeCall();

        // act
        var raiseMembersHand = () => LiveSessions(Owner)
            .SetHandRaised(Owner.Session, call.ChatId, call.MemberAuthorId, true, default);

        // assert
        await raiseMembersHand.Should().ThrowAsync<Exception>();
        await WhenHandRaised(call.ChatId, call.MemberAuthorId, false);
    }

    [Fact]
    public async Task OnlyAControllerCanLowerAllHands()
    {
        // arrange
        var call = await ArrangeCall();
        await LiveSessions(Member).SetHandRaised(Member.Session, call.ChatId, call.MemberAuthorId, true, default);
        await LiveSessions(Moderator)
            .SetHandRaised(Moderator.Session, call.ChatId, call.ModeratorAuthorId, true, default);

        // act
        var memberLowersAll = () => LiveSessions(Member).LowerAllHands(Member.Session, call.ChatId, default);
        await memberLowersAll.Should().ThrowAsync<Exception>();
        await LiveSessions(Owner).LowerAllHands(Owner.Session, call.ChatId, default);

        // assert
        await WhenHandRaised(call.ChatId, call.MemberAuthorId, false);
        await WhenHandRaised(call.ChatId, call.ModeratorAuthorId, false);
    }

    // Private methods

    private async Task<CallSetup> ArrangeCall()
    {
        await Owner.SignInAsUniqueBob();
        await Moderator.SignInAsUniqueAlice();
        await Member.SignInAsUniqueAlice();

        var (chatId, inviteId) = await Owner.CreateChat(false);
        var ownerAuthor = await Owner.GetOwnAuthor(chatId).Require();
        var moderatorAuthor = await Moderator.JoinChat(chatId, inviteId);
        var memberAuthor = await Member.JoinChat(chatId, inviteId);

        // A real call, not an ambient session: LiveSessionsBackend.Get only surfaces members for a call.
        var backend = Backend(Owner);
        var invitees = new ApiArray<AuthorId>([moderatorAuthor.Id, memberAuthor.Id]);
        await backend.StartCall(chatId, ownerAuthor.Id, invitees, false, default);
        await backend.AcceptCall(chatId, moderatorAuthor.Id, default);
        await backend.AcceptCall(chatId, memberAuthor.Id, default);
        // AcceptCall no longer registers the invitee's own presence - that now comes only from a real
        // stream, so it has to be simulated here for the two to actually count as call participants.
        await backend.SetParticipation(chatId, moderatorAuthor.Id, ParticipationKind.Record, true, default);
        await backend.SetParticipation(chatId, memberAuthor.Id, ParticipationKind.Record, true, default);
        return new CallSetup(chatId, ownerAuthor.Id, moderatorAuthor.Id, memberAuthor.Id);
    }

    private Task PromoteToModerator(AuthorId authorId)
        => Owner.Commander.Call(new Authors_ChangeRole {
            Session = Owner.Session,
            AuthorId = authorId,
            SystemRole = SystemRole.Moderator,
            IsInRole = true,
        });

    private async Task<bool> IsMuted(ChatId chatId, AuthorId authorId, bool expected = true)
    {
        // Get is a compute method, so settle on the expected value rather than reading a stale one.
        var backend = Backend(Owner);
        var computed = await Computed.Capture(() => backend.Get(chatId, default));
        try {
            computed = await computed
                .When(x => MicMuted(x, authorId) == expected)
                .WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch (TimeoutException) { }

        return MicMuted(computed.Value, authorId);
    }

    private Task WhenHandRaised(
        ChatId chatId,
        AuthorId authorId,
        bool expected = true,
        [CallerFilePath] string callerFilePath = "",
        [CallerLineNumber] int callerLine = 0)
    {
        var backend = Backend(Owner);
        return TestWait.When(async ct => {
            var live = await backend.Get(chatId, ct);
            HandRaised(live, authorId).Should().Be(expected);
        }, callerFilePath: callerFilePath, callerLine: callerLine);
    }

    private static async Task WaitForModerate(WebClientTester tester, ChatId chatId)
    {
        var chats = tester.AppServices.GetRequiredService<IChats>();
        var computed = await Computed.Capture(() => chats.GetRules(tester.Session, chatId, default));
        await computed.When(x => x.CanModerate()).WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static ILiveSessions LiveSessions(WebClientTester tester)
        => tester.AppServices.GetRequiredService<ILiveSessions>();
    private static ILiveSessionsBackend Backend(WebClientTester tester)
        => tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
    private static bool MicMuted(LiveSession? live, AuthorId authorId)
        => live?.Members.FirstOrDefault(x => x.AuthorId == authorId)?.MicMuted ?? false;
    private static bool HandRaised(LiveSession? live, AuthorId authorId)
        => live?.Members.FirstOrDefault(x => x.AuthorId == authorId)?.IsHandRaised ?? false;

    // Nested types

    private sealed record CallSetup(
        ChatId ChatId,
        AuthorId OwnerAuthorId,
        AuthorId ModeratorAuthorId,
        AuthorId MemberAuthorId);
}
