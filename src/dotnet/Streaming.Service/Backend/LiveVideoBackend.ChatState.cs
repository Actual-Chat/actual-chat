using System.Collections.Frozen;
using ActualChat.Live;

namespace ActualChat.Streaming;

public partial class LiveVideoBackend
{
    public sealed class ChatState(LiveVideoBackend owner, ChatId chatId)
    {
        private readonly Lock _codecLock = new();

        // Codec recommendation
        private ApiArray<string> _currentSupportedDecoderCodecs = new(["av1", "hevc", "vp9", "h264"]);
        private CpuTimestamp _lastCodecDowngradeAt;

        // Priority queue state (in-memory, not Redis)
        private readonly Dictionary<AuthorId, Moment> _lastAudioActivityAt = new();
        private volatile FrozenSet<string> _pausedStreamIds = FrozenSet<string>.Empty;

        public LiveVideoBackend Owner { get; } = owner;
        public ChatId ChatId { get; } = chatId;

        public ApiArray<string> CurrentSupportedDecoderCodecs {
            get {
                lock (_codecLock)
                    return _currentSupportedDecoderCodecs;
            }
        }

        // Lock-free: reads an immutable FrozenSet snapshot
        public bool ShouldPause(StreamId streamId)
            => _pausedStreamIds.Contains(streamId.Value);

        public void RecomputeCodecs(Dictionary<string, ApiArray<string>> members)
        {
            lock (_codecLock)
                RecomputeSupportedDecoderCodecs(members);
        }

        public void EvaluatePriority(
            ApiArray<VideoStreamInfo> videoStreams,
            ApiArray<LiveStreamInfo> audioStreams,
            MomentClockSet clocks)
        {
            List<string> changedIds;
            lock (_codecLock)
                changedIds = EvaluatePriorityLocked(videoStreams, audioStreams, clocks);

            // Invalidate OUTSIDE lock — no longer blocks ShouldPause readers
            foreach (var id in changedIds)
                Owner.InvalidateShouldPause(ChatId, StreamId.Parse(id));
        }

        // Private methods

        // Must be called under _codecLock
        private void RecomputeSupportedDecoderCodecs(Dictionary<string, ApiArray<string>> members)
        {
            var newCodecs = ComputeSupportedDecoderCodecs(members);
            if (_currentSupportedDecoderCodecs.SequenceEqual(newCodecs))
                return;

            // Hysteresis: compare primary (first) codec for up/downgrade timing
            var currentPrimary = _currentSupportedDecoderCodecs.Count > 0 ? _currentSupportedDecoderCodecs[0] : "h264";
            var newPrimary = newCodecs.Count > 0 ? newCodecs[0] : "h264";

            // Delay switching UP (h264→hevc, h264→av1, hevc→av1)
            var codecRank = new Dictionary<string, int> { ["h264"] = 0, ["vp9"] = 1, ["hevc"] = 2, ["av1"] = 3 };
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

        private static ApiArray<string> ComputeSupportedDecoderCodecs(Dictionary<string, ApiArray<string>> members)
        {
            if (members.Count == 0)
                return new ApiArray<string>(["av1", "hevc", "vp9", "h264"]); // No viewers, all codecs available

            var allSupportAv1 = true;
            var allSupportHevc = true;
            var allSupportVp9 = true;
            foreach (var (_, codecs) in members) {
                if (codecs.All(codec => codec != "av1"))
                    allSupportAv1 = false;
                if (codecs.All(codec => codec != "hevc"))
                    allSupportHevc = false;
                if (codecs.All(codec => codec != "vp9"))
                    allSupportVp9 = false;
                if (!allSupportAv1 && !allSupportHevc && !allSupportVp9)
                    break;
            }

            var result = new List<string>();
            if (allSupportAv1) result.Add("av1");
            if (allSupportHevc) result.Add("hevc");
            if (allSupportVp9) result.Add("vp9");
            result.Add("h264"); // always available
            return new ApiArray<string>(result.ToArray());
        }

        // Returns list of changed stream IDs for invalidation (done outside lock by caller)
        private List<string> EvaluatePriorityLocked(
            ApiArray<VideoStreamInfo> videoStreams,
            ApiArray<LiveStreamInfo> audioStreams,
            MomentClockSet clocks)
        {
            var webcamStreams = videoStreams.Where(s => s.StreamKind == StreamKind.Webcam).ToList();
            var currentPaused = _pausedStreamIds;

            // Below threshold — unpause all
            if (webcamStreams.Count < Constants.Video.PriorityActivationThreshold) {
                if (currentPaused.Count == 0)
                    return [];

                var toInvalidate = currentPaused.ToList();
                _pausedStreamIds = FrozenSet<string>.Empty;
                return toInvalidate;
            }

            // Build set of currently speaking author IDs
            var speakingAuthorIds = audioStreams.Select(s => s.AuthorId).ToHashSet();

            // Update last-activity timestamps for authors currently speaking
            var now = clocks.ServerClock.Now;
            foreach (var stream in webcamStreams)
                if (speakingAuthorIds.Contains(stream.AuthorId))
                    _lastAudioActivityAt[stream.AuthorId] = now;
                else
                    _lastAudioActivityAt.TryAdd(stream.AuthorId, default);

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

            // Collect streams whose pause state changed
            var changedIds = newPaused
                .Where(id => !currentPaused.Contains(id)) // newly paused
                .Concat(currentPaused.Where(id => !newPaused.Contains(id)))  // newly unpaused
                .ToList();

            // Atomic swap — ShouldPause readers see old or new, never torn
            _pausedStreamIds = newPaused.ToFrozenSet(StringComparer.Ordinal);

            return changedIds;
        }
    }
}
