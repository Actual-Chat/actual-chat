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

        // Priority queue state
        private readonly Dictionary<AuthorId, CpuTimestamp> _lastAudioActivityAt = new();
        private readonly HashSet<string> _pausedStreamIds = new(); // StreamId.Value strings

        public LiveVideoBackend Owner { get; } = owner;
        public ChatId ChatId { get; } = chatId;

        public ApiArray<string> GetCurrentSupportedDecoderCodecs()
        {
            lock (_codecLock)
                return _currentSupportedDecoderCodecs;
        }

        public void RecomputeCodecs(Dictionary<string, ApiArray<string>> members)
        {
            lock (_codecLock)
                RecomputeSupportedDecoderCodecsLocked(members);
        }

        public bool IsStreamPaused(StreamId streamId)
        {
            lock (_codecLock)
                return _pausedStreamIds.Contains(streamId.Value);
        }

        public void EvaluatePriority(
            ApiArray<VideoStreamInfo> videoStreams,
            ApiArray<LiveStreamInfo> audioStreams)
        {
            lock (_codecLock)
                EvaluatePriorityLocked(videoStreams, audioStreams);
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
            ApiArray<LiveStreamInfo> audioStreams)
        {
            var webcamStreams = videoStreams.Where(s => s.StreamKind == StreamKind.Webcam).ToList();

            // Below threshold — clear all pauses
            if (webcamStreams.Count < Constants.Video.PriorityActivationThreshold) {
                if (_pausedStreamIds.Count > 0) {
                    _pausedStreamIds.Clear();
                    Owner.InvalidateIsStreamPaused(ChatId);
                }
                return;
            }

            // Build set of currently speaking author IDs
            var speakingAuthorIds = audioStreams.Select(s => s.AuthorId).ToHashSet();

            // Update last-activity timestamps for authors currently speaking
            var now = CpuTimestamp.Now;
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

            // Top N are active, rest are paused
            var maxActive = Constants.Video.MaxWebcamStreamsPerChat;
            var changed = false;
            for (var i = 0; i < ranked.Count; i++) {
                var streamIdValue = ranked[i].StreamId.Value;
                if (i < maxActive) {
                    var isSpeaking = speakingAuthorIds.Contains(ranked[i].AuthorId);
                    var lastActivity = _lastAudioActivityAt.GetValueOrDefault(ranked[i].AuthorId);
                    var withinGrace = lastActivity != default
                        && lastActivity.Elapsed < Constants.Video.SilenceGracePeriod;

                    if (i < Constants.Video.PriorityActivationThreshold || isSpeaking || withinGrace) {
                        if (_pausedStreamIds.Remove(streamIdValue))
                            changed = true;
                    } else {
                        if (_pausedStreamIds.Add(streamIdValue))
                            changed = true;
                    }
                } else {
                    if (_pausedStreamIds.Add(streamIdValue))
                        changed = true;
                }
            }

            // Clean up entries for streams no longer active
            var activeStreamIds = webcamStreams.Select(s => s.StreamId.Value).ToHashSet();
            _pausedStreamIds.RemoveWhere(id => !activeStreamIds.Contains(id));

            if (changed)
                Owner.InvalidateIsStreamPaused(ChatId);
        }
    }
}
