using ActualChat.Notifications;

namespace ActualChat.Testing.Host;

public sealed record ApnsPttWakeMessage(
    ChatId ChatId,
    Moment StartedAt,
    string ChatTitle,
    IReadOnlyList<Symbol> DeviceIds);

public sealed record ApnsCallRingMessage(
    ConversationId ConversationId,
    AuthorId Caller,
    string CallerName,
    bool HasVideo,
    IReadOnlyList<Symbol> DeviceIds);

// Replaces IApnsClient in test hosts: records every PTT wake / call ring instead of hitting APNs.
public sealed class ApnsTestSink(ILogger<ApnsTestSink> log) : IApnsClient
{
    private readonly ConcurrentQueue<ApnsPttWakeMessage> _wakes = new();
    private readonly ConcurrentQueue<ApnsCallRingMessage> _callRings = new();

    public IReadOnlyList<ApnsPttWakeMessage> Wakes => _wakes.ToArray();
    public IReadOnlyList<ApnsCallRingMessage> CallRings => _callRings.ToArray();

    public void Clear()
    {
        _wakes.Clear();
        _callRings.Clear();
    }

    public Task SendPttWake(
        ChatId chatId,
        Moment startedAt,
        string chatTitle,
        IReadOnlyCollection<Symbol> deviceIds,
        CancellationToken cancellationToken)
    {
        log.LogInformation("SendPttWake: chat {ChatId} -> {DeviceCount} device(s)", chatId, deviceIds.Count);
        _wakes.Enqueue(new ApnsPttWakeMessage(chatId, startedAt, chatTitle, [..deviceIds]));
        return Task.CompletedTask;
    }

    public Task SendCallRing(
        ConversationId conversationId,
        AuthorId caller,
        string callerName,
        bool hasVideo,
        IReadOnlyCollection<Symbol> deviceIds,
        CancellationToken cancellationToken)
    {
        log.LogInformation("SendCallRing: conversation {ConversationId} -> {DeviceCount} device(s)",
            conversationId, deviceIds.Count);
        _callRings.Enqueue(
            new ApnsCallRingMessage(conversationId, caller, callerName, hasVideo, [..deviceIds]));
        return Task.CompletedTask;
    }
}
