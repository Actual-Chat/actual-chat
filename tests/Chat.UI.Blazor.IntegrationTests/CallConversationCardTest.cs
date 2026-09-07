using ActualChat.Streaming;
using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection))]
public sealed class CallConversationCardTest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    private BlazorTester Tester => field ??= AppHost.NewBlazorTester(Out);

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task AnEndedCallShouldProduceOneCardNotTwo()
    {
        // Collapsed, the fold already hides the entry for an unrelated reason; expanding puts it back
        // into the per-entry loop, where only the tile-builder skip keeps it from rendering twice.

        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        var alice = await Tester.SignInAsUniqueAlice();
        await Tester.SignIn(bob);
        var chatId = (ChatId)PeerChatId.New(bob.Id, alice.Id);
        var authors = AppHost.Services.GetRequiredService<IAuthorsBackend>();
        var bobAuthor = await authors.EnsureJoined(chatId, bob.Id, CancellationToken.None);
        var aliceAuthor = await authors.EnsureJoined(chatId, alice.Id, CancellationToken.None);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();

        // act
        await liveBackend.StartCall(
            chatId, bobAuthor.Id, new[] { aliceAuthor.Id }.ToApiArray(), false, CancellationToken.None);
        await liveBackend.AcceptCall(chatId, aliceAuthor.Id, CancellationToken.None);
        var connected = await liveBackend.GetState(chatId, CancellationToken.None);
        await liveBackend.LeaveCall(chatId, aliceAuthor.Id, CancellationToken.None);
        await liveBackend.LeaveCall(chatId, bobAuthor.Id, CancellationToken.None);

        var conversationId = connected!.ToMaterializedConversation().Id;
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        chatUI.ToggleExpandConversation(conversationId);
        var idRange = await Tester.Chats.GetIdRange(Tester.Session, chatId, CancellationToken.None);
        var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);
        var items = await chatUI.GetChatItems(chatId, query, 0, CancellationToken.None);
        var messages = items.Items.SelectMany(i => i.GetLeafMessages()).ToList();

        // assert
        var hasSplitFooter = messages.OfType<ConversationFooter>().Any(f => f.Conversation!.Id == conversationId);
        hasSplitFooter.Should().BeTrue(
            "the toggle must have actually expanded the conversation - collapsed, the fold hides the " +
            "entry for an unrelated reason and both assertions below would pass vacuously");
        var hasEndedCallEntryAsMessage = messages.OfType<ChatEntryMessage>()
            .Any(m => m.Kind == ChatMessageKind.None && m.Entry is CallEntry { Outcome: CallOutcome.Ended });
        hasEndedCallEntryAsMessage.Should().BeFalse(
            "the entry only anchors the card in the lid range; the conversation item is the card");
        var hasCallConversation = messages.Any(m => m.Conversation is { IsCall: true });
        hasCallConversation.Should().BeTrue("a finished call must leave a conversation card behind");
    }

    [Fact]
    public async Task MaterializedCallConversationShouldSpanRealTalkTime()
    {
        // Before this fix StartsAt/EndsAt both fell back to defaults that never differ for a
        // transcription-off call (LastSummaryAt is never written), so the footer always read "0:00".

        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        var alice = await Tester.SignInAsUniqueAlice();
        await Tester.SignIn(bob);
        var chatId = (ChatId)PeerChatId.New(bob.Id, alice.Id);
        var authors = AppHost.Services.GetRequiredService<IAuthorsBackend>();
        var bobAuthor = await authors.EnsureJoined(chatId, bob.Id, CancellationToken.None);
        var aliceAuthor = await authors.EnsureJoined(chatId, alice.Id, CancellationToken.None);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        var conversations = AppHost.Services.GetRequiredService<IConversationsBackend>();

        // act
        await liveBackend.StartCall(
            chatId, bobAuthor.Id, new[] { aliceAuthor.Id }.ToApiArray(), false, CancellationToken.None);
        await liveBackend.AcceptCall(chatId, aliceAuthor.Id, CancellationToken.None);
        var connected = await liveBackend.GetState(chatId, CancellationToken.None);
        var beforeHangup = Moment.Now;
        await liveBackend.LeaveCall(chatId, aliceAuthor.Id, CancellationToken.None);
        await liveBackend.LeaveCall(chatId, bobAuthor.Id, CancellationToken.None);

        // assert
        var conversationId = connected!.ToMaterializedConversation().Id;
        var conversation = await conversations.Get(conversationId, CancellationToken.None);
        conversation.Should().NotBeNull();
        // Not exact equality: the storage round-trip quantizes Moment to microseconds.
        (conversation!.StartsAt - connected.SessionStartedAt!.Value).Duration()
            .Should().BeLessThan(TimeSpan.FromMilliseconds(1),
                "the span must be talk time from connect, not ring time from StartedAt");
        (conversation.EndsAt >= beforeHangup).Should().BeTrue(
            "EndsAt must be the close moment, not stuck at StartsAt or a LastSummaryAt a call never writes");
    }
}
