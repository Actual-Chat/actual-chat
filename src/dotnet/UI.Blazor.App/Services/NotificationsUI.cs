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
        return SelectByKind(active, kind);
    }

    [ComputeMethod]
    public virtual async Task<ChatReactionState> GetReactionState(
        ChatId chatId, CancellationToken cancellationToken = default)
    {
        var active = await Notifications.ListActive(Session, cancellationToken).ConfigureAwait(false);
        return SelectReactionState(active, chatId);
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
        return SelectAttentionAt(active, chatId);
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

    // Protected/internal methods

    // Internal rather than private so the projections can be tested without a hub.
    internal static ApiArray<Notification> SelectByKind(ApiArray<Notification> active, NotificationKind kind)
        => active
            .Where(x => x.Kind == kind)
            .OrderByDescending(x => x.SentAt)
            .ToApiArray();

    internal static ChatReactionState SelectReactionState(ApiArray<Notification> active, ChatId chatId)
    {
        var newest = active
            .OfType<ReactionNotification>()
            .Where(x => x.ChatId == chatId)
            .MaxBy(x => x.SentAt);
        if (newest is null)
            return default;

        // LastEmoji is null on notifications persisted before it existed; the accumulated set is the fallback.
        return new ChatReactionState(newest.LastEmoji ?? newest.Emojis.LastOrDefault(), newest.SentAt);
    }

    internal static Moment? SelectAttentionAt(ApiArray<Notification> active, ChatId chatId)
        => active
            .OfType<AttentionNotification>()
            .Where(x => x.ChatId == chatId)
            .Select(x => (Moment?)x.SentAt)
            .Max();
}

public readonly record struct ChatReactionState(Emoji? Emoji, Moment SentAt);
