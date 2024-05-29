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
        while (true) {
            cancellationToken.ThrowIfCancellationRequested();

            ApiArray<Chat.Chat> batch;
            try {
                // TODO: handle the case with infinite getting the same chats in a batch
                // when there are more than BatchSize chats with the same version
                batch = await chats
                    .ListChanged(minVersion, long.MaxValue, ChatId.None, BatchSize, cancellationToken)
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
                    minVersion = chat.Version + 1;
                    yield return (chat.Id, chat.Version);
                }
            }
        }
    }
}
