namespace ActualChat.Notifications;

/// <summary>
/// What clears a notification once it's shown. Orthogonal to
/// <see cref="Notification.ExpiresAt"/>, which is a backstop for all three.
/// </summary>
public enum NotificationDismissMode
{
    // Nothing but an explicit dismissal or expiry. The default on purpose: a kind that forgets to
    // declare a mode stays visible until something acts on it, rather than being silently cleared
    // by a read anchor that may not mean what it looks like.
    Explicit = 0,
    // The user's Read position passed the notification's entry.
    OnRead,
    // The notification's entry was actually on screen. Reactions anchor at the recipient's own
    // message, which is read the moment it's sent, so OnRead would clear them before delivery.
    OnView,
}
