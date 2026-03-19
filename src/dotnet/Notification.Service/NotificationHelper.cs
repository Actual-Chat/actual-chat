using ActualChat.Chat;

namespace ActualChat.Notification;

public static class NotificationHelper
{
    public static string GetTitle(Chat.Chat chat, AuthorFull author)
        => chat.Kind switch {
            ChatKind.Group or ChatKind.Place or ChatKind.Thread => $"{author.Avatar.Name} @ {chat.Title}",
            ChatKind.Peer => $"{author.Avatar.Name}",
            _ => throw new ArgumentOutOfRangeException($"{nameof(chat)}.{nameof(chat.Kind)}", chat.Kind, null),
        };

    public static string GetIconUrl(Chat.Chat chat, AuthorFull author, UrlMapper urlMapper)
    {
        var query = chat.GetIconQuery(author);
        return urlMapper.IconUrl(query);
    }

    public static async ValueTask<(string Content, HashSet<MentionId> MentionIds)> GetText(
        ChatEntry entry,
        MarkupConsumer consumer,
        KeyedFactory<IBackendChatMarkupHub, ChatId> chatMarkupHubFactory,
        CancellationToken cancellationToken)
    {
        var chatMarkupHub = chatMarkupHubFactory[entry.ChatId];
        var markup = await chatMarkupHub.GetMarkup(entry, consumer, cancellationToken).ConfigureAwait(false);
        var mentionIds = MentionExtractor.Instance.GetMentionIds(markup);
        return (markup.ToReadableText(consumer), mentionIds);
    }
}
