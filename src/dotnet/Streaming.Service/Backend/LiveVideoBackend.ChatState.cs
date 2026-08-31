namespace ActualChat.Streaming;

public partial class LiveVideoBackend
{
    public sealed class ChatState(LiveVideoBackend owner, ChatId chatId)
    {
        // The codec every client is required to decode, and so the only one the
        // intersection may never drop. Mirrors FLOOR_CATEGORY in
        // codec-support.ts; why it is VP9 is in docs/live-video/03-codecs-and-layers.md.
        public const string FloorCodec = "vp9";

        // Not a codec: a marker a client puts in its advertised list to say the
        // list is a deliberate override rather than a capability report. Honoured
        // only for admins; stripped and ignored for everyone else.
        public const string ForcedCodecMarker = "forced";

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

        public void RecomputeCodecs(Dictionary<string, VideoStreamMemberInfo> members)
        {
            lock (_codecLock)
                RecomputeSupportedDecoderCodecs(members);
        }

        // Private methods

        // Must be called under _codecLock
        private void RecomputeSupportedDecoderCodecs(Dictionary<string, VideoStreamMemberInfo> members)
        {
            var (newCodecs, isForced) = ComputeSupportedDecoderCodecs(members);
            // Compared as a set: the list carries no order, so a reshuffle is
            // not a change.
            if (_currentSupportedDecoderCodecs.Count == newCodecs.Count
                && !newCodecs.Except(_currentSupportedDecoderCodecs, StringComparer.Ordinal).Any())
                return;

            // Hysteresis works on the best codec the set contains, not on any
            // position in it: which codec a sender ends up using is the sender's
            // decision, but "how good is the best option available" is a property
            // of the set, and that is what flaps as members come and go.
            var currentBest = BestCodecQuality(_currentSupportedDecoderCodecs);
            var newBest = BestCodecQuality(newCodecs);

            // Delay widening, so a member leaving and rejoining doesn't restart
            // everyone's encoder twice. An admin override skips the wait: it is
            // an explicit instruction, not a capability that happened to change.
            if (!isForced
                && newBest > currentBest
                && _lastCodecDowngradeAt.Elapsed < Constants.Video.CodecSwitchHysteresisWindow)
                return;

            if (newBest < currentBest)
                _lastCodecDowngradeAt = CpuTimestamp.Now;

            _currentSupportedDecoderCodecs = newCodecs;
        }

        // Compression quality, higher is better. Times the hysteresis above and
        // nothing else - it is not a priority any client sees.
        private static int CodecQuality(string codec)
            => codec switch {
                "av1" => 3,
                "hevc" => 2,
                "vp9" => 1,
                _ => 0,
            };

        private static int BestCodecQuality(ApiArray<string> codecs)
        {
            var best = 0;
            foreach (var codec in codecs)
                best = Math.Max(best, CodecQuality(codec));
            return best;
        }

        private static (ApiArray<string> Codecs, bool IsForced) ComputeSupportedDecoderCodecs(
            Dictionary<string, VideoStreamMemberInfo> members)
        {
            if (members.Count == 0)
                return (new ApiArray<string>(["av1", "hevc", "vp9", "h264"]), false); // No viewers, all codecs available

            // An admin advertising the marker is overriding the negotiation, not
            // reporting what it can play: its codecs become the call's list as-is,
            // with no intersection and no floor. Several admins overriding at once
            // UNION their picks — each is asking for their own codec to be used,
            // and intersecting would leave nothing.
            var forced = members.Values
                .Where(m => m.IsAdmin && m.SupportedDecoderCodecs.Contains(ForcedCodecMarker, StringComparer.Ordinal))
                .SelectMany(m => m.SupportedDecoderCodecs)
                .Where(codec => !string.Equals(codec, ForcedCodecMarker, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (forced.Length > 0)
                return (new ApiArray<string>(forced), true);

            var allSupportAv1 = true;
            var allSupportHevc = true;
            var allSupportH264 = true;
            foreach (var (_, info) in members) {
                // A non-admin's marker carries no authority; the rest of its list
                // is still an honest capability report.
                var codecs = info.SupportedDecoderCodecs;
                if (codecs.All(codec => codec != "av1"))
                    allSupportAv1 = false;
                if (codecs.All(codec => codec != "hevc"))
                    allSupportHevc = false;
                if (codecs.All(codec => codec != "h264"))
                    allSupportH264 = false;
                if (!allSupportAv1 && !allSupportHevc && !allSupportH264)
                    break;
            }

            // A set, not a ranking: a codec is either decodable by every member
            // or it is not. Which one a sender uses is the sender's decision,
            // made from its own encoder ladder.
            var result = new List<string>();
            if (allSupportAv1) result.Add("av1");
            if (allSupportHevc) result.Add("hevc");
            result.Add(FloorCodec); // see FloorCodec
            if (allSupportH264) result.Add("h264");
            return (new ApiArray<string>(result.ToArray()), false);
        }
    }
}
