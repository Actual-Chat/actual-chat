using ActualChat.Live;
using ActualChat.Video;

namespace ActualChat.Streaming;

public partial class LiveVideoBackend
{
    public sealed class ChatState(LiveVideoBackend owner, ChatId chatId)
    {
        private readonly Lock _codecLock = new();

        // Codec recommendation
        private ApiArray<string> _currentSupportedDecoderCodecs = new(["av1", "hevc", "h264"]);
        private CpuTimestamp _lastCodecDowngradeAt;

        // Priority queue state (in-memory, not Redis)
        private readonly Dictionary<AuthorId, Moment> _lastAudioActivityAt = new();
        private readonly HashSet<string> _pausedStreamIds = new(); // StreamId.Value strings

        public LiveVideoBackend Owner { get; } = owner;
        public ChatId ChatId { get; } = chatId;

        public ApiArray<string> GetCurrentSupportedDecoderCodecs()
        {
            lock (_codecLock)
                return _currentSupportedDecoderCodecs;
        }

        public bool ShouldPause(StreamId streamId)
        {
            lock (_codecLock)
                return _pausedStreamIds.Contains(streamId.Value);
        }

        public void RecomputeCodecs(Dictionary<string, ApiArray<string>> members)
        {
            lock (_codecLock)
                RecomputeSupportedDecoderCodecsLocked(members);
        }

        public void EvaluatePriority(
            ApiArray<VideoStreamInfo> videoStreams,
            ApiArray<LiveStreamInfo> audioStreams,
            MomentClockSet clocks)
        {
            lock (_codecLock)
                EvaluatePriorityLocked(videoStreams, audioStreams, clocks);
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

            // Delay switching UP (h264→hevc, h264→av1, hevc→av1)
            var codecRank = new Dictionary<string, int> { ["h264"] = 0, ["hevc"] = 1, ["av1"] = 2 };
            var currentRank = codecRank.GetValueOrDefault(currentPrimary, 0);
            var newRank = codecRank.GetValueOrDefault(newPrimary, 0);

            if (newRank > currentRank) {
                var elapsed = _lastCodecDowngradeAt.Elapsed;
                if (elapsed < Constants.Video.CodecSwitchHysteresisWindow)
                    return; // Not enough time since last downgrade
            }

            // Track downgrade timing
            if (newRank < currentRank)
                _lastCodecDowngradeAt = CpuTimestamp.Now;

            _currentSupportedDecoderCodecs = newCodecs;
        }

        private static ApiArray<string> ComputeSupportedDecoderCodecsLocked(Dictionary<string, ApiArray<string>> members)
        {
            if (members.Count == 0)
                return new ApiArray<string>(["av1", "hevc", "h264"]); // No viewers, all codecs available

            var allSupportAv1 = true;
            var allSupportHevc = true;
            foreach (var (_, codecs) in members) {
                if (codecs.All(codec => codec != "av1"))
                    allSupportAv1 = false;
                if (codecs.All(codec => codec != "hevc"))
                    allSupportHevc = false;
                if (!allSupportAv1 && !allSupportHevc)
                    break;
            }

            var result = new List<string>();
            if (allSupportAv1) result.Add("av1");
            if (allSupportHevc) result.Add("hevc");
            result.Add("h264"); // always available
            return new ApiArray<string>(result.ToArray());
        }

        private void EvaluatePriorityLocked(
            ApiArray<VideoStreamInfo> videoStreams,
            ApiArray<LiveStreamInfo> audioStreams,
            MomentClockSet clocks)
        {
            var webcamStreams = videoStreams.Where(s => s.StreamKind == StreamKind.Webcam).ToList();

            // Below threshold — unpause all
            if (webcamStreams.Count < Constants.Video.PriorityActivationThreshold) {
                if (_pausedStreamIds.Count == 0)
                    return;

                var toInvalidate = _pausedStreamIds.ToList();
                _pausedStreamIds.Clear();
                foreach (var id in toInvalidate)
                    Owner.InvalidateShouldPause(ChatId, StreamId.Parse(id));
                return;
            }

            // Build set of currently speaking author IDs
            var speakingAuthorIds = audioStreams.Select(s => s.AuthorId).ToHashSet();

            // Update last-activity timestamps for authors currently speaking
            var now = clocks.ServerClock.Now;
            foreach (var stream in webcamStreams) {
                if (speakingAuthorIds.Contains(stream.AuthorId))
                    _lastAudioActivityAt[stream.AuthorId] = now;
                else
                    _lastAudioActivityAt.TryAdd(stream.AuthorId, default);
            }

            // Rank: currently speaking first, then by recency of last speech
            var ranked = webcamStreams
                .OrderByDescending(s => speakingAuthorIds.Contains(s.AuthorId) ? 1 : 0)
                .ThenByDescending(s => _lastAudioActivityAt.GetValueOrDefault(s.AuthorId))
                .ToList();

            // Compute new paused set
            var newPaused = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < ranked.Count; i++) {
                if (i < Constants.Video.PriorityActivationThreshold)
                    continue; // Always active

                if (i >= Constants.Video.MaxWebcamStreamsPerChat) {
                    newPaused.Add(ranked[i].StreamId.Value);
                    continue;
                }

                // Between threshold and cap: pause if not speaking and grace expired
                var isSpeaking = speakingAuthorIds.Contains(ranked[i].AuthorId);
                if (isSpeaking)
                    continue;

                var lastActivity = _lastAudioActivityAt.GetValueOrDefault(ranked[i].AuthorId);
                var withinGrace = lastActivity != default
                    && (now - lastActivity) < Constants.Video.SilenceGracePeriod;
                if (!withinGrace)
                    newPaused.Add(ranked[i].StreamId.Value);
            }

            // 1. Collect streams whose pause state changed
            var changedIds = newPaused
                .Where(id => !_pausedStreamIds.Contains(id)) // newly paused
                .Concat(_pausedStreamIds.Where(id => !newPaused.Contains(id)))  // newly unpaused
                .ToList();

            // 2. Update state
            _pausedStreamIds.Clear();
            _pausedStreamIds.UnionWith(newPaused);

            // 3. Invalidate — subscribers will read the updated state
            foreach (var id in changedIds)
                Owner.InvalidateShouldPause(ChatId, StreamId.Parse(id));
        }
    }
}
