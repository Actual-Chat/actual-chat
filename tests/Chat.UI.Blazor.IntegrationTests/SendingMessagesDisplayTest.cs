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
}
