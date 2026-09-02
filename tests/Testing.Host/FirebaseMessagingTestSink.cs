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
    public IReadOnlyList<string> ActiveTags { get; init; } = [];
    public long ActiveVersion { get; init; }
    public bool IsDismissal => Notification == null;
}

public sealed record FirebaseBadgeMessage(IReadOnlyList<Symbol> DeviceIds, int BadgeCount);

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
    private readonly ConcurrentQueue<FirebaseBadgeMessage> _badges = new();
    private int _rejectedDismissals;

    public IReadOnlyList<FirebaseSentMessage> Messages => _messages.ToArray();
    public IReadOnlyList<FirebaseWakeMessage> Wakes => _wakes.ToArray();
    public IReadOnlyList<FirebaseBadgeMessage> Badges => _badges.ToArray();
    // Makes the next N dismissal sends throw, so a test can assert that a failed send leaves the
    // dismissal owed rather than losing it.
    public int FailDismissalCount { get; set; }
    // Makes the next N dismissal sends accept nothing instead of throwing - how FCM reports a
    // rejected push.
    public int RejectDismissalCount { get; set; }
    // Lets a test wait for the rejection instead of racing the converge the dismissal queues.
    public int RejectedDismissals => Volatile.Read(ref _rejectedDismissals);
    public event Action<FirebaseSentMessage>? Sent;

    public void Clear()
    {
        _messages.Clear();
        _wakes.Clear();
        _badges.Clear();
        Interlocked.Exchange(ref _rejectedDismissals, 0);
    }

    public Task SendMessage(
        Notification notification,
        IReadOnlyCollection<Symbol> deviceIds,
        bool? enableDataCollection,
        UserNotificationInfo info,
        bool isSilent,
        CancellationToken cancellationToken)
    {
        var badgeCount = info.Items.Count;
        log.LogInformation("SendMessage: {NotificationId} -> {DeviceCount} device(s), badge={Badge}, silent={IsSilent}",
            notification.Id, deviceIds.Count, badgeCount, isSilent);
        Add(new FirebaseSentMessage(notification, [], [..deviceIds], badgeCount, isSilent) {
            ActiveTags = [..info.Items.Select(n => n.GetPushTag()).SkipNullItems().Distinct()],
            ActiveVersion = info.Version,
        });
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<PendingDismissal>> SendDismissal(
        IReadOnlyCollection<PendingDismissal> dismissals,
        IReadOnlyCollection<Symbol> deviceIds,
        int badgeCount,
        CancellationToken cancellationToken)
    {
        if (FailDismissalCount > 0) {
            FailDismissalCount--;
            log.LogInformation("SendDismissal: injected failure");
            throw new InvalidOperationException("Injected dismissal failure.");
        }
        if (RejectDismissalCount > 0) {
            RejectDismissalCount--;
            Interlocked.Increment(ref _rejectedDismissals);
            log.LogInformation("SendDismissal: injected rejection");
            return Task.FromResult<IReadOnlyCollection<PendingDismissal>>([]);
        }

        log.LogInformation("SendDismissal: {Count} id(s) -> {DeviceCount} device(s), badge={Badge}",
            dismissals.Count, deviceIds.Count, badgeCount);
        Add(new FirebaseSentMessage(null, [..dismissals.Select(x => x.Id)], [..deviceIds], badgeCount));
        return Task.FromResult<IReadOnlyCollection<PendingDismissal>>([..dismissals]);
    }

    public Task SendBadge(
        IReadOnlyCollection<Symbol> deviceIds,
        int badgeCount,
        CancellationToken cancellationToken)
    {
        log.LogInformation("SendBadge: {DeviceCount} device(s), badge={Badge}", deviceIds.Count, badgeCount);
        if (deviceIds.Count > 0)
            _badges.Enqueue(new FirebaseBadgeMessage([..deviceIds], badgeCount));
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
