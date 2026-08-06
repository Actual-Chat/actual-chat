using ActualChat.Notifications;
using Notification = ActualChat.Notifications.Notification;

namespace ActualChat.Testing.Host;

public sealed record FirebaseSentMessage(
    Notification? Notification,
    IReadOnlyList<NotificationId> DismissedIds,
    IReadOnlyList<Symbol> DeviceIds,
    int BadgeCount,
    bool IsSilent = false)
{
    public bool IsDismissal => Notification == null;
}

public sealed record FirebaseWakeMessage(
    ChatId ChatId,
    AuthorId AuthorId,
    Moment StartedAt,
    IReadOnlyList<Symbol> DeviceIds);

// Replaces IFirebaseMessagingClient in test hosts: records every push instead of
// hitting Firebase, so tests can assert on what would have been sent.
public sealed class FirebaseMessagingTestSink(ILogger<FirebaseMessagingTestSink> log) : IFirebaseMessagingClient
{
    private readonly ConcurrentQueue<FirebaseSentMessage> _messages = new();
    private readonly ConcurrentQueue<FirebaseWakeMessage> _wakes = new();

    public IReadOnlyList<FirebaseSentMessage> Messages => _messages.ToArray();
    public IReadOnlyList<FirebaseWakeMessage> Wakes => _wakes.ToArray();
    public event Action<FirebaseSentMessage>? Sent;

    public void Clear()
    {
        _messages.Clear();
        _wakes.Clear();
    }

    public Task SendMessage(
        Notification notification,
        IReadOnlyCollection<Symbol> deviceIds,
        bool? enableDataCollection,
        int badgeCount,
        bool isSilent,
        CancellationToken cancellationToken)
    {
        log.LogInformation("SendMessage: {NotificationId} -> {DeviceCount} device(s), badge={Badge}, silent={IsSilent}",
            notification.Id, deviceIds.Count, badgeCount, isSilent);
        Add(new FirebaseSentMessage(notification, [], [..deviceIds], badgeCount, isSilent));
        return Task.CompletedTask;
    }

    public Task SendDismissal(
        IReadOnlyCollection<Notification> dismissedNotifications,
        IReadOnlyCollection<Symbol> deviceIds,
        int badgeCount,
        CancellationToken cancellationToken)
    {
        log.LogInformation("SendDismissal: {Count} id(s) -> {DeviceCount} device(s), badge={Badge}",
            dismissedNotifications.Count, deviceIds.Count, badgeCount);
        Add(new FirebaseSentMessage(null, [..dismissedNotifications.Select(n => n.Id)], [..deviceIds], badgeCount));
        return Task.CompletedTask;
    }

    public Task SendSpeechStartedWake(
        ChatId chatId,
        AuthorId authorId,
        Moment startedAt,
        IReadOnlyCollection<Symbol> deviceIds,
        CancellationToken cancellationToken)
    {
        log.LogInformation("SendSpeechStartedWake: chat {ChatId} -> {DeviceCount} device(s)",
            chatId, deviceIds.Count);
        _wakes.Enqueue(new FirebaseWakeMessage(chatId, authorId, startedAt, [..deviceIds]));
        return Task.CompletedTask;
    }

    private void Add(FirebaseSentMessage message)
    {
        _messages.Enqueue(message);
        Sent?.Invoke(message);
    }
}
