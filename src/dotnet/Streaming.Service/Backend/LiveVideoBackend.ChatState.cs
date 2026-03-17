using ActualChat.Video;

namespace ActualChat.Streaming;

public partial class LiveVideoBackend
{
    public sealed class ChatState(LiveVideoBackend owner, ChatId chatId)
    {
        private readonly AsyncObservable<VideoStreamInfo> _newStreams = new();
        private readonly Lock _codecLock = new();

        // Codec recommendation
        private readonly AsyncObservable<ApiArray<string>> _supportedDecoderCodecChanges = new();
        private ApiArray<string> _currentSupportedDecoderCodecs = new(["av1", "h264"]);
        private CpuTimestamp _lastCodecDowngradeAt;

        public LiveVideoBackend Owner { get; } = owner;
        public ChatId ChatId { get; } = chatId;

        public void PublishNewStream(VideoStreamInfo streamInfo)
            => _newStreams.Publish(streamInfo);

        public ApiArray<string> GetCurrentSupportedDecoderCodecs()
        {
            lock (_codecLock)
                return _currentSupportedDecoderCodecs;
        }

        public void RecomputeAndPublishCodecs(Dictionary<string, ApiArray<string>> members)
        {
            lock (_codecLock)
                RecomputeSupportedDecoderCodecsLocked(members);
        }

        public async IAsyncEnumerable<VideoStreamInfo> ObserveStreams(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var subscription = _newStreams.Subscribe();
            await using var _ = subscription.ConfigureAwait(false);

            // Snapshot current streams from Redis
            var redisStreams = await Owner.ListActiveStreams(ChatId, cancellationToken).ConfigureAwait(false);
            var initialStreams = redisStreams.ToList();
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
            lock (_codecLock)
                currentCodecs = _currentSupportedDecoderCodecs;
            yield return currentCodecs;

            await foreach (var codecs in subscription.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return codecs;
        }

        public void Complete(Exception? error = null)
        {
            _newStreams.TryComplete(error);
            _supportedDecoderCodecChanges.TryComplete(error);
        }

        // Private methods

        // Must be called under _codecLock
        private void RecomputeSupportedDecoderCodecsLocked(Dictionary<string, ApiArray<string>> members)
        {
            var newCodecs = ComputeSupportedDecoderCodecsLocked(members);
            if (_currentSupportedDecoderCodecs.SequenceEqual(newCodecs))
                return;

            // Hysteresis: compare primary (first) codec for up/downgrade timing
            var currentPrimary = _currentSupportedDecoderCodecs.Count > 0 ? _currentSupportedDecoderCodecs[0] : "h264";
            var newPrimary = newCodecs.Count > 0 ? newCodecs[0] : "h264";

            // Delay switching UP to AV1 by CodecSwitchHysteresisWindow
            if (newPrimary == "av1" && currentPrimary == "h264") {
                var elapsed = _lastCodecDowngradeAt.Elapsed;
                if (elapsed < Constants.Video.CodecSwitchHysteresisWindow)
                    return; // Not enough time since last downgrade
            }

            // Track downgrade timing
            if (newPrimary == "h264" && currentPrimary == "av1")
                _lastCodecDowngradeAt = CpuTimestamp.Now;

            _currentSupportedDecoderCodecs = newCodecs;
            _supportedDecoderCodecChanges.Publish(newCodecs);
        }

        private static ApiArray<string> ComputeSupportedDecoderCodecsLocked(Dictionary<string, ApiArray<string>> members)
        {
            if (members.Count == 0)
                return new ApiArray<string>(["av1", "h264"]); // No viewers, all codecs available

            // Check if all members support AV1 decoding
            var allSupportAv1 = true;
            foreach (var (_, codecs) in members) {
                if (codecs.Any(codec => codec == "av1"))
                    continue;

                allSupportAv1 = false;
                break;
            }

            return allSupportAv1
                ? new ApiArray<string>(["av1", "h264"])  // All support AV1 — priority: av1 > h264
                : new ApiArray<string>(["h264"]);         // Not all support AV1 — h264 only
        }
    }
}
