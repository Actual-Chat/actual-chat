using ActualChat.Notifications;

namespace ActualChat.Testing.Host;

public sealed record ApnsPttWakeMessage(
    ChatId ChatId,
    Moment StartedAt,
    string ChatTitle,
    IReadOnlyList<Symbol> DeviceIds);

// Replaces IApnsClient in test hosts: records every PTT wake instead of hitting APNs.
public sealed class ApnsTestSink(ILogger<ApnsTestSink> log) : IApnsClient
{
    private readonly ConcurrentQueue<ApnsPttWakeMessage> _wakes = new();

    public IReadOnlyList<ApnsPttWakeMessage> Wakes => _wakes.ToArray();

    public void Clear()
        => _wakes.Clear();

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
}
