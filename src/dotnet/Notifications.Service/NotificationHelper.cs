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

    public static string GetVoiceChatStartedText(IReadOnlyList<string> authorNames)
    {
        var shown = authorNames.Take(Constants.Notification.MaxSummaryAuthors).ToList();
        var moreCount = authorNames.Count - shown.Count;
        var names = shown.Count switch {
            0 => "",
            1 => shown[0],
            _ when moreCount > 0 => $"{string.Join(", ", shown)} and {moreCount} more",
            _ => $"{string.Join(", ", shown.Take(shown.Count - 1))} and {shown[^1]}",
        };
        return names.IsNullOrEmpty() ? "Voice chat started" : $"{names} started a voice chat";
    }

    public static string ComposeAggregatedText(ChatEntryRelatedNotification notification)
    {
        var messages = notification.RecentMessages;
        if (messages.IsEmpty)
            return notification.LeadText.IsNullOrEmpty() ? notification.Text : notification.LeadText;

        // Newest first: collapsed banners show only the first line(s), and that must be the
        // latest message, not the oldest unread one.
        var showAuthorNames = notification.ChatId.GetThreadOutermostParentOrSelf().Kind
            is ChatKind.Group or ChatKind.Place;
        var lines = new List<string>(messages.Count + 1);
        for (var i = messages.Count - 1; i >= 0; i--) {
            var m = messages[i];
            lines.Add(showAuthorNames && !m.AuthorName.IsNullOrEmpty() ? $"{m.AuthorName}: {m.Text}" : m.Text);
        }
        var moreCount = notification.UnreadCount - messages.Count;
        if (moreCount > 0)
            lines.Add(moreCount == 1 ? "+1 earlier message" : $"+{moreCount} earlier messages");
        return string.Join('\n', lines);
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
