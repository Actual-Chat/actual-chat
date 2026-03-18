using ActualChat.Live;

namespace ActualChat.Streaming;

public partial class LiveAudioBackend
{
    public sealed class ChatState(LiveAudioBackend owner, ChatId chatId)
    {
        private readonly AsyncObservable<LiveStreamInfo> _newStreams = new();

        private LiveAudioBackend Owner { get; } = owner;
        private ChatId ChatId { get; } = chatId;

        public void PublishNewStream(LiveStreamInfo stream)
            => _newStreams.Publish(stream);

        public async IAsyncEnumerable<LiveStreamInfo> ObserveStreams(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            // Subscribe first to avoid missing streams registered between snapshot and subscription
            var subscription = _newStreams.Subscribe();
            await using var _ = subscription.ConfigureAwait(false);

            // Snapshot current streams from Redis
            var currentStreams = await Owner.ListActiveStreams(ChatId, cancellationToken).ConfigureAwait(false);
            var initialStreams = currentStreams.ToList();
            var dedupeEndsAt = CpuTimestamp.Now + TimeSpan.FromSeconds(5);

            // Yield all currently active streams
            foreach (var stream in initialStreams)
                yield return stream;

            // Yield new streams, filtering duplicates within the dedupe window
            await foreach (var stream in subscription.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false)) {
                if (initialStreams != null) {
                    if (CpuTimestamp.Now > dedupeEndsAt)
                        initialStreams = null;
                    else if (initialStreams.Exists(x => x.StreamId == stream.StreamId))
                        continue;
                }
                yield return stream;
            }
        }

        public void Complete(Exception? error = null)
            => _newStreams.TryComplete(error);
    }
}
