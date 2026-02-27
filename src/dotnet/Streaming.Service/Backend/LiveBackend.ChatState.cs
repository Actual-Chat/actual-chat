using ActualChat.Live;

namespace ActualChat.Streaming;

public partial class LiveBackend
{
    public sealed class ChatState(LiveBackend owner, ChatId chatId)
    {
        private readonly ConcurrentDictionary<string, LiveStreamInfo> _streams = new(StringComparer.Ordinal);
        private readonly AsyncObservable<LiveStreamInfo> _newStreams = new();
        private readonly SemaphoreSlim _populateLock = new(1, 1);
        private volatile bool _isPopulated;

        public LiveBackend Owner { get; } = owner;
        public ChatId ChatId { get; } = chatId;

        public async Task<ApiArray<LiveStreamInfo>> ListActiveStreams(CancellationToken cancellationToken)
        {
            await EnsurePopulated(cancellationToken).ConfigureAwait(false);
            return new(_streams.Values);
        }

        public async IAsyncEnumerable<LiveStreamInfo> ObserveStreams(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await EnsurePopulated(cancellationToken).ConfigureAwait(false);

            // Subscribe first to avoid missing streams registered between snapshot and subscription
            var subscription = _newStreams.Subscribe();
            await using var _ = subscription.ConfigureAwait(false);

            // Snapshot current streams and track when we took it
            var initialStreams = _streams.Values.ToList();
            var dedupeEndsAt = CpuTimestamp.Now + TimeSpan.FromSeconds(5);

            // Yield all currently active streams
            foreach (var stream in initialStreams)
                yield return stream;

            // Yield new streams, filtering duplicates within the dedupe window
            await foreach (var stream in subscription.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false)) {
                if (initialStreams != null) {
                    if (CpuTimestamp.Now > dedupeEndsAt)
                        initialStreams = null;
                    else if (initialStreams.Exists(x => OrdinalEquals(x.StreamId, stream.StreamId)))
                        continue;
                }
                yield return stream;
            }
        }

        public async Task<bool> Register(LiveStreamInfo stream, CancellationToken cancellationToken)
        {
            await EnsurePopulated(cancellationToken).ConfigureAwait(false);
            if (!_streams.TryAdd(stream.StreamId, stream))
                return false;

            _newStreams.Publish(stream);
            return true;
        }

        public async Task<bool> Unregister(string streamId, CancellationToken cancellationToken)
        {
            await EnsurePopulated(cancellationToken).ConfigureAwait(false);
            return _streams.TryRemove(streamId, out _);
        }

        public void Complete(Exception? error = null)
            => _newStreams.TryComplete(error);

        // Private methods

        private async ValueTask EnsurePopulated(CancellationToken cancellationToken)
        {
            // Double-checked locking
            if (_isPopulated) return;
            await _populateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try {
                if (_isPopulated) return;

                await Populate(cancellationToken).ConfigureAwait(false);
                _isPopulated = true;
            }
            finally {
                _populateLock.Release();
            }
        }

        private async Task Populate(CancellationToken cancellationToken)
        {
            // Look for entries from the last MaxEntryDuration
            var minBeginsAt = Owner.ServerClock.Now - Constants.Chat.MaxEntryDuration;
            var entries = await Owner.ChatsBackend.ListEntries(ChatId, minBeginsAt, cancellationToken).ConfigureAwait(false);

            // Find entries that are currently streaming
            foreach (var entry in entries) {
                // Text entry with MediaOrStreamId that is not a valid MediaId = live stream
                if (!entry.MediaOrStreamId.IsNullOrEmpty()
                    && !MediaId.TryParse(entry.MediaOrStreamId, out _)) {
                    var streamInfo = new LiveStreamInfo {
                        ChatId = ChatId,
                        AuthorId = entry.AuthorId,
                        StreamId = entry.MediaOrStreamId,
                        BeginsAt = entry.BeginsAt,
                    };
                    _streams.TryAdd(streamInfo.StreamId, streamInfo);
                }
            }
        }
    }
}
