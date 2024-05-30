using ActualChat.Chat;

namespace ActualChat.MLSearch.Indexing.Initializer;

internal interface IInfiniteChatSequence
{
    IAsyncEnumerable<(ChatId, long)> LoadAsync(long minVersion, CancellationToken cancellationToken = default);
}

public class InfiniteChatSequence(
    IMomentClock clock,
    IChatsBackend chats,
    ILogger<InfiniteChatSequence> log
) : IInfiniteChatSequence
{
    public int BatchSize { get; init; } = 100;
    public TimeSpan RetryInterval { get; init; }= TimeSpan.FromSeconds(10);
    public TimeSpan NoChatsIdleInterval { get; init; } = TimeSpan.FromMinutes(1);

    public async IAsyncEnumerable<(ChatId, long)> LoadAsync(
        long minVersion, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (lastChatId, lastVersion) = (ChatId.None, minVersion);
        while (true) {
            cancellationToken.ThrowIfCancellationRequested();

            ApiArray<Chat.Chat> batch;
            try {
                batch = await chats
                    .ListChanged(lastVersion, long.MaxValue, lastChatId, BatchSize, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch(Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                log.LogError(e,
                    "Failed to load a batch of chats of length {Len} in the version range from {MinVersion} to infinity.",
                    BatchSize, minVersion);
                await clock.Delay(RetryInterval, cancellationToken).ConfigureAwait(false);
                continue;
            }
            if (batch.Count==0) {
                await clock.Delay(NoChatsIdleInterval, cancellationToken).ConfigureAwait(false);
            }
            else {
                foreach (var chat in batch) {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return (lastChatId, lastVersion) = (chat.Id, chat.Version);
                }
            }
        }
    }
}
