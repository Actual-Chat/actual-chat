using ActualChat.Chat;
using ActualChat.Chat.Events;

namespace ActualChat.Notification;

public class NewChatEntryNotificationGenerator: IChatEventNotificationGenerator<NewChatEntryEvent>
{
    private readonly IChatAuthorsBackend _chatAuthorsBackend;
    private readonly IChatsBackend _chatsBackend;
    private readonly MomentClockSet _clocks;

    public NewChatEntryNotificationGenerator(
        IChatAuthorsBackend chatAuthorsBackend,
        IChatsBackend chatsBackend,
        MomentClockSet clocks)
    {
        _chatAuthorsBackend = chatAuthorsBackend;
        _chatsBackend = chatsBackend;
        _clocks = clocks;
    }

    // TODO(AK): support mentions
    // TODO(AK): throttle notifications
    public async IAsyncEnumerable<NotificationEntry> GenerateNotifications(
        NewChatEntryEvent chatEvent,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var clock = _clocks.CoarseSystemClock;
        var title = await GetTitle(chatEvent.ChatId, cancellationToken).ConfigureAwait(false);
        var content = GetContent(chatEvent.Content);
            yield return new ChatNotificationEntry(chatEvent.ChatId, chatEvent.AuthorId, title, content, clock.Now);
    }

    private async Task<string> GetTitle(string chatId, CancellationToken cancellationToken)
    {
        // TODO(AK): Internationalization
        var chat = await _chatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
        return chat?.Title ?? "New message";
    }

    private string GetContent(string chatEventContent)
    {
        if (chatEventContent.Length <= 1024)
            return chatEventContent;

        var lastSpaceIndex = chatEventContent.IndexOf(' ', 1000);
        return chatEventContent.Substring(0, lastSpaceIndex < 1024 ? lastSpaceIndex : 1000);
    }
}
