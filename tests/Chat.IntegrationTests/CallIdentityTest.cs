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
        var first = await backend.GetState(chatId, default);
        await backend.CancelCall(chatId, bobAuthor.Id, default);
        await backend.StartCall(chatId, bobAuthor.Id, invitees, false, default);
        var second = await backend.GetState(chatId, default);

        // assert
        first!.CallId.Should().NotBeNull();
        first.CallId!.ChatId.Should().Be(chatId);
        second!.CallId.Should().NotBeNull();
        second.CallId.Should().NotBe(first.CallId, "no chat entry separates two unanswered group calls");
        var live = await backend.Get(chatId, default);
        live!.CallId.Should().Be(second.CallId);
        live.Invites.Should().ContainSingle(i => i.CallId == second.CallId);
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
        var state = await backend.GetState(chatId, default);
        await TestWait.When(async ct => {
            var ring = await liveSessions.GetMyCall(alice.Session, "alice-phone", ct);
            ring.Should().NotBeNull();
            ring!.CallId.Should().Be(state!.CallId);
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
        await backend.StartCall(chatId, bobAuthor.Id, invitees, false, default);
        var firstCallId = (await backend.GetState(chatId, default))!.CallId;
        await backend.CancelCall(chatId, bobAuthor.Id, default);
        await backend.StartCall(chatId, bobAuthor.Id, invitees, false, default);
        var secondCallId = (await backend.GetState(chatId, default))!.CallId;

        // act
        await backend.DeclineCall(chatId, aliceAuthor.Id, firstCallId, default);

        // assert
        var live = await backend.Get(chatId, default);
        live!.Invites.Should().ContainSingle(
            i => i.InviteeId == aliceAuthor.Id && i.Status == CallInviteStatus.Ringing,
            "the decline was for a call that is over");

        // act
        await backend.DeclineCall(chatId, aliceAuthor.Id, secondCallId, default);

        // assert
        await TestWait.When(async ct => {
            var state = await backend.GetState(chatId, ct);
            state.Should().BeNull("a declined two-party call is over");
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
        await backend.StartCall(chatId, bobAuthor.Id, invitees, false, default);
        var firstCallId = (await backend.GetState(chatId, default))!.CallId;
        await backend.CancelCall(chatId, bobAuthor.Id, default);
        await backend.StartCall(chatId, bobAuthor.Id, invitees, false, default);

        // act
        var accept = () => backend.AcceptCall(
            chatId, aliceAuthor.Id, alice.Session.Hash, "alice-phone", firstCallId, default);

        // assert
        await accept.Should().ThrowAsync<InvalidOperationException>();
        var state = await backend.GetState(chatId, default);
        state!.IsDialing.Should().BeTrue("the ring that is going was not the one answered");
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
        var live = await backend.Get(chatId, default);
        live!.Invites.Should().ContainSingle(
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
        var state = await backend.GetState(chatId, default);
        state!.CallId.Should().Be(callId, "the call that was dialing keeps the chat");
        state.CallerId.Should().Be(bobAuthor.Id);
        var callsBackend = bob.AppServices.GetRequiredService<ICallsBackend>();
        var carolAccount = await carol.GetOwnAccount();
        (await callsBackend.GetUserCall(carolAccount.Id, default))
            .Should().BeNull("the refused call's claims are released");
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
        await backend.StartCall(chatId, bobAuthor.Id, invitees, false, default);
        var firstCallId = (await backend.GetState(chatId, default))!.CallId;
        await backend.CancelCall(chatId, bobAuthor.Id, default);
        await backend.StartCall(chatId, bobAuthor.Id, invitees, false, default);

        // act
        await backend.CancelCall(chatId, bobAuthor.Id, firstCallId, default);

        // assert
        var state = await backend.GetState(chatId, default);
        state.Should().NotBeNull();
        state!.IsDialing.Should().BeTrue();
        state.Outcome.Should().Be(CallOutcome.None);
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
        await backend.StartCall(chatId, bobAuthor.Id, invitees, false, default);
        await backend.CancelCall(chatId, bobAuthor.Id, default);
        var left = await backend.GetState(chatId, default);
        left.Should().NotBeNull("the recorder holds the session");
        left!.Outcome.Should().Be(CallOutcome.Canceled);

        // act
        await backend.StartCall(chatId, bobAuthor.Id, invitees, false, default);

        // assert
        var state = await backend.GetState(chatId, default);
        state!.CallId.Should().NotBe(left.CallId);
        state.Outcome.Should().Be(CallOutcome.None, "the outcome is first-writer-wins, and this call has none yet");
    }

    [Fact]
    public async Task ReleaseOfAnEarlierCallShouldNotFreeTheUserOfTheNextOne()
    {
        // arrange
        await using var bob = AppHost.NewBlazorTester(Out);
        var bobAccount = await bob.SignInAsUniqueBob();
        var (chatId, _) = await bob.CreateChat(false);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var callsBackend = bob.AppServices.GetRequiredService<ICallsBackend>();
        var firstCallId = CallId.New(chatId, "1");
        var secondCallId = CallId.New(chatId, "2");
        var claim = new UserCall {
            ChatId = chatId,
            AuthorId = bobAuthor!.Id,
            Role = CallRole.Caller,
            Phase = CallPhase.Dialing,
            CallId = secondCallId,
        };
        (await callsBackend.TryClaim(bobAccount.Id, claim, default)).Should().BeTrue();

        // act
        await callsBackend.ReleaseCall(bobAccount.Id, firstCallId, default);

        // assert - a fresh claim backs itself, so it is still readable
        var held = await callsBackend.GetUserCall(bobAccount.Id, default);
        held.Should().NotBeNull();
        held!.CallId.Should().Be(secondCallId);

        // act
        await callsBackend.ReleaseCall(bobAccount.Id, secondCallId, default);

        // assert
        (await callsBackend.GetUserCall(bobAccount.Id, default)).Should().BeNull();
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
