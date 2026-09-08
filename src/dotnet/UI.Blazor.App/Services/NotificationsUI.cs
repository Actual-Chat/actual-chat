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
