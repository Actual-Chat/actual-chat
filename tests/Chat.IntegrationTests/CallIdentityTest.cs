using ActualChat.Live;
using ActualChat.Streaming;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public sealed class CallIdentityTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task NextCallToTheSameChatShouldGetItsOwnId()
    {
        // arrange
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        var (chatId, bobAuthor, aliceAuthor) = await NewGroupChat(bob, alice);
        var backend = bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        var invitees = new[] { aliceAuthor.Id }.ToApiArray();

        // act
        await backend.StartCall(chatId, bobAuthor.Id, invitees, false, default);
        var first = await backend.GetCall(chatId, default);
        await backend.CancelCall(chatId, bobAuthor.Id, default);
        await backend.StartCall(chatId, bobAuthor.Id, invitees, false, default);
        var second = await backend.GetCall(chatId, default);

        // assert
        first.Should().NotBeNull();
        first!.Id.ChatId.Should().Be(chatId);
        second.Should().NotBeNull();
        second!.Id.Should().NotBe(first.Id, "no chat entry separates two unanswered group calls");
        (await backend.ListInvites(chatId, default)).Should().ContainSingle(i => i.CallId == second.Id);
    }

    [Fact]
    public async Task RingShouldNameItsCallToTheCallee()
    {
        // arrange
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        var (chatId, bobAuthor, aliceAuthor) = await NewGroupChat(bob, alice);
        var backend = bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        var liveSessions = alice.AppServices.GetRequiredService<ILiveSessions>();

        // act
        await backend.StartCall(chatId, bobAuthor.Id, new[] { aliceAuthor.Id }.ToApiArray(), false, default);

        // assert
        var call = await backend.GetCall(chatId, default);
        await TestWait.When(async ct => {
            var ring = await liveSessions.GetMyCall(alice.Session, "alice-phone", ct);
            ring.Should().NotBeNull();
            ring!.CallId.Should().Be(call!.Id);
        });
    }

    [Fact]
    public async Task DeclineNamingAnEarlierCallShouldLeaveTheNextOneRinging()
    {
        // arrange
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        var (chatId, bobAuthor, aliceAuthor) = await NewGroupChat(bob, alice);
        var backend = bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        var invitees = new[] { aliceAuthor.Id }.ToApiArray();
        var firstCallId = await backend.StartCall(chatId, bobAuthor.Id, invitees, false, default);
        await backend.CancelCall(chatId, bobAuthor.Id, default);
        var secondCallId = await backend.StartCall(chatId, bobAuthor.Id, invitees, false, default);

        // act
        await backend.DeclineCall(chatId, aliceAuthor.Id, firstCallId, default);

        // assert
        (await backend.ListInvites(chatId, default)).Should().ContainSingle(
            i => i.InviteeId == aliceAuthor.Id && i.Status == CallInviteStatus.Ringing,
            "the decline was for a call that is over");

        // act
        await backend.DeclineCall(chatId, aliceAuthor.Id, secondCallId, default);

        // assert
        await TestWait.When(async ct => {
            var call = await backend.GetCall(chatId, ct);
            call.Should().BeNull("a declined two-party call is over");
        });
    }

    [Fact]
    public async Task AcceptNamingAnEarlierCallShouldBeRefused()
    {
        // arrange
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        var (chatId, bobAuthor, aliceAuthor) = await NewGroupChat(bob, alice);
        var backend = bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        var invitees = new[] { aliceAuthor.Id }.ToApiArray();
        var firstCallId = await backend.StartCall(chatId, bobAuthor.Id, invitees, false, default);
        await backend.CancelCall(chatId, bobAuthor.Id, default);
        await backend.StartCall(chatId, bobAuthor.Id, invitees, false, default);

        // act
        var accept = () => backend.AcceptCall(
            chatId, aliceAuthor.Id, alice.Session.Hash, "alice-phone", firstCallId, default);

        // assert
        await accept.Should().ThrowAsync<InvalidOperationException>();
        var call = await backend.GetCall(chatId, default);
        call!.IsAnswered.Should().BeFalse("the ring that is going was not the one answered");
    }

    [Fact]
    public async Task StartCallRepeatedByTheDialingCallerShouldKeepTheCall()
    {
        // arrange - an RPC resent after a reconnect places the same call again
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        var (chatId, bobAuthor, aliceAuthor) = await NewGroupChat(bob, alice);
        var backend = bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        var invitees = new[] { aliceAuthor.Id }.ToApiArray();
        var callId = await backend.StartCall(chatId, bobAuthor.Id, invitees, false, default);

        // act
        var repeatedCallId = await backend.StartCall(chatId, bobAuthor.Id, invitees, false, default);

        // assert
        repeatedCallId.Should().Be(callId);
        (await backend.ListInvites(chatId, default)).Should().ContainSingle(
            i => i.InviteeId == aliceAuthor.Id && i.Status == CallInviteStatus.Ringing);
    }

    [Fact]
    public async Task StartCallIntoAnotherOnesDialShouldBeRefused()
    {
        // arrange
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await using var carol = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        await carol.SignInAsUniqueBob();
        var (chatId, inviteId) = await bob.CreateChat(false);
        var aliceAuthor = await alice.JoinChat(chatId, inviteId);
        var carolAuthor = await carol.JoinChat(chatId, inviteId);
        var bobAuthor = (await bob.GetOwnAuthor(chatId))!;
        var backend = bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        var callId = await backend.StartCall(
            chatId, bobAuthor.Id, new[] { aliceAuthor.Id }.ToApiArray(), false, default);

        // act
        var start = () => backend.StartCall(
            chatId, carolAuthor.Id, new[] { aliceAuthor.Id, bobAuthor.Id }.ToApiArray(), false, default);

        // assert
        await start.Should().ThrowAsync<InvalidOperationException>();
        var call = await backend.GetCall(chatId, default);
        call!.Id.Should().Be(callId, "the call that was dialing keeps the chat");
        call.CallerId.Should().Be(bobAuthor.Id);
        var callsBackend = bob.AppServices.GetRequiredService<ICallsBackend>();
        var carolAccount = await carol.GetOwnAccount();
        (await callsBackend.GetUserCall(carolAccount.Id, default))!.Phase
            .Should().Be(CallPhase.Ended, "the refused call's claims are ended");
    }

    [Fact]
    public async Task CancelNamingAnEarlierCallShouldLeaveTheNextOneDialing()
    {
        // arrange
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        var (chatId, bobAuthor, aliceAuthor) = await NewGroupChat(bob, alice);
        var backend = bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        var invitees = new[] { aliceAuthor.Id }.ToApiArray();
        var firstCallId = await backend.StartCall(chatId, bobAuthor.Id, invitees, false, default);
        await backend.CancelCall(chatId, bobAuthor.Id, default);
        await backend.StartCall(chatId, bobAuthor.Id, invitees, false, default);

        // act
        await backend.CancelCall(chatId, bobAuthor.Id, firstCallId, default);

        // assert
        var call = await backend.GetCall(chatId, default);
        call.Should().NotBeNull();
        call!.IsAnswered.Should().BeFalse();
        call.Outcome.Should().Be(CallOutcome.None);
    }

    [Fact]
    public async Task CancelByAnInviteeShouldLeaveTheCallRinging()
    {
        // arrange
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        var (chatId, bobAuthor, aliceAuthor) = await NewGroupChat(bob, alice);
        var backend = bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        var invitees = new[] { aliceAuthor.Id }.ToApiArray();
        var callId = await backend.StartCall(chatId, bobAuthor.Id, invitees, false, default);

        // act
        await backend.CancelCall(chatId, aliceAuthor.Id, callId, default);

        // assert
        var call = await backend.GetCall(chatId, default);
        call.Should().NotBeNull();
        call!.IsAnswered.Should().BeFalse();
        call.Outcome.Should().Be(CallOutcome.None);
        (await backend.ListInvites(chatId, default)).Single(x => x.InviteeId == aliceAuthor.Id)
            .Status.Should().Be(CallInviteStatus.Ringing);
    }

    [Fact]
    public async Task NextCallShouldNotInheritTheOutcomeOfACallItsSessionOutlived()
    {
        // arrange - Alice records on her own, which keeps the chat's session open past Bob's cancelled call
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        var (chatId, bobAuthor, aliceAuthor) = await NewGroupChat(bob, alice);
        var backend = bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        var invitees = new[] { aliceAuthor.Id }.ToApiArray();
        await backend.OnStreamRegistered(chatId, aliceAuthor.Id, null, false, true, default);
        var firstCallId = await backend.StartCall(chatId, bobAuthor.Id, invitees, false, default);
        await backend.CancelCall(chatId, bobAuthor.Id, default);
        (await backend.GetCall(chatId, default)).Should().BeNull("a cancelled call is over");
        var left = await backend.GetState(chatId, default);
        left.Should().NotBeNull("the recorder holds the session");
        left!.Kind.Should().Be(LiveSessionKind.Ambient, "the call never touched the session");

        // act
        await backend.StartCall(chatId, bobAuthor.Id, invitees, false, default);

        // assert
        var call = await backend.GetCall(chatId, default);
        call!.Id.Should().NotBe(firstCallId);
        call.Outcome.Should().Be(CallOutcome.None, "the outcome is first-writer-wins, and this call has none yet");
    }

    [Fact]
    public async Task EndOfAnEarlierCallShouldNotFreeTheUserOfTheNextOne()
    {
        // arrange
        await using var bob = AppHost.NewBlazorTester(Out);
        var bobAccount = await bob.SignInAsUniqueBob();
        var (chatId, _) = await bob.CreateChat(false);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var callsBackend = bob.AppServices.GetRequiredService<ICallsBackend>();
        var firstCallId = CallId.New(chatId, "1");
        var secondCallId = CallId.New(chatId, "2");
        var claim = new UserCallClaim {
            ChatId = chatId,
            AuthorId = bobAuthor!.Id,
            Role = CallRole.Caller,
            Phase = CallPhase.Dialing,
            CallId = secondCallId,
        };
        (await callsBackend.TryClaim(bobAccount.Id, claim, default)).Should().BeTrue();

        // act
        await callsBackend.EndCall(bobAccount.Id, firstCallId, CallOutcome.Canceled, default);

        // assert - a fresh claim backs itself, so it is still readable
        var held = await callsBackend.GetUserCall(bobAccount.Id, default);
        held.Should().NotBeNull();
        held!.CallId.Should().Be(secondCallId);
        held.Phase.Should().Be(CallPhase.Dialing);

        // act
        await callsBackend.EndCall(bobAccount.Id, secondCallId, CallOutcome.Canceled, default);

        // assert - the end stays readable, for the client that ran the call to hear of it
        var ended = await callsBackend.GetUserCall(bobAccount.Id, default);
        ended!.CallId.Should().Be(secondCallId);
        ended.Phase.Should().Be(CallPhase.Ended);
        ended.Outcome.Should().Be(CallOutcome.Canceled);
    }

    [Fact]
    public async Task EndedClaimShouldNotKeepTheUserBusy()
    {
        // arrange
        await using var bob = AppHost.NewBlazorTester(Out);
        var bobAccount = await bob.SignInAsUniqueBob();
        var (chatId, _) = await bob.CreateChat(false);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var callsBackend = bob.AppServices.GetRequiredService<ICallsBackend>();
        var firstCallId = CallId.New(chatId, "1");
        var claim = new UserCallClaim {
            ChatId = chatId,
            AuthorId = bobAuthor!.Id,
            Role = CallRole.Caller,
            Phase = CallPhase.Dialing,
            CallId = firstCallId,
        };
        (await callsBackend.TryClaim(bobAccount.Id, claim, default)).Should().BeTrue();
        await callsBackend.EndCall(bobAccount.Id, firstCallId, CallOutcome.NoAnswer, default);

        // act - a claim younger than ClaimGrace backs itself, so only its end can free the user this fast
        var isClaimed = await callsBackend.TryClaim(
            bobAccount.Id, claim with { CallId = CallId.New(chatId, "2") }, default);

        // assert
        isClaimed.Should().BeTrue();
    }

    [Fact]
    public async Task SelfHealShouldNotOverwriteTheOutcomeOfAnEndedClaim()
    {
        // arrange
        await using var bob = AppHost.NewBlazorTester(Out);
        var bobAccount = await bob.SignInAsUniqueBob();
        var (chatId, _) = await bob.CreateChat(false);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var callsBackend = bob.AppServices.GetRequiredService<ICallsBackend>();
        var callId = CallId.New(chatId, "1");
        var claim = new UserCallClaim {
            ChatId = chatId,
            AuthorId = bobAuthor!.Id,
            Role = CallRole.Caller,
            Phase = CallPhase.Dialing,
            CallId = callId,
        };
        (await callsBackend.TryClaim(bobAccount.Id, claim, default)).Should().BeTrue();
        await callsBackend.EndCall(bobAccount.Id, callId, CallOutcome.Declined, default);

        // act - a late end that knows nothing of how the call went
        await callsBackend.EndCall(bobAccount.Id, callId, CallOutcome.None, default);

        // assert
        (await callsBackend.GetUserCall(bobAccount.Id, default))!.Outcome.Should().Be(CallOutcome.Declined);
    }

    // Private methods

    private static async Task<(ChatId ChatId, AuthorFull Bob, AuthorFull Alice)> NewGroupChat(
        IWebTester bob, IWebTester alice)
    {
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        return (chatId, bobAuthor!, aliceAuthor!);
    }
}
