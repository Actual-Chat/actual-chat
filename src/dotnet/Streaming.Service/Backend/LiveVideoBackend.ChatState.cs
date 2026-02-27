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
        private readonly AsyncObservable<ApiArray<string>> _supportedDecoderCodecChanges = new();
        private ApiArray<string> _currentSupportedDecoderCodecs = new(["av1", "h264"]);
        private CpuTimestamp _lastCodecDowngradeAt;

        public LiveVideoBackend Owner { get; } = owner;
        public ChatId ChatId { get; } = chatId;

        public ApiArray<VideoStreamInfo> ListActiveStreams()
            => new(_streams.Values);

        public ApiArray<AuthorId> GetStreamingAuthorIds()
        {
            if (_streams.IsEmpty)
                return default;

            return _streams.Values
                .Select(s => s.AuthorId)
                .Distinct()
                .ToApiArray();
        }

        public int GetMemberCount()
        {
            lock (_membersLock)
                return _members.Count;
        }

        public ApiArray<string> GetSupportedDecoderCodecs()
        {
            lock (_membersLock)
                return _currentSupportedDecoderCodecs;
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

        public async IAsyncEnumerable<ApiArray<string>> ObserveSupportedDecoderCodecs(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var subscription = _supportedDecoderCodecChanges.Subscribe();
            await using var _ = subscription.ConfigureAwait(false);

            // Yield current value first
            ApiArray<string> currentCodecs;
            lock (_membersLock)
                currentCodecs = _currentSupportedDecoderCodecs;
            yield return currentCodecs;

            await foreach (var codecs in subscription.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return codecs;
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
                RecomputeSupportedDecoderCodecsLocked();
                return true;
            }
        }

        public bool UnregisterMember(string sessionId)
        {
            lock (_membersLock) {
                if (!_members.Remove(sessionId))
                    return false;
                RecomputeSupportedDecoderCodecsLocked();
                return true;
            }
        }

        public void Complete(Exception? error = null)
        {
            _newStreams.TryComplete(error);
            _supportedDecoderCodecChanges.TryComplete(error);
        }

        // Must be called under _membersLock
        private void RecomputeSupportedDecoderCodecsLocked()
        {
            var newCodecs = ComputeSupportedDecoderCodecsLocked();
            if (_currentSupportedDecoderCodecs.SequenceEqual(newCodecs))
                return;

            // Hysteresis: compare primary (first) codec for up/downgrade timing
            var currentPrimary = _currentSupportedDecoderCodecs.Count > 0 ? _currentSupportedDecoderCodecs[0] : "h264";
            var newPrimary = newCodecs.Count > 0 ? newCodecs[0] : "h264";

            // Delay switching UP to AV1 by CodecSwitchHysteresisWindow
            if (OrdinalEquals(newPrimary, "av1") && OrdinalEquals(currentPrimary, "h264")) {
                var elapsed = _lastCodecDowngradeAt.Elapsed;
                if (elapsed < Constants.Video.CodecSwitchHysteresisWindow)
                    return; // Not enough time since last downgrade
            }

            // Track downgrade timing
            if (OrdinalEquals(newPrimary, "h264") && OrdinalEquals(currentPrimary, "av1"))
                _lastCodecDowngradeAt = CpuTimestamp.Now;

            _currentSupportedDecoderCodecs = newCodecs;
            _supportedDecoderCodecChanges.Publish(newCodecs);
        }

        // Must be called under _membersLock
        private ApiArray<string> ComputeSupportedDecoderCodecsLocked()
        {
            if (_members.Count == 0)
                return new(["av1", "h264"]); // No viewers, all codecs available

            // Check if all members support AV1 decoding
            var allSupportAv1 = true;
            foreach (var (_, codecs) in _members) {
                var supportsAv1 = false;
                foreach (var codec in codecs) {
                    if (OrdinalEquals(codec, "av1")) {
                        supportsAv1 = true;
                        break;
                    }
                }
                if (!supportsAv1) {
                    allSupportAv1 = false;
                    break;
                }
            }

            return allSupportAv1
                ? new(["av1", "h264"])  // All support AV1 — priority: av1 > h264
                : new(["h264"]);         // Not all support AV1 — h264 only
        }
    }
}
