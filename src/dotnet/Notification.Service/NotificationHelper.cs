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
        => chat.Kind switch {
            ChatKind.Group or ChatKind.Place or ChatKind.Thread => chat.Picture?.BlobId.IsNullOrEmpty() == false
                ? urlMapper.ContentUrl(chat.Picture.BlobId)
                : "/favicon_voxt.ico",
            ChatKind.Peer => author.Avatar.Media?.BlobId.IsNullOrEmpty() == false
                ? urlMapper.ContentUrl(author.Avatar.Media.BlobId)
                : "/favicon_voxt.ico",
            _ => throw new ArgumentOutOfRangeException($"{nameof(chat)}.{nameof(chat.Kind)}", chat.Kind, null),
        };

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
