using ActualChat.Video;

namespace ActualChat.Streaming;

public partial class LiveVideoBackend
{
    public sealed class ChatState(LiveVideoBackend owner, ChatId chatId)
    {
        private readonly ConcurrentDictionary<StreamId, VideoStreamInfo> _streams = new();
        private readonly AsyncObservable<VideoStreamInfo> _newStreams = new();
        private readonly Dictionary<string, ApiArray<string>> _members = new(StringComparer.Ordinal);
        private readonly Lock _membersLock = new();

        // Codec recommendation
        private readonly AsyncObservable<string> _recommendedCodecChanges = new();
        private string _currentRecommendedCodec = "av1";
        private CpuTimestamp _lastCodecDowngradeAt;

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

        public string GetRecommendedCodec()
        {
            lock (_membersLock)
                return _currentRecommendedCodec;
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

        public async IAsyncEnumerable<string> ObserveRecommendedCodec(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var subscription = _recommendedCodecChanges.Subscribe();
            await using var _ = subscription.ConfigureAwait(false);

            // Yield current value first
            string currentCodec;
            lock (_membersLock)
                currentCodec = _currentRecommendedCodec;
            yield return currentCodec;

            await foreach (var codec in subscription.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return codec;
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

        public bool RegisterMember(string sessionId, ApiArray<string> supportedDecoderCodecs)
        {
            lock (_membersLock) {
                if (_members.TryGetValue(sessionId, out var existing) && existing.SequenceEqual(supportedDecoderCodecs))
                    return false; // No change
                _members[sessionId] = supportedDecoderCodecs;
                RecomputeRecommendedCodecLocked();
                return true;
            }
        }

        public bool UnregisterMember(string sessionId)
        {
            lock (_membersLock) {
                if (!_members.Remove(sessionId))
                    return false;
                RecomputeRecommendedCodecLocked();
                return true;
            }
        }

        public void Complete(Exception? error = null)
        {
            _newStreams.TryComplete(error);
            _recommendedCodecChanges.TryComplete(error);
        }

        // Must be called under _membersLock
        private void RecomputeRecommendedCodecLocked()
        {
            var newCodec = ComputeRecommendedCodecLocked();
            if (newCodec == _currentRecommendedCodec)
                return;

            // Hysteresis: delay switching UP to AV1 by CodecSwitchHysteresisWindow
            if (newCodec == "av1" && _currentRecommendedCodec == "h264") {
                var elapsed = _lastCodecDowngradeAt.Elapsed;
                if (elapsed < Constants.Video.CodecSwitchHysteresisWindow)
                    return; // Not enough time since last downgrade
            }

            // Track downgrade timing
            if (newCodec == "h264" && _currentRecommendedCodec == "av1")
                _lastCodecDowngradeAt = CpuTimestamp.Now;

            _currentRecommendedCodec = newCodec;
            _recommendedCodecChanges.Publish(newCodec);
        }

        // Must be called under _membersLock
        private string ComputeRecommendedCodecLocked()
        {
            if (_members.Count == 0)
                return "av1"; // No viewers, use best codec

            // Check if all members support AV1 decoding
            foreach (var (_, codecs) in _members) {
                var supportsAv1 = false;
                foreach (var codec in codecs) {
                    if (OrdinalEquals(codec, "av1")) {
                        supportsAv1 = true;
                        break;
                    }
                }
                if (!supportsAv1)
                    return "h264";
            }

            return "av1";
        }
    }
}
