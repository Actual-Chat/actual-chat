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
        state!.HostAuthorId.Should().Be(call.MemberAuthorId);
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
        state!.HostAuthorId.Should().Be(call.OwnerAuthorId);
    }

    [Fact]
    public async Task SetHostInvalidatesBothStateAndGet()
    {
        // arrange - Host lives on the state, so InvalidateGet alone would leave GetState stale
        var call = await ArrangeCall();
        var backend = Backend(Owner);
        var stateComputed = await Computed.Capture(() => backend.GetState(call.ChatId, default));
        var getComputed = await Computed.Capture(() => backend.Get(call.ChatId, default));
        stateComputed.Value!.HostAuthorId.Should().Be(call.OwnerAuthorId);

        // act
        await LiveSessions(Owner).SetHost(Owner.Session, call.ChatId, call.MemberAuthorId, default);

        // assert
        stateComputed = await stateComputed
            .When(x => x!.HostAuthorId == call.MemberAuthorId)
            .WaitAsync(TimeSpan.FromSeconds(10));
        stateComputed.Value!.HostAuthorId.Should().Be(call.MemberAuthorId);
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
    public async Task PlainMemberShouldRaiseAndLowerOwnHand()
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
    public async Task ModeratorShouldLowerAnothersHandButPlainMemberShouldNot()
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
    public async Task NobodyShouldRaiseAnothersHand()
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
    public async Task OnlyAControllerShouldLowerAllHands()
    {
        // arrange
        var call = await ArrangeCall();
        await LiveSessions(Member).SetHandRaised(Member.Session, call.ChatId, call.MemberAuthorId, true, default);
        await LiveSessions(Moderator)
            .SetHandRaised(Moderator.Session, call.ChatId, call.ModeratorAuthorId, true, default);

        // act
        var memberLowersAll = () => LiveSessions(Member).LowerAllHands(Member.Session, call.ChatId, default);
        var ownerLowersAll = () => LiveSessions(Owner).LowerAllHands(Owner.Session, call.ChatId, default);

        // assert
        await memberLowersAll.Should().ThrowAsync<Exception>();
        await ownerLowersAll.Should().NotThrowAsync();
        await WhenHandRaised(call.ChatId, call.MemberAuthorId, false);
        await WhenHandRaised(call.ChatId, call.ModeratorAuthorId, false);
    }

    [Fact]
    public async Task RaisingAndLoweringAHandShouldPostSystemEntries()
    {
        // arrange
        var call = await ArrangeCall();
        var maxLid = await ChatsBackend(Owner).GetMaxLid(call.ChatId, false, default);

        // act - the second raise changes nothing, so it leaves no entry
        await LiveSessions(Member).SetHandRaised(Member.Session, call.ChatId, call.MemberAuthorId, true, default);
        await LiveSessions(Member).SetHandRaised(Member.Session, call.ChatId, call.MemberAuthorId, true, default);
        await LiveSessions(Owner).SetHandRaised(Owner.Session, call.ChatId, call.MemberAuthorId, false, default);

        // assert
        var entries = await WhenSystemEntries<HandRaisedEntry>(call.ChatId, maxLid + 1, 2);
        entries.Select(x => x.IsHandRaised).Should().Equal([true, false],
            "a hand going up and down is announced, each move once");
        entries.Should().OnlyContain(x => x.TargetAuthorId == call.MemberAuthorId,
            "the entry names whose hand moved, not who moved it");
    }

    [Fact]
    public async Task LoweringAllHandsShouldPostASystemEntryPerHand()
    {
        // arrange
        var call = await ArrangeCall();
        await LiveSessions(Member).SetHandRaised(Member.Session, call.ChatId, call.MemberAuthorId, true, default);
        await LiveSessions(Moderator)
            .SetHandRaised(Moderator.Session, call.ChatId, call.ModeratorAuthorId, true, default);
        await WhenHandRaised(call.ChatId, call.MemberAuthorId);
        await WhenHandRaised(call.ChatId, call.ModeratorAuthorId);
        var maxLid = await ChatsBackend(Owner).GetMaxLid(call.ChatId, false, default);

        // act
        await LiveSessions(Owner).LowerAllHands(Owner.Session, call.ChatId, default);
        await LiveSessions(Owner).LowerAllHands(Owner.Session, call.ChatId, default);

        // assert - the second call finds no hand up, so it leaves no entry
        var entries = await WhenSystemEntries<HandRaisedEntry>(call.ChatId, maxLid + 1, 2);
        entries.Should().OnlyContain(x => !x.IsHandRaised);
        entries.Select(x => x.TargetAuthorId).Should()
            .BeEquivalentTo([call.MemberAuthorId, call.ModeratorAuthorId]);
    }

    [Fact]
    public async Task ChangingTranscriptionShouldPostSystemEntries()
    {
        // arrange
        var call = await ArrangeCall();
        var maxLid = await ChatsBackend(Owner).GetMaxLid(call.ChatId, false, default);
        var rules = SessionRules.Default with { VideoAllowed = false };

        // act
        await LiveSessions(Owner).SetRules(Owner.Session, call.ChatId, rules, default);
        await LiveSessions(Owner).SetRules(Owner.Session, call.ChatId, rules.WithText(false), default);
        await LiveSessions(Owner).SetRules(Owner.Session, call.ChatId, rules.WithText(true), default);

        // assert
        var entries = await WhenSystemEntries<TranscriptionChangedEntry>(call.ChatId, maxLid + 1, 2);
        entries.Select(x => x.IsTranscriptionOn).Should().Equal([false, true],
            "only a transcription flip is announced, and each flip once");
        entries.Should().OnlyContain(x => x.TargetAuthorId == call.OwnerAuthorId);
    }

    [Fact]
    public async Task EitherPeerShouldChangeTranscriptionAndNothingElse()
    {
        // arrange - Member is the callee, so not the call's host
        var chatId = await ArrangePeerCall();
        var maxLid = await ChatsBackend(Owner).GetMaxLid(chatId, false, default);

        // act
        await LiveSessions(Member).SetRules(Member.Session, chatId, SessionRules.Default.WithText(false), default);
        var memberTurnsVideoOff = () => LiveSessions(Member)
            .SetRules(Member.Session, chatId, SessionRules.Default with { VideoAllowed = false }, default);

        // assert
        var state = await Backend(Owner).GetState(chatId, default);
        state!.Rules.HasText().Should().BeFalse();
        await memberTurnsVideoOff.Should().ThrowAsync<Exception>();
        var entries = await WhenSystemEntries<TranscriptionChangedEntry>(chatId, maxLid + 1, 1);
        entries[0].IsTranscriptionOn.Should().BeFalse();
    }

    [Fact]
    public async Task SameTranscriptionChangeRacingItselfShouldPostOneSystemEntry()
    {
        // arrange
        var chatId = await ArrangePeerCall();
        var maxLid = await ChatsBackend(Owner).GetMaxLid(chatId, false, default);
        var rules = SessionRules.Default.WithText(false);

        // act - both sides press "off" at once, each more than once
        var requests = new List<Task>();
        for (var i = 0; i < 4; i++) {
            requests.Add(LiveSessions(Owner).SetRules(Owner.Session, chatId, rules, default));
            requests.Add(LiveSessions(Member).SetRules(Member.Session, chatId, rules, default));
        }
        await Task.WhenAll(requests);

        // assert - an entry is written before its request completes, so none is still on its way
        var entries = await WhenSystemEntries<TranscriptionChangedEntry>(chatId, maxLid + 1, 1);
        entries[0].IsTranscriptionOn.Should().BeFalse();
    }

    // Private methods

    private async Task<ChatId> ArrangePeerCall()
    {
        var owner = await Owner.SignInAsUniqueBob();
        var member = await Member.SignInAsUniqueAlice();
        var chatId = (ChatId)PeerChatId.New(owner.Id, member.Id);
        // A reply is what makes the two contacts, and gives each an author in the chat
        await Owner.CreateTextEntry(chatId, "hi");
        await Member.CreateTextEntry(chatId, "hey");
        var ownerAuthor = await Owner.GetOwnAuthor(chatId).Require();
        var memberAuthor = await Member.GetOwnAuthor(chatId).Require();

        var backend = Backend(Owner);
        await backend.StartCall(chatId, ownerAuthor.Id, new ApiArray<AuthorId>([memberAuthor.Id]), false, default);
        await backend.AcceptCall(chatId, memberAuthor.Id, default);
        await backend.SetParticipation(chatId, memberAuthor.Id, ParticipationKind.Record, true, default);
        return chatId;
    }

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
        // Presence comes only from a client's own stream - the invitees' always, and the caller's too, since
        // AcceptCall registers the caller as a placeholder - so all three have to be simulated here.
        await backend.SetParticipation(chatId, ownerAuthor.Id, ParticipationKind.Record, true, default);
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

    private Task<List<TEntry>> WhenSystemEntries<TEntry>(ChatId chatId, long minLid, int expectedCount)
        where TEntry : SystemEntry
    {
        var chatsBackend = ChatsBackend(Owner);
        return TestWait.WhenPolled(async () => {
            var maxLid = await chatsBackend.GetMaxLid(chatId, false, default);
            var found = new List<TEntry>();
            for (var lid = minLid; lid <= maxLid; lid++) {
                var entry = await chatsBackend.GetEntry(ChatEntryId.New(chatId, lid), default);
                if (entry is TEntry match)
                    found.Add(match);
            }
            found.Should().HaveCount(expectedCount);
            return found;
        }, TimeSpan.FromSeconds(30));
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
    private static IChatsBackend ChatsBackend(WebClientTester tester)
        => tester.AppServices.GetRequiredService<IChatsBackend>();
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
