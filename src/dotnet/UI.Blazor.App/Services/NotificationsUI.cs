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
