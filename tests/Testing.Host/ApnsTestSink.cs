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
    // Mirrors the real client: an unconfigured one sends nothing and reports nothing delivered.
    public bool IsConfigured { get; set; } = true;
    public bool MustFailCallRings { get; set; }

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

    public Task<IReadOnlySet<Symbol>> SendCallRing(
        ConversationId conversationId,
        AuthorId caller,
        string callerName,
        bool hasVideo,
        IReadOnlyCollection<Symbol> deviceIds,
        CancellationToken cancellationToken)
    {
        if (MustFailCallRings)
            throw new InvalidOperationException("APNs call ring failed");

        log.LogInformation("SendCallRing: conversation {ConversationId} -> {DeviceCount} device(s)",
            conversationId, deviceIds.Count);
        if (!IsConfigured)
            return Task.FromResult<IReadOnlySet<Symbol>>(new HashSet<Symbol>());

        _callRings.Enqueue(
            new ApnsCallRingMessage(conversationId, caller, callerName, hasVideo, [..deviceIds]));
        return Task.FromResult<IReadOnlySet<Symbol>>(deviceIds.ToHashSet());
    }
}
