using ActualChat.Localization;
using Microsoft.Extensions.Localization;

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

    public static (string SenderName, string GroupTitle) GetTitleParts(Chat.Chat chat, AuthorFull author)
        // Empty GroupTitle means "this chat isn't a group" - Android turns a non-empty one into
        // SetGroupConversation(true), so a peer chat must leave it empty however it's titled.
        => chat.Id.GetThreadOutermostParentOrSelf().Kind switch {
            ChatKind.Group or ChatKind.Place => (author.Avatar.Name, chat.Title),
            ChatKind.Peer => (author.Avatar.Name, ""),
            _ => throw new ArgumentOutOfRangeException($"{nameof(chat)}.{nameof(chat.Kind)}", chat.Kind, null),
        };

    public static string GetTitle(string senderName, string groupTitle)
        => groupTitle.IsNullOrEmpty() ? senderName : $"{senderName} @ {groupTitle}";

    public static string GetIconUrl(Chat.Chat chat, AuthorFull author, UrlMapper urlMapper)
        // Unsized, the generator draws its 80px base, which an avatar slot on a 3x screen upscales.
        => urlMapper.IconUrl(chat.GetIconQuery(author, AvatarQuery.SupportedSizes[^1], renderAvatarTitle: true));

    public static string GetVoiceChatStartedText(IReadOnlyList<string> authorNames, IStringLocalizer l)
    {
        var shown = authorNames.Take(Constants.Notification.MaxSummaryAuthors).ToList();
        var moreCount = authorNames.Count - shown.Count;
        var names = shown.Count switch {
            0 => "",
            1 => shown[0],
            _ when moreCount > 0 => l.Notification_NamesAndMore(moreCount, string.Join(", ", shown), moreCount),
            _ => l.Conversation_TwoNames_Format(string.Join(", ", shown.Take(shown.Count - 1)), shown[^1]),
        };
        // Every author, not just the shown ones: "and 3 more" is part of the subject.
        return names.IsNullOrEmpty()
            ? l.Notification_VoiceChatStarted
            : l.Notification_VoiceChatStartedBy(authorNames.Count, names);
    }

    public static string ComposeAggregatedText(ChatEntryRelatedNotification notification, IStringLocalizer l)
    {
        var messages = notification.RecentMessages;
        if (messages.IsEmpty)
            return notification.LeadText.IsNullOrEmpty() ? notification.Text : notification.LeadText;

        // The banner headline is the chat, so these lines are the only place a sender is named.
        var showAuthorNames = notification.ChatId.GetThreadOutermostParentOrSelf().Kind
            is ChatKind.Group or ChatKind.Place;
        var lines = new List<string>(messages.Count + 1);
        // Newest first: collapsed banners show only the first line(s), and that must be the
        // latest message, not the oldest unread one.
        for (var i = messages.Count - 1; i >= 0; i--) {
            var m = messages[i];
            lines.Add(showAuthorNames && !m.AuthorName.IsNullOrEmpty()
                ? l.Notification_AuthorLine_Format(m.AuthorName, m.Text)
                : m.Text);
        }
        var moreCount = notification.UnreadCount - messages.Count;
        if (moreCount > 0)
            lines.Add(l.Notification_EarlierMessages(moreCount, moreCount));
        return string.Join('\n', lines);
    }
}
