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

    public static string GetTitle(NotificationKind kind, string senderName, string groupTitle)
        // A group banner of a coalescing kind names each message's author in its body (see
        // ComposeAggregatedText), so headlining it with the sender too would name them twice - and
        // it gets the chat headline from its very first message, or the same banner would re-title
        // itself once a second author arrives. The other kinds have a plain body, so the sender
        // stays in their headline.
        => groupTitle.IsNullOrEmpty()
            ? senderName
            : IsCoalescing(kind)
                ? groupTitle
                : $"{senderName} @ {groupTitle}";

    public static bool IsCoalescing(NotificationKind kind)
        => kind is NotificationKind.Message or NotificationKind.Reply or NotificationKind.Thread;

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

        // One banner holds several authors, and the headline names the chat (see GetTitle).
        var showAuthorNames = notification.ChatId.GetThreadOutermostParentOrSelf().Kind
            is ChatKind.Group or ChatKind.Place;
        var lines = new List<string>(messages.Count + 1);
        // Oldest -> newest, i.e. the order the messages have in the chat, so the banner reads as a
        // transcript excerpt - which puts the count of the ones that fell out of the window on top.
        var moreCount = notification.UnreadCount - messages.Count;
        if (moreCount > 0)
            lines.Add(l.Notification_EarlierMessages(moreCount, moreCount));
        foreach (var m in messages)
            lines.Add(showAuthorNames && !m.AuthorName.IsNullOrEmpty()
                ? l.Notification_AuthorLine_Format(m.AuthorName, m.Text)
                : m.Text);
        return string.Join('\n', lines);
    }

    public static ReactionNotification ComposeReaction(ReactionNotification notification, IStringLocalizer l)
    {
        // One reactor with one emoji is exactly what the send path already composed - which is also
        // every peer chat, where reactions are one per author and your own never notify. Returning
        // the same instance then lets the caller skip the update; a blob written before SenderName
        // and QuotedText existed keeps whichever half it can't recompose.
        var otherCount = notification.AuthorIds.Count - 1;
        var emojis = GetCurrentEmojis(notification);
        var mustRetitle = otherCount > 0 && !notification.SenderName.IsNullOrEmpty();
        var mustRewrite = emojis.Count > 1 && !notification.QuotedText.IsNullOrEmpty();
        if (!mustRetitle && !mustRewrite)
            return notification;

        // Always recomposed from SenderName, which is why nothing here writes back to it: a merge
        // can carry an existing notification's copy forward, and the count would be appended twice.
        var displaySenderName = mustRetitle
            ? l.Notification_SenderAndMore(otherCount, notification.SenderName, otherCount)
            : "";
        return notification with {
            DisplaySenderName = displaySenderName,
            Title = mustRetitle
                ? GetTitle(NotificationKind.Reaction, displaySenderName, notification.GroupTitle)
                : notification.Title,
            Text = mustRewrite
                ? l.Notification_Reaction_Format(ComposeEmojiRun(emojis), notification.QuotedText)
                : notification.Text,
        };
    }

    // Private methods

    private static IReadOnlyList<Emoji> GetCurrentEmojis(ReactionNotification notification)
    {
        // Emojis accumulates with dedup and never drops one, so a reactor who switched emoji leaves
        // their old one behind - and a removed reaction doesn't notify at all. There can't be more
        // current emoji than reactors and the stale ones are the oldest, so the newest that many are
        // the closest the stored state gets to what the message carries now.
        var emojis = notification.Emojis;
        if (emojis.Count <= 1)
            return emojis;

        // LastEmoji, not the last arrival: switching to one already in the set appends nothing, so
        // arrival order would drop the newest reaction and keep the stale one it replaced.
        var newest = notification.LastEmoji ?? emojis[^1];
        var rest = emojis.Where(x => x != newest).ToList();
        var restCount = Math.Min(rest.Count, Math.Max(1, notification.AuthorIds.Count) - 1);
        var result = rest.Skip(rest.Count - Math.Max(0, restCount)).ToList();
        result.Add(newest);
        return result;
    }

    private static string ComposeEmojiRun(IReadOnlyList<Emoji> emojis)
    {
        var shownCount = Math.Min(emojis.Count, Constants.Notification.MaxShownReactionEmojis);
        var run = string.Concat(emojis.Take(shownCount).Select(x => x.Symbol));
        return emojis.Count > shownCount ? run + "…" : run;
    }
}
