using ActualChat.Hashing;
using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection))]
public class SendingMessagesDisplayTest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    private BlazorTester Tester => field ??= AppHost.NewBlazorTester(Out);

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    // Regression coverage for the sending-message display path (GetChatItems must merge the
    // optimistic "sending" entries at the chat tail). The empty-tail case this guards in prod -
    // idTiles == 0 - isn't reproducible in-process (the test host keeps removed entries as
    // tombstone tiles), so this exercises the merge via the normal path; the idTiles == 0 branch
    // is covered by live verification.
    [Fact]
    public async Task ShouldSurfaceSendingMessageAtChatTail()
    {
        // arrange: a chat with no visible messages (its only message removed)
        await Tester.SignInAsUniqueBob();
        var (chat, _) = await Tester.CreateAndGetChat(false, "sending-display-test");
        var entry = await Tester.CreateTextEntry(chat.Id, "to-be-removed");
        await Tester.RemoveTextEntry(entry.Id);

        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        var sendingMessages = Tester.ScopedAppServices.GetRequiredService<SendingMessages>();
        var now = Tester.AppServices.Clocks().SystemClock.Now;

        // act: register an optimistic "sending" message, then load the chat at its tail
        var accessor = sendingMessages.GetSendingMessages(chat.Id);
        var sendingMessage = new SendingMessage(
            Guid.NewGuid().ToString(),
            "",
            chat.Id,
            null,
            now,
            "TEST_SENDING",
            HashString.None,
            null,
            () => { });
        accessor.ChatSendingMessages.AddSendingMessage(sendingMessage);

        var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
        var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);
        var items = await chatUI.GetChatItems(chat.Id, query, 0, CancellationToken.None);

        // assert: the optimistic message is surfaced
        items.Items.OfType<ChatEntryMessage>()
            .Select(m => m.Entry.Content)
            .Should().Contain("TEST_SENDING");
    }

    // The optimistic copy is dropped only when ProcessLoadedEntriesRange sees PostedChatEntry, and that
    // is set by ConfirmMessageWasSent - which can land after the real entry has already rendered, since
    // the post result and the entry's invalidation reach the client over unordered channels. Confirming
    // has to invalidate the tail tile, or the message keeps a stuck "sending" twin next to itself.
    [Fact]
    public async Task ShouldDropSendingMessageConfirmedAfterEntryRendered()
    {
        // arrange: the real entry is already posted and rendered, but its send is still unconfirmed
        await Tester.SignInAsUniqueBob();
        var (chat, _) = await Tester.CreateAndGetChat(false, "sending-confirm-test");
        var entry = await Tester.CreateTextEntry(chat.Id, "CONFIRM_RACE");

        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        var sendingMessages = Tester.ScopedAppServices.GetRequiredService<SendingMessages>();
        var now = Tester.AppServices.Clocks().SystemClock.Now;

        var accessor = sendingMessages.GetSendingMessages(chat.Id);
        var sendingMessage = new SendingMessage(
            Guid.NewGuid().ToString(),
            "",
            chat.Id,
            null,
            now,
            "CONFIRM_RACE",
            HashString.None,
            null,
            () => { });
        accessor.ChatSendingMessages.AddSendingMessage(sendingMessage);

        // act
        await TestWait.When(async ct => {
            var sending = await GetSendingContents(chatUI, chat.Id, ct);
            sending.Should().Equal("CONFIRM_RACE");
        }, TimeSpan.FromSeconds(10));

        accessor.ChatSendingMessages.ConfirmMessageWasSent(sendingMessage, entry, now, true);

        // assert: the optimistic twin is gone
        await TestWait.When(async ct => {
            var sending = await GetSendingContents(chatUI, chat.Id, ct);
            sending.Should().BeEmpty();
        }, TimeSpan.FromSeconds(10));
    }

    // Same race as above, seen from the other side: confirmation may never have happened yet when the
    // entry lands. The entry carries the send's ClientId, so its presence in the loaded range is enough
    // to retire the optimistic copy - otherwise the copy is re-emitted at the next free lid, where it
    // grows in and is dropped a render later.
    [Fact]
    public async Task ShouldDropSendingMessageWhoseEntryLoadedBeforeConfirmation()
    {
        // arrange: the entry is posted with a client id, and its send is never confirmed
        await Tester.SignInAsUniqueBob();
        var (chat, _) = await Tester.CreateAndGetChat(false, "sending-client-id-test");
        var clientId = Guid.NewGuid().ToString();
        await Tester.Commander.Call(new Chats_UpsertEntry {
            Session = Tester.Session,
            ChatId = chat.Id,
            LocalId = null,
            Text = "CLIENT_ID_RACE",
            ClientId = clientId,
        });

        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        var sendingMessages = Tester.ScopedAppServices.GetRequiredService<SendingMessages>();
        var now = Tester.AppServices.Clocks().SystemClock.Now;

        var accessor = sendingMessages.GetSendingMessages(chat.Id);
        var sendingMessage = new SendingMessage(
            Guid.NewGuid().ToString(),
            clientId,
            chat.Id,
            null,
            now,
            "CLIENT_ID_RACE",
            HashString.None,
            null,
            () => { });
        accessor.ChatSendingMessages.AddSendingMessage(sendingMessage);

        // act + assert: loading the tail retires the copy, though ConfirmMessageWasSent never ran and
        // PostedChatEntry is still null - the entry's ClientId is what retires it
        await TestWait.When(async ct => {
            await GetSendingContents(chatUI, chat.Id, ct);
            sendingMessage.PostedChatEntry.Should().BeNull();
            sendingMessage.LoadedForDisplay.Should().BeTrue();
        }, TimeSpan.FromSeconds(10));
    }

    // A location send has no LocationId until the queue creates the shared location, so the optimistic
    // entry must carry the point itself - that's what the message view renders the pending pin from.
    [Fact]
    public async Task ShouldSurfaceQueuedLocationAtChatTail()
    {
        // arrange
        await Tester.SignInAsUniqueBob();
        var (chat, _) = await Tester.CreateAndGetChat(false, "sending-location-test");
        await Tester.CreateTextEntry(chat.Id, "before-location");

        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        var sendingMessages = Tester.ScopedAppServices.GetRequiredService<SendingMessages>();
        var now = Tester.AppServices.Clocks().SystemClock.Now;
        var point = new GeoPoint(51.5074, -0.1278);

        // act
        var accessor = sendingMessages.GetSendingMessages(chat.Id);
        accessor.ChatSendingMessages.AddSendingMessage(new SendingMessage(
            Guid.NewGuid().ToString(),
            "",
            chat.Id,
            null,
            now,
            "",
            HashString.None,
            null,
            () => { }) {
            LocationPoint = point,
            IsLocationPlace = true,
        });

        var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
        var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);
        var items = await chatUI.GetChatItems(chat.Id, query, 0, CancellationToken.None);

        // assert
        var sending = items.Items.OfType<ChatEntryMessage>()
            .Select(m => m.Entry.GetSendingMessage())
            .SkipNullItems()
            .ToList();
        sending.Should().ContainSingle();
        sending[0].LocationPoint.Should().Be(point);
        sending[0].IsLocationPlace.Should().BeTrue();
    }

    // The queue creates the shared location and posts the entry for it: both must land, and the
    // entry must reference the location the point was sent with.
    [Fact]
    public async Task ShouldPostLocationEntryViaSendingQueue()
    {
        // arrange
        await Tester.SignInAsUniqueBob();
        var (chat, _) = await Tester.CreateAndGetChat(false, "sending-location-queue-test");
        var sendingMessages = Tester.ScopedAppServices.GetRequiredService<SendingMessages>();
        var sharedLocations = Tester.AppServices.GetRequiredService<ISharedLocations>();
        var point = new GeoPoint(48.8566, 2.3522);

        // act
        var postTask = await sendingMessages.Send(
            SendMessageRequest.NewLocation(chat.Id, point, isPlace: true),
            CancellationToken.None);
        var entry = await postTask.WaitAsync(TimeSpan.FromSeconds(30));

        // assert
        entry.Should().NotBeNull();
        entry!.LocationId.Should().NotBeNull();
        var location = await sharedLocations.Get(Tester.Session, chat.Id, entry.LocationId!, CancellationToken.None);
        location.Should().NotBeNull();
        location!.Point.Should().Be(point);
        location.IsPlace.Should().BeTrue();
        location.Duration.Should().Be(TimeSpan.Zero, "a one-shot send is a frozen pin, not a live share");
        var reread = await Tester.Chats.GetEntry(Tester.Session, entry.Id, CancellationToken.None);
        reread!.LocationId.Should().Be(entry.LocationId);
    }

    // A live share's entry goes through the queue too, with the queue creating the live share: the
    // reporter finds the send by the uuid it chose and adopts the share id from the posted entry.
    [Fact]
    public async Task ShouldPostLiveLocationEntryViaSendingQueue()
    {
        // arrange
        await Tester.SignInAsUniqueBob();
        var (chat, _) = await Tester.CreateAndGetChat(false, "sending-live-location-queue-test");
        var sendingMessages = Tester.ScopedAppServices.GetRequiredService<SendingMessages>();
        var sharedLocations = Tester.AppServices.GetRequiredService<ISharedLocations>();
        var point = new GeoPoint(52.52, 13.405);
        var duration = Constants.Location.Durations[0];
        var uuid = Ulid.NewUlid().ToString();

        // act
        var postTask = await sendingMessages.Send(
            SendMessageRequest.NewLiveLocation(chat.Id, point, duration, uuid),
            CancellationToken.None);
        var entry = await postTask.WaitAsync(TimeSpan.FromSeconds(30));

        // assert
        entry!.LocationId.Should().NotBeNull();
        var sending = sendingMessages.TryGetSendingMessage(chat.Id, uuid);
        sending.Should().NotBeNull("the reporter resumes a queued share by the uuid it chose");
        sending!.PostedChatEntry?.LocationId.Should().Be(entry.LocationId);
        var location = await sharedLocations.Get(Tester.Session, chat.Id, entry.LocationId!, CancellationToken.None);
        location!.Point.Should().Be(point);
        location.Duration.Should().Be(duration);
        location.IsLive(Tester.AppServices.Clocks().SystemClock.Now).Should().BeTrue();
    }

    private async Task<List<string>> GetSendingContents(
        ChatUI chatUI,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var idRange = await Tester.Chats.GetIdRange(Tester.Session, chatId, cancellationToken);
        var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);
        var items = await chatUI.GetChatItems(chatId, query, 0, cancellationToken);
        return items.Items.OfType<ChatEntryMessage>()
            .Where(m => m.Entry.IsSending)
            .Select(m => m.Entry.Content)
            .ToList();
    }

    // Each chat-items entry renders with a @key; duplicate sibling keys throw inside Blazor's keyed
    // render-tree diff and tear down the circuit. A conversation at the chat tail used to be emitted
    // twice - once by the last loaded tile, once by the explicit tail-tile fallback - producing a
    // duplicate ConversationStart @key. This guards the "GetChatItems yields unique render keys"
    // invariant while still surfacing the tail merge. The exact prod trigger (a summarized chat whose
    // tail ids are all removed, so no tile carries the tail) isn't reproducible in-process - the test
    // host keeps removed entries as tombstone tiles - so this exercises the invariant on the normal path.
    [Fact]
    public async Task ShouldNotProduceDuplicateRenderKeysAtChatTail()
    {
        // arrange: a chat with several messages and a removed tail entry
        await Tester.SignInAsUniqueBob();
        var (chat, _) = await Tester.CreateAndGetChat(false, "dup-key-test");
        for (var i = 0; i < 5; i++)
            await Tester.CreateTextEntry(chat.Id, $"msg-{i}");
        var tailEntry = await Tester.CreateTextEntry(chat.Id, "tail-to-remove");
        await Tester.RemoveTextEntry(tailEntry.Id);

        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        var sendingMessages = Tester.ScopedAppServices.GetRequiredService<SendingMessages>();
        var now = Tester.AppServices.Clocks().SystemClock.Now;

        // act: register an optimistic "sending" message, then load the chat at its tail
        var accessor = sendingMessages.GetSendingMessages(chat.Id);
        accessor.ChatSendingMessages.AddSendingMessage(new SendingMessage(
            Guid.NewGuid().ToString(),
            "",
            chat.Id,
            null,
            now,
            "TAIL_SENDING",
            HashString.None,
            null,
            () => { }));

        var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
        var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);
        var items = await chatUI.GetChatItems(chat.Id, query, 0, CancellationToken.None);

        // assert: the tail merge still surfaces the optimistic message
        items.Items.OfType<ChatEntryMessage>()
            .Select(m => m.Entry.Content)
            .Should().Contain("TAIL_SENDING");

        // assert: no duplicate @key among siblings - top-level items and each conversation's children
        items.Items
            .Select(i => ((IVirtualListItem)i).RenderKey)
            .Should().OnlyHaveUniqueItems();
        foreach (var conversation in items.Items.OfType<ExpandedConversationMessage>())
            conversation.Items
                .Select(i => ((IVirtualListItem)i).RenderKey)
                .Should().OnlyHaveUniqueItems();
    }
}
