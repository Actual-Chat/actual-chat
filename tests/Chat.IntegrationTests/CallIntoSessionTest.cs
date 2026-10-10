using ActualChat.Live;
using ActualChat.Streaming;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public sealed class CallIntoSessionTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnansweredCallIntoSessionShouldLeaveItAsItWas(bool isDeclined)
    {
        // arrange - Bob and Alice are already talking in the chat (#5000)
        await using var tester = AppHost.NewBlazorTester(Out);
        var (chatId, bob, alice) = await NewPeerChat(tester);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        var before = await StartTalking(backend, chatId, bob.Id, alice.Id);
        await backend.StartCall(chatId, bob.Id, new[] { alice.Id }.ToApiArray(), false, default);

        // act
        if (isDeclined)
            await backend.DeclineCall(chatId, alice.Id, default);
        else
            await backend.CancelCall(chatId, bob.Id, default);

        // assert
        (await backend.GetCall(chatId, default)).Should().BeNull("the call is over");
        var after = await backend.GetState(chatId, default);
        after.Should().NotBeNull("the session the call rang into goes on");
        after!.Kind.Should().Be(LiveSessionKind.Ambient);
        after.ConversationId.Should().Be(before.ConversationId, "its block is the same one");
        after.IsClosing.Should().BeFalse("both of its speakers are still there");
        var live = await backend.Get(chatId, default);
        live!.Kind.Should().Be(LiveSessionKind.Ambient, "the call no longer overlays the session");
        var entries = await ReadCallEntries(tester, chatId);
        entries.Should().ContainSingle()
            .Which.Outcome.Should().Be(isDeclined ? CallOutcome.Declined : CallOutcome.Canceled);
    }

    [Fact]
    public async Task AnsweredCallIntoSessionShouldKeepTheSessionAndRecordItsTalkTime()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var (chatId, bob, alice) = await NewPeerChat(tester);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        var before = await StartTalking(backend, chatId, bob.Id, alice.Id);
        await backend.StartCall(chatId, bob.Id, new[] { alice.Id }.ToApiArray(), false, default);

        // act
        await backend.AcceptCall(chatId, alice.Id, default);

        // assert - answered, the call joins the session and leaves its block an ordinary one
        (await backend.GetCall(chatId, default))!.IsAnswered.Should().BeTrue();
        var during = await backend.GetState(chatId, default);
        during!.Kind.Should().Be(LiveSessionKind.Ambient);
        during.ConversationId.Should().Be(before.ConversationId);
        var live = await backend.Get(chatId, default);
        live!.Kind.Should().Be(LiveSessionKind.Call);
        live.Conversation!.IsCall.Should().BeFalse("the block is the session's, not the call's");

        // act - Bob hangs up; Alice stays, still recording
        await HangUp(backend, chatId, bob.Id);

        // assert
        (await backend.GetCall(chatId, default)).Should().BeNull("a call with one party left is over");
        var after = await backend.GetState(chatId, default);
        after.Should().NotBeNull("Alice still records, so the session goes on");
        after!.ConversationId.Should().Be(before.ConversationId);
        (await ReadCallEntries(tester, chatId)).Should().BeEmpty("the entry waits for the call's last words");
        // Polled, as the chat's entries aren't read through anything the call invalidates. Nothing streams
        // here, so only the transcriber's own post-audio deadline (5 s) stands between the end and the entry.
        await TestWait.WhenPolled(async () => {
            var entry = (await ReadCallEntries(tester, chatId)).Should().ContainSingle().Which;
            entry.Outcome.Should().Be(CallOutcome.Ended);
            entry.EndsAt.Should().NotBeNull("an answered call's entry spans its talk time");
            entry.EndsAt!.Value.Should().BeGreaterThanOrEqualTo(entry.BeginsAt);
        }, TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task PeerCallSessionShouldEndWithTheCallForBoth()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var (chatId, bob, alice) = await NewPeerChat(tester);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.StartCall(chatId, bob.Id, new[] { alice.Id }.ToApiArray(), false, default);
        await backend.AcceptCall(chatId, alice.Id, default);
        await backend.SetParticipation(chatId, alice.Id, ParticipationKind.Record, true, default);
        var session = (await backend.GetState(chatId, default))!;

        // act - Bob hangs up while Alice is still recording
        await HangUp(backend, chatId, bob.Id);

        // assert - the call is over; the session waits for Alice's client, which stops on the call's end
        (await backend.GetCall(chatId, default)).Should().BeNull();
        (await backend.GetState(chatId, default)).Should().NotBeNull();

        // act - Alice's client stops her recording, as it does on a peer call's end
        await backend.SetParticipation(chatId, alice.Id, ParticipationKind.Record, false, default);

        // assert - nobody records, so the session closes, and its block is kept as the call's card
        (await backend.GetState(chatId, default)).Should().BeNull();
        var conversations = tester.AppServices.GetRequiredService<IConversationsBackend>();
        var conversation = await conversations.Get(session.ConversationId, default);
        conversation!.IsCall.Should().BeTrue();
    }

    [Fact]
    public async Task CallSessionShouldHaveATranscriptOnceAStreamIsTranscribed()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var (chatId, bob, alice) = await NewPeerChat(tester);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.StartCall(chatId, bob.Id, new[] { alice.Id }.ToApiArray(), false, default);
        await backend.AcceptCall(chatId, alice.Id, default);
        (await backend.GetState(chatId, default))!.HasTranscript.Should().BeFalse("nobody has spoken yet");

        // act - the answer started the session; the streams come after it
        await backend.OnStreamRegistered(chatId, alice.Id, null, true, true, default);

        // assert
        var live = await backend.GetState(chatId, default);
        live!.Kind.Should().Be(LiveSessionKind.Call);
        live.HasTranscript.Should().BeTrue("a session the call started is transcribed like any other");
    }

    [Fact]
    public async Task GroupCallSessionShouldOutliveTheCall()
    {
        // arrange - Bob calls Alice in a group chat; Carol, not in the call, records too
        await using var tester = AppHost.NewBlazorTester(Out);
        var bobAccount = await tester.SignInAsUniqueBob();
        var aliceAccount = await tester.SignInAsUniqueAlice();
        var carolAccount = await tester.SignInAsNew("Carol");
        await tester.SignIn(bobAccount);
        var (chatId, _) = await tester.CreateChat(false);
        var authors = tester.AppServices.GetRequiredService<IAuthorsBackend>();
        var bob = (await tester.GetOwnAuthor(chatId))!;
        var alice = await authors.EnsureJoined(chatId, aliceAccount.Id, default);
        var carol = await authors.EnsureJoined(chatId, carolAccount.Id, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        await backend.StartCall(chatId, bob.Id, new[] { alice.Id }.ToApiArray(), false, default);
        await backend.AcceptCall(chatId, alice.Id, default);
        await backend.SetParticipation(chatId, alice.Id, ParticipationKind.Record, true, default);
        await backend.SetParticipation(chatId, carol.Id, ParticipationKind.Record, true, default);
        var session = (await backend.GetState(chatId, default))!;

        // act - Bob hangs up
        await HangUp(backend, chatId, bob.Id);

        // assert - a group call goes on while anyone is in it
        (await backend.GetCall(chatId, default)).Should().NotBeNull("Alice is still on it");

        // act - Alice, the last party, hangs up too
        await backend.SetParticipation(chatId, alice.Id, ParticipationKind.Record, false, default);

        // assert - the call is over, its session goes on as any other would
        (await backend.GetCall(chatId, default)).Should().BeNull();
        var after = await backend.GetState(chatId, default);
        after.Should().NotBeNull("Carol still records");
        after!.Kind.Should().Be(LiveSessionKind.Call, "the session is still the one the call started");

        // act - nobody records any more
        await backend.SetParticipation(chatId, carol.Id, ParticipationKind.Record, false, default);

        // assert - it closes, and its block is kept as the call's card
        (await backend.GetState(chatId, default)).Should().BeNull();
        var conversations = tester.AppServices.GetRequiredService<IConversationsBackend>();
        var conversation = await conversations.Get(session.ConversationId, default);
        conversation.Should().NotBeNull();
        conversation!.IsCall.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task RingShouldTimeOutWithNobodyObservingTheCall()
    {
        // The ring timer, not GetCall's self-heal, has to end it: nothing here reads the call.

        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var (chatId, bob, alice) = await NewPeerChat(tester);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();

        // act
        await backend.StartCall(chatId, bob.Id, new[] { alice.Id }.ToApiArray(), false, default);

        // assert - polled, as the chat's entries aren't read through anything the call invalidates. The
        // budget covers the ring and the late-answer window after it (30 s), with room to spare.
        await TestWait.WhenPolled(async () => {
            var entries = await ReadCallEntries(tester, chatId);
            entries.Should().ContainSingle().Which.Outcome.Should().Be(CallOutcome.NoAnswer);
        }, TimeSpan.FromSeconds(60));
    }

    // Private methods

    private static async Task<LiveSessionState> StartTalking(
        ILiveSessionsBackend backend, ChatId chatId, AuthorId bobId, AuthorId aliceId)
    {
        await backend.OnStreamRegistered(chatId, bobId, null, false, true, default);
        await backend.OnStreamRegistered(chatId, aliceId, null, false, true, default);
        var state = await backend.GetState(chatId, default);
        state!.StartedAt.Should().NotBeNull("two speakers latch the session");
        state.Kind.Should().Be(LiveSessionKind.Ambient);
        return state;
    }

    private static async Task HangUp(ILiveSessionsBackend backend, ChatId chatId, AuthorId authorId)
    {
        // A leave that leaves the call one party only schedules its close CallLeaveGrace later;
        // EnforceCallLeaveGrace is internal so the test runs that check now instead of waiting it out.
        await backend.SetParticipation(chatId, authorId, ParticipationKind.Record, false, default);
        await ((LiveSessionsBackend)backend).EnforceCallLeaveGrace(chatId);
    }

    private static async Task<(ChatId ChatId, AuthorFull Bob, AuthorFull Alice)> NewPeerChat(IWebTester tester)
    {
        var bob = await tester.SignInAsUniqueBob();
        var alice = await tester.SignInAsUniqueAlice();
        await tester.CreatePeerContact(alice, bob);
        await tester.SignIn(bob);
        var chatId = (ChatId)PeerChatId.New(bob.Id, alice.Id);
        var authors = tester.AppServices.GetRequiredService<IAuthorsBackend>();
        return (chatId,
            await authors.EnsureJoined(chatId, bob.Id, default),
            await authors.EnsureJoined(chatId, alice.Id, default));
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
