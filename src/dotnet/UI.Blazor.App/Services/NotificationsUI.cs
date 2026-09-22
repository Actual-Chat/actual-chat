using ActualChat.Notifications;
using Notification = ActualChat.Notifications.Notification;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Per-kind and per-chat projections of the user's active notification set, so the
/// notifications panel, its badges and the navbar bell share one computed over
/// <see cref="INotifications.ListActive"/>.
/// </summary>
public class NotificationsUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    // The reactions tab has no ChatListFilter: its rows are notifications, not chats
    public const string ReactionsFilterId = "@reactions";
    public const int MaxHistoryGroups = 30;

    private INotifications Notifications => field ??= Hub.Notifications;

    [ComputeMethod]
    public virtual async Task<ApiArray<Notification>> ListByKind(
        NotificationKind kind, CancellationToken cancellationToken = default)
    {
        var active = await Notifications.ListActive(Session, cancellationToken).ConfigureAwait(false);
        return active
            .Where(x => x.Kind == kind)
            .OrderByDescending(x => x.SentAt)
            .ToApiArray();
    }

    [ComputeMethod]
    public virtual async Task<ApiArray<NotificationHistoryGroup>> ListHistory(
        Symbol filterId, CancellationToken cancellationToken = default)
    {
        // One group per chat: the newest past notification of the tab's kinds and how many older
        // ones it stands for. Notifications still in the active set are the active rows' business.
        var items = await ListHistoryItems(filterId, cancellationToken).ConfigureAwait(false);
        var active = await Notifications.ListActive(Session, cancellationToken).ConfigureAwait(false);
        var activeIds = active.Select(x => x.Id).ToHashSet();
        var isPeopleOnly = filterId == ChatListFilter.UnreadPeople.Id;
        return items
            .Where(x => x.ChatId is not null
                && (x.NotificationId is null || !activeIds.Contains(x.NotificationId))
                && (!isPeopleOnly || x.ChatId.Kind == ChatKind.Peer))
            .GroupBy(x => x.ChatId!)
            .Select(g => new NotificationHistoryGroup(g.Key, g.First(), g.Count() - 1))
            .OrderByDescending(g => g.Newest.Seq)
            // The panel renders the whole block at once, so it's bounded here rather than per tab
            .Take(MaxHistoryGroups)
            .ToApiArray();
    }

    [ComputeMethod]
    public virtual async Task<ApiArray<NotificationHistoryItem>> ListHistoryItems(
        Symbol filterId, CancellationToken cancellationToken = default)
    {
        // Separate from ListHistory so an active-set change only regroups what's already in
        // memory; the version is the only reactive seam over the plain ListHistory read.
        _ = await Notifications.GetHistoryVersion(Session, cancellationToken).ConfigureAwait(false);
        var query = new NotificationHistoryQuery {
            Kinds = GetHistoryKinds(filterId),
            Limit = Constants.Notification.HistoryMaxLimit,
            IsNewestFirst = true,
        };
        return await Notifications.ListHistory(Session, query, cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    public virtual async Task<bool> HasAnyHistory(CancellationToken cancellationToken = default)
        // The version alone answers this, so the bell never pulls the rows to learn it exists
        => await Notifications.GetHistoryVersion(Session, cancellationToken).ConfigureAwait(false) > 0;

    // Projected to a value-compared record, not handed out as the notification: a Notification's
    // ApiArray members compare by reference, so an unchanged one would re-render every bound row.
    [ComputeMethod(ConsolidationDelay = 0.3)]
    public virtual async Task<ChatNotificationTarget?> GetNavigationTarget(
        ChatId chatId, bool includeReactions, CancellationToken cancellationToken = default)
    {
        var active = await Notifications.ListActive(Session, cancellationToken).ConfigureAwait(false);
        var target = active
            .ListNavigable(chatId)
            .FirstOrDefault(x => includeReactions || x.Kind != NotificationKind.Reaction);
        if (target is null)
            return null;

        // LastEmoji is null on notifications persisted before it existed; the accumulated set is the fallback.
        var emoji = target is ReactionNotification reaction
            ? reaction.LastEmoji ?? reaction.Emojis.LastOrDefault()
            : null;
        return new ChatNotificationTarget(target.Id, target.Kind, target.DismissMode, target.EntryId, emoji);
    }

    [ComputeMethod]
    public virtual async Task<ChatReactionState> GetReactionState(
        ChatId chatId, CancellationToken cancellationToken = default)
    {
        var active = await Notifications.ListActive(Session, cancellationToken).ConfigureAwait(false);
        var newest = active
            .OfType<ReactionNotification>()
            .Where(x => x.ChatId == chatId)
            .MaxBy(x => x.SentAt);
        return newest is null
            ? default
            // LastEmoji is null on notifications persisted before it existed; the accumulated set is the fallback.
            : new ChatReactionState(newest.LastEmoji ?? newest.Emojis.LastOrDefault(), newest.SentAt);
    }

    [ComputeMethod]
    public virtual async Task<IReadOnlyList<ReactionNotification>> ListChatReactions(
        ChatId chatId, CancellationToken cancellationToken = default)
    {
        var active = await Notifications.ListActive(Session, cancellationToken).ConfigureAwait(false);
        return active
            .OfType<ReactionNotification>()
            .Where(x => x.ChatId == chatId)
            .OrderBy(x => x.EntryLid)
            .ToList();
    }

    [ComputeMethod]
    public virtual async Task<ApiArray<ChatId>> ListReactedChatIds(CancellationToken cancellationToken = default)
    {
        var active = await Notifications.ListActive(Session, cancellationToken).ConfigureAwait(false);
        return active
            .OfType<ReactionNotification>()
            .Select(x => x.ChatId)
            .Distinct()
            .ToApiArray();
    }

    [ComputeMethod]
    public virtual async Task<Moment?> GetAttentionAt(ChatId chatId, CancellationToken cancellationToken = default)
    {
        var active = await Notifications.ListActive(Session, cancellationToken).ConfigureAwait(false);
        return active
            .OfType<AttentionNotification>()
            .Where(x => x.ChatId == chatId)
            .Max(x => (Moment?)x.SentAt);
    }

    [ComputeMethod]
    public virtual async Task<ApiArray<ChatId>> ListAttentionChatIds(CancellationToken cancellationToken = default)
    {
        var active = await Notifications.ListActive(Session, cancellationToken).ConfigureAwait(false);
        return active
            .OfType<AttentionNotification>()
            .Select(x => x.ChatId)
            .Distinct()
            .ToApiArray();
    }

    // Private methods

    private static ApiArray<NotificationKind> GetHistoryKinds(Symbol filterId)
    {
        if (filterId == ChatListFilter.UnreadMentions.Id)
            return ApiArray.New(NotificationKind.Mention, NotificationKind.Attention);
        if (filterId == ReactionsFilterId)
            return ApiArray.New(NotificationKind.Reaction);

        return ApiArray<NotificationKind>.Empty;
    }
}

public readonly record struct ChatReactionState(Emoji? Emoji, Moment SentAt);

/// <summary>
/// The notification a notifications-panel row is bound to: enough of it to link, badge and
/// dismiss it, and value-compared throughout.
/// </summary>
public sealed record ChatNotificationTarget(
    NotificationId Id,
    NotificationKind Kind,
    NotificationDismissMode DismissMode,
    ChatEntryId EntryId,
    Emoji? Emoji);

/// <summary>
/// A notifications-panel history row: a chat, its newest past notification of the tab's kinds,
/// and how many older ones the row stands for.
/// </summary>
public sealed record NotificationHistoryGroup(ChatId ChatId, NotificationHistoryItem Newest, int OlderCount);
