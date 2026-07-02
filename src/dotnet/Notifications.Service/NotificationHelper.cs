namespace ActualChat.Notifications;

public static class NotificationHelper
{
    public static string GetTitle(Chat.Chat chat, AuthorFull author)
        => chat.Id.GetThreadOutermostParentOrSelf().Kind switch {
            ChatKind.Group or ChatKind.Place => $"{author.Avatar.Name} @ {chat.Title}",
            ChatKind.Peer => $"{author.Avatar.Name}",
            _ => throw new ArgumentOutOfRangeException($"{nameof(chat)}.{nameof(chat.Kind)}", chat.Kind, null),
        };

    public static string GetIconUrl(Chat.Chat chat, AuthorFull author, UrlMapper urlMapper)
        => urlMapper.IconUrl(chat.GetIconQuery(author));

    public static string GetAggregatedText(string leadText, IReadOnlyList<string> authorNames, int moreCount)
    {
        // moreCount counts messages beyond those LeadText already shows, so a rolled-in lead
        // never produces a "+1 more" for a message the user is looking at.
        if (moreCount <= 0)
            return leadText;

        var namePart = string.Join(", ", authorNames.Take(Constants.Notification.MaxSummaryAuthors));
        var moreText = moreCount == 1 ? "+1 more message" : $"+{moreCount} more messages";
        var tail = namePart.IsNullOrEmpty() ? moreText : $"{namePart} · {moreText}";
        return $"{leadText}\n{tail}";
    }

    public static async ValueTask<(string Content, HashSet<MentionRef> MentionIds)> GetText(
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
