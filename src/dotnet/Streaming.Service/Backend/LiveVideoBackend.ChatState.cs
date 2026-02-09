namespace ActualChat.Streaming;

public partial class LiveVideoBackend
{
    public sealed class ChatState(LiveVideoBackend owner, ChatId chatId)
    {
        private readonly ConcurrentDictionary<StreamId, VideoStreamInfo> _streams = new();
        private readonly AsyncObservable<VideoStreamInfo> _newStreams = new();
        private readonly HashSet<string> _members = new(StringComparer.Ordinal);
        private readonly Lock _membersLock = new();

        public LiveVideoBackend Owner { get; } = owner;
        public ChatId ChatId { get; } = chatId;

        public ApiArray<VideoStreamInfo> ListActiveStreams()
            => new(_streams.Values);

        public AuthorId[] GetStreamingAuthorIds()
        {
            if (_streams.IsEmpty)
                return [];

            return _streams.Values
                .Select(s => s.AuthorId)
                .Distinct()
                .ToArray();
        }

        public int GetMemberCount()
        {
            lock (_membersLock)
                return _members.Count;
        }

        public async IAsyncEnumerable<VideoStreamInfo> ObserveStreams(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var subscription = _newStreams.Subscribe();
            await using var _ = subscription.ConfigureAwait(false);

            var initialStreams = _streams.Values.ToList();
            var dedupeEndsAt = CpuTimestamp.Now + TimeSpan.FromSeconds(5);

            foreach (var stream in initialStreams)
                yield return stream;

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

        public bool RegisterStream(VideoStreamInfo streamInfo)
        {
            if (!_streams.TryAdd(streamInfo.StreamId, streamInfo))
                return false;

            _newStreams.Publish(streamInfo);
            return true;
        }

        public bool UnregisterStream(StreamId streamId)
            => _streams.TryRemove(streamId, out _);

        public bool RegisterMember(string sessionId)
        {
            lock (_membersLock)
                return _members.Add(sessionId);
        }

        public bool UnregisterMember(string sessionId)
        {
            lock (_membersLock) {
                if (!_members.Remove(sessionId))
                    return false;
                return true;
            }
        }

        public void Complete(Exception? error = null)
            => _newStreams.TryComplete(error);
    }
}
