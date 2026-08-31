namespace ActualChat.Streaming;

public partial class LiveVideoBackend
{
    public sealed class ChatState(LiveVideoBackend owner, ChatId chatId)
    {
        // The codec every client is required to decode, and so the only one the
        // intersection may never drop. It is VP9 rather than H.264 on
        // measurement: VP9 encodes and decodes at real-time latency on every
        // engine the app ships to, while Firefox's H.264 encoder runs ~18
        // frames behind. A client that cannot decode it needs a decoder, not a
        // renegotiation that would cost every other member their camera.
        // Mirrors FLOOR_CATEGORY in codec-support.ts.
        public const string FloorCodec = "vp9";

        private readonly Lock _codecLock = new();

        // Codec recommendation
        private ApiArray<string> _currentSupportedDecoderCodecs = new(["av1", "hevc", "vp9", "h264"]);
        private CpuTimestamp _lastCodecDowngradeAt;

        public LiveVideoBackend Owner { get; } = owner;
        public ChatId ChatId { get; } = chatId;

        public ApiArray<string> CurrentSupportedDecoderCodecs {
            get {
                lock (_codecLock)
                    return _currentSupportedDecoderCodecs;
            }
        }

        public void RecomputeCodecs(Dictionary<string, ApiArray<string>> members)
        {
            lock (_codecLock)
                RecomputeSupportedDecoderCodecs(members);
        }

        // Private methods

        // Must be called under _codecLock
        private void RecomputeSupportedDecoderCodecs(Dictionary<string, ApiArray<string>> members)
        {
            var newCodecs = ComputeSupportedDecoderCodecs(members);
            if (_currentSupportedDecoderCodecs.SequenceEqual(newCodecs))
                return;

            // Hysteresis: compare primary (first) codec for up/downgrade timing
            var currentPrimary = _currentSupportedDecoderCodecs.Count > 0 ? _currentSupportedDecoderCodecs[0] : FloorCodec;
            var newPrimary = newCodecs.Count > 0 ? newCodecs[0] : FloorCodec;

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
            var allSupportH264 = true;
            foreach (var (_, codecs) in members) {
                if (codecs.All(codec => codec != "av1"))
                    allSupportAv1 = false;
                if (codecs.All(codec => codec != "hevc"))
                    allSupportHevc = false;
                if (codecs.All(codec => codec != "h264"))
                    allSupportH264 = false;
                if (!allSupportAv1 && !allSupportHevc && !allSupportH264)
                    break;
            }

            var result = new List<string>();
            if (allSupportAv1) result.Add("av1");
            if (allSupportHevc) result.Add("hevc");
            result.Add(FloorCodec); // see FloorCodec
            if (allSupportH264) result.Add("h264");
            return new ApiArray<string>(result.ToArray());
        }
    }
}
