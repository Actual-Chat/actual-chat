using ActualChat.Live;
using ActualChat.Streaming;
using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;
using Bunit;
using Microsoft.AspNetCore.Components.Rendering;

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
        // Collapsed, the fold already hides the entry for an unrelated reason; expanded, it is back in the
        // per-entry loop, where only the tile-builder skip keeps it from rendering twice.

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
        // A message short of the summary gate is what makes the card expanded by default
        await Tester.CreateTextEntry(chatId, "hi");
        await liveBackend.SetParticipation(
            chatId, aliceAuthor.Id, ParticipationKind.Record, false, CancellationToken.None);
        await liveBackend.SetParticipation(
            chatId, bobAuthor.Id, ParticipationKind.Record, false, CancellationToken.None);

        var conversationId = connected!.OpenConversation!.ToMaterializedConversation().Id;
        var messages = await GetMessages(chatId);

        // assert
        var hasSplitFooter = messages.OfType<ConversationFooter>().Any(f => f.Conversation!.Id == conversationId);
        hasSplitFooter.Should().BeTrue(
            "the conversation must render expanded - collapsed, the fold hides the entry for an " +
            "unrelated reason and both assertions below would pass vacuously");
        var hasEndedCallEntryAsMessage = messages.OfType<ChatEntryMessage>()
            .Any(m => m.Kind == ChatMessageKind.None && m.Entry is CallEntry { Outcome: CallOutcome.Ended });
        hasEndedCallEntryAsMessage.Should().BeFalse(
            "the entry only anchors the card in the lid range; the conversation item is the card");
        var hasCallConversation = messages.Any(m => m.Conversation is { IsCall: true });
        hasCallConversation.Should().BeTrue("a finished call must leave a conversation card behind");
    }

    [Fact]
    public async Task ACallThatLeftNoMessagesShouldNotExpand()
    {
        // Joining expands the live block through an override, which the card it turns into inherits

        // arrange
        var (chatId, bobAuthor, aliceAuthor) = await NewPeerChat();
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.StartCall(
            chatId, bobAuthor.Id, new[] { aliceAuthor.Id }.ToApiArray(), false, CancellationToken.None);
        await liveBackend.AcceptCall(chatId, aliceAuthor.Id, CancellationToken.None);
        var connected = await liveBackend.GetState(chatId, CancellationToken.None);
        await liveBackend.SetParticipation(
            chatId, aliceAuthor.Id, ParticipationKind.Record, false, CancellationToken.None);
        await liveBackend.SetParticipation(
            chatId, bobAuthor.Id, ParticipationKind.Record, false, CancellationToken.None);
        var conversationId = connected!.OpenConversation!.ToMaterializedConversation().Id;
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();

        // act
        chatUI.ToggleExpandConversation(conversationId);
        var messages = await GetMessages(chatId);

        // assert
        messages.Should().Contain(m => m.Conversation != null && m.Conversation.Id == conversationId,
            "the card itself must still be there");
        messages.OfType<ConversationFooter>().Should().NotContain(f => f.Conversation!.Id == conversationId,
            "there is nothing between a message-less call's header and footer to expand into");
    }

    [Fact]
    public async Task AnEndedCallsEntryShouldStayHiddenWhileItsCardIsStillOpen()
    {
        // The call's end writes its entry before the session closes into the card, and until then the open
        // card's range stops at its last summary - short of an entry that follows a message of the call.

        // arrange
        var (chatId, bobAuthor, aliceAuthor) = await NewPeerChat();
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.StartCall(
            chatId, bobAuthor.Id, new[] { aliceAuthor.Id }.ToApiArray(), false, CancellationToken.None);
        await liveBackend.AcceptCall(chatId, aliceAuthor.Id, CancellationToken.None);
        var connected = await liveBackend.GetState(chatId, CancellationToken.None);
        var liveConversationId = connected!.OpenConversation!.Id;
        await Tester.CreateTextEntry(chatId, "hi");
        var callEntryDiff = new ChatEntryDiff {
            Kind = ChatEntryKind.Call,
            AuthorId = Constants.User.Walle.GetWalleAuthorId(chatId),
            BeginsAt = connected.StartedAt!.Value,
            EndsAt = Moment.Now,
            CallerId = bobAuthor.Id,
            Outcome = CallOutcome.Ended,
            ConversationId = liveConversationId,
        };
        var writeEntry = new ChatsBackend_ChangeEntry(ChatEntryId.New(chatId, 0), null, Change.Create(callEntryDiff));
        var callEntry = await AppHost.Services.Commander().Call(writeEntry, true, CancellationToken.None);

        // act
        var messages = await GetMessages(chatId);

        // assert
        messages.OfType<ChatEntryMessage>().Should().Contain(m => m.Entry.Content == "hi",
            "the open card must be expanded here, or the entry would be hidden by its fold, not by its card");
        var stillConnected = await liveBackend.GetState(chatId, CancellationToken.None);
        stillConnected!.OpenConversation!.ToConversation().EntryLidRange.Contains(callEntry.LocalId).Should().BeFalse(
            "the open card's own range must miss the entry, or the range check alone would hide it");
        messages.OfType<ChatEntryMessage>()
            .Should().NotContain(m => m.Kind == ChatMessageKind.None && m.Entry is CallEntry,
                "the card already shows the call; the entry flashed in as a second one");
    }

    [Fact]
    public async Task AChatWithoutSummariesShouldStillDrawAnEndedCallsCard()
    {
        // arrange
        var (chatId, bobAuthor, aliceAuthor) = await NewPeerChat();
        // A peer chat is only a row once something is written to it
        await Tester.CreateTextEntry(chatId, "hello");
        var turnSummariesOff = new Chats_Change {
            Session = Tester.Session,
            ChatId = chatId,
            ExpectedVersion = null,
            Change = Change.Update(new ChatDiff { IsSummarized = false }),
        };
        await Tester.Commander.Call(turnSummariesOff, CancellationToken.None);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();

        // act
        await liveBackend.StartCall(
            chatId, bobAuthor.Id, new[] { aliceAuthor.Id }.ToApiArray(), false, CancellationToken.None);
        await liveBackend.AcceptCall(chatId, aliceAuthor.Id, CancellationToken.None);
        await liveBackend.SetParticipation(
            chatId, aliceAuthor.Id, ParticipationKind.Record, false, CancellationToken.None);
        await liveBackend.SetParticipation(
            chatId, bobAuthor.Id, ParticipationKind.Record, false, CancellationToken.None);

        // assert
        var chat = await Tester.Chats.Get(Tester.Session, chatId, CancellationToken.None);
        chat!.IsSummarized.Should().BeFalse();
        var messages = await GetMessages(chatId);
        messages.Any(m => m.Conversation is { IsCall: true }).Should().BeTrue(
            "the card is all a finished call leaves behind in a chat without summaries");
        messages.OfType<ChatEntryMessage>()
            .Should().NotContain(m => m.Kind == ChatMessageKind.None && m.Entry is CallEntry,
                "the card already shows the call");
    }

    [Fact]
    public async Task AChatWithoutSummariesShouldKeepAnOldConversationsMessagesInPlace()
    {
        // arrange
        var (chatId, _, _) = await NewPeerChat();
        var first = await Tester.CreateTextEntry(chatId, "said while summaries were on");
        var last = await Tester.CreateTextEntry(chatId, "and this too");
        var old = new Conversation(ConversationId.New(chatId, first.LocalId), 1) {
            Title = "Old", Description = "d", Summary = "s", MessageCount = 2,
            EndEntryLid = last.LocalId,
            StartsAt = first.BeginsAt, EndsAt = last.BeginsAt,
            IsExpandedByDefault = false,
        };
        var materialize = new ConversationBackend_Materialize(old);
        await Tester.Commander.Call(materialize, CancellationToken.None);
        var turnSummariesOff = new Chats_Change {
            Session = Tester.Session,
            ChatId = chatId,
            ExpectedVersion = null,
            Change = Change.Update(new ChatDiff { IsSummarized = false }),
        };
        await Tester.Commander.Call(turnSummariesOff, CancellationToken.None);

        // act
        var messages = await GetMessages(chatId);

        // assert
        messages.Should().NotContain(m => m.Conversation != null && m.Conversation.Id == old.Id,
            "a chat without summaries draws only its calls' cards");
        messages.OfType<ChatEntryMessage>().Select(m => m.Entry.LocalId)
            .Should().Contain([first.LocalId, last.LocalId], "the old conversation must not fold its messages");
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
        await liveBackend.SetParticipation(
            chatId, aliceAuthor.Id, ParticipationKind.Record, false, CancellationToken.None);
        await liveBackend.SetParticipation(
            chatId, bobAuthor.Id, ParticipationKind.Record, false, CancellationToken.None);

        // assert
        var conversationId = connected!.OpenConversation!.ToMaterializedConversation().Id;
        var conversation = await conversations.Get(conversationId, CancellationToken.None);
        conversation.Should().NotBeNull();
        // Not exact equality: the storage round-trip quantizes Moment to microseconds.
        (conversation!.StartsAt - connected.StartedAt!.Value).Duration()
            .Should().BeLessThan(TimeSpan.FromMilliseconds(1),
                "the span must be talk time from connect, not ring time from StartedAt");
        (conversation.EndsAt >= beforeHangup).Should().BeTrue(
            "EndsAt must be the close moment, not stuck at StartsAt or a LastSummaryAt a call never writes");
    }

    [Fact]
    public async Task APeerCallsLiveHeaderShouldNotOfferJoinAfterHangUp()
    {
        // The server keeps a peer call's session for its leave grace after a hang-up, and Join there
        // offered to re-enter a call that is already ending.

        // arrange
        var (chatId, bobAuthor, aliceAuthor) = await NewPeerChat();
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        await liveBackend.StartCall(
            chatId, bobAuthor.Id, new[] { aliceAuthor.Id }.ToApiArray(), false, CancellationToken.None);
        await liveBackend.AcceptCall(chatId, aliceAuthor.Id, CancellationToken.None);
        // Both parties streaming, or the connect grace ends the call before the header is checked
        await liveBackend.OnStreamRegistered(chatId, bobAuthor.Id, null, true, true, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chatId, aliceAuthor.Id, null, true, true, CancellationToken.None);
        var connected = await liveBackend.GetState(chatId, CancellationToken.None);
        var conversation = connected!.OpenConversation!.ToConversation();
        var chat = await Tester.Chats.Get(Tester.Session, chatId, CancellationToken.None);
        var hub = Tester.ScopedAppServices.GetRequiredService<AppUIHub>();
        Tester.Renderer.SetRendererInfo(new RendererInfo("Server", true));
        var header = Tester.Render<LiveConversationHeaderView>(p => p
            .Add(x => x.Header, new LiveConversationHeader(conversation))
            .Add(x => x.ChatContext, new ChatContext(hub, chat!)));
        var headerState = ((IStatefulComponent<LiveConversationHeaderState>)header.Instance).State;
        // The caller's client joins an answered call on its own
        await TestWait.When(async ct => {
            var isJoined = await hub.LiveSessionUI.AmIInLiveConversation(chatId, ct);
            isJoined.Should().BeTrue();
        });

        // act
        await hub.CallUI.HangUp(chatId);

        // assert - the placeholder state reads as joined, so a false here is the computed one
        await TestWait.When(async ct => {
            var state = await headerState.Use(ct);
            state.IsJoined.Should().BeFalse();
        });
        // The state runs ahead of the markup, so the render this check reads is forced
        header.Render();
        headerState.Value.IsDissolving.Should().BeFalse(
            "the session must still be in its leave grace, where only the fix keeps Join away");
        header.FindAll(".c-lc-join").Should().BeEmpty();
    }

    // Private methods

    private async Task<(ChatId ChatId, AuthorFull Bob, AuthorFull Alice)> NewPeerChat()
    {
        var bob = await Tester.SignInAsUniqueBob();
        var alice = await Tester.SignInAsUniqueAlice();
        await Tester.SignIn(bob);
        var chatId = (ChatId)PeerChatId.New(bob.Id, alice.Id);
        var authors = AppHost.Services.GetRequiredService<IAuthorsBackend>();
        var bobAuthor = await authors.EnsureJoined(chatId, bob.Id, CancellationToken.None);
        var aliceAuthor = await authors.EnsureJoined(chatId, alice.Id, CancellationToken.None);
        return (chatId, bobAuthor, aliceAuthor);
    }

    private async Task<List<ChatMessage>> GetMessages(ChatId chatId)
    {
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        var idRange = await Tester.Chats.GetIdRange(Tester.Session, chatId, CancellationToken.None);
        var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);
        var items = await chatUI.GetChatItems(chatId, query, 0, CancellationToken.None);
        return items.Items.SelectMany(i => i.GetLeafMessages()).ToList();
    }
}
