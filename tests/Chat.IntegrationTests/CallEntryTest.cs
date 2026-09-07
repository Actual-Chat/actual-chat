using ActualChat.Live;
using ActualChat.Streaming;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public sealed class CallEntryTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task CanceledRingShouldWriteOneCanceledEntry()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var (chatId, bob, alice) = await NewPeerChat(tester);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();

        // act
        await backend.StartCall(chatId, bob.Id, new[] { alice.Id }.ToApiArray(), false, default);
        await backend.CancelCall(chatId, bob.Id, default);

        // assert
        var caller = await tester.GetAuthor(bob.Id);
        var entries = await ReadCallEntries(tester, chatId);
        entries.Should().ContainSingle();
        entries[0].Outcome.Should().Be(CallOutcome.Canceled);
        entries[0].CallerId.Should().Be(bob.Id);
        entries[0].CallerName.Should().Be(caller!.Avatar.Name);
        entries[0].InviteeIds.Should().Equal(alice.Id);
        entries[0].HasVideo.Should().BeFalse();
    }

    [Fact]
    public async Task DeclinedThenCanceledCallShouldStayDeclined()
    {
        // The outcome is first-writer-wins: the decline is the call's story, and the caller's
        // hang-up right after it must not rewrite that into Canceled.

        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var (chatId, bob, alice) = await NewPeerChat(tester);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();

        // act
        await backend.StartCall(chatId, bob.Id, new[] { alice.Id }.ToApiArray(), false, default);
        await backend.DeclineCall(chatId, alice.Id, default);
        await backend.CancelCall(chatId, bob.Id, default);

        // assert
        var entries = await ReadCallEntries(tester, chatId);
        entries.Should().ContainSingle();
        entries[0].Outcome.Should().Be(CallOutcome.Declined);
    }

    [Fact]
    public async Task AnsweredCallShouldWriteEndedAndMaterializeACallConversation()
    {
        // The case the whole Ended outcome exists for: transcription is off, so nothing else would
        // remain in the chat once the session closes. The live state is read while the call is still
        // connected because it names the conversation and the close drops it.

        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var (chatId, bob, alice) = await NewPeerChat(tester);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        var conversations = tester.AppServices.GetRequiredService<IConversationsBackend>();

        // act
        await backend.StartCall(chatId, bob.Id, new[] { alice.Id }.ToApiArray(), true, default);
        await backend.AcceptCall(chatId, alice.Id, default);
        var connected = await backend.GetState(chatId, default);
        connected.Should().NotBeNull();
        await backend.LeaveCall(chatId, alice.Id, default);

        // assert
        var entries = await ReadCallEntries(tester, chatId);
        entries.Should().ContainSingle();
        entries[0].Outcome.Should().Be(CallOutcome.Ended);
        entries[0].CallerId.Should().Be(bob.Id);
        entries[0].HasVideo.Should().BeTrue();

        var conversation = await conversations.Get(connected!.ToMaterializedConversation().Id, default);
        conversation.Should().NotBeNull();
        conversation!.IsCall.Should().BeTrue();
    }

    [Fact]
    public async Task CallerHangingUpAnAnsweredCallShouldBeEndedNotCanceled()
    {
        // CancelCall is also the caller's hang-up, so a connected call reaches the close with
        // Canceled recorded on it. The split is decided by SessionStartedAt, not by that outcome.

        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var (chatId, bob, alice) = await NewPeerChat(tester);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();

        // act
        await backend.StartCall(chatId, bob.Id, new[] { alice.Id }.ToApiArray(), false, default);
        await backend.AcceptCall(chatId, alice.Id, default);
        await backend.CancelCall(chatId, bob.Id, default);
        (await backend.GetState(chatId, default))!.Outcome.Should().Be(CallOutcome.Canceled);
        await backend.LeaveCall(chatId, alice.Id, default);

        // assert
        var entries = await ReadCallEntries(tester, chatId);
        entries.Should().ContainSingle();
        entries[0].Outcome.Should().Be(CallOutcome.Ended);
    }

    [Fact]
    public async Task GroupCallShouldWriteNoEntry()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.GetOwnAuthor(chatId);
        author.Should().NotBeNull();
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();

        // act
        await backend.StartCall(chatId, author!.Id, ApiArray<AuthorId>.Empty, false, default);
        await backend.CancelCall(chatId, author.Id, default);

        // assert
        (await ReadCallEntries(tester, chatId)).Should().BeEmpty();
    }

    // Private methods

    private static async Task<(ChatId ChatId, AuthorFull Bob, AuthorFull Alice)> NewPeerChat(IWebTester tester)
    {
        var bob = await tester.SignInAsUniqueBob();
        var alice = await tester.SignInAsUniqueAlice();
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
