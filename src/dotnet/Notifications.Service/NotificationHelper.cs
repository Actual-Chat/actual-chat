namespace ActualChat.Notifications;

public static class NotificationHelper
{
    // The single source of truth for mode filtering: fan-out and display-side suppression must
    // agree, or OnPush drops exempted pushes when it re-reads GetUserNotificationInfo.
    public static NotificationImportance GetImportance(NotificationKind kind)
        => kind switch {
            NotificationKind.Attention or NotificationKind.IncomingCall => NotificationImportance.Ringer,
            NotificationKind.Mention or NotificationKind.Reply or NotificationKind.Invitation
                => NotificationImportance.Important,
            _ => NotificationImportance.Ordinary,
        };

    public static bool IsDeliverable(NotificationImportance importance, ChatNotificationMode mode)
        => importance switch {
            NotificationImportance.Ringer => true,
            NotificationImportance.Important => mode != ChatNotificationMode.Muted,
            _ => mode == ChatNotificationMode.Default,
        };

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
