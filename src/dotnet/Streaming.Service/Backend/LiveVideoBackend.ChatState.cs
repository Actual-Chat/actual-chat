namespace ActualChat.Streaming;

public partial class LiveVideoBackend
{
    public sealed class ChatState(LiveVideoBackend owner, ChatId chatId)
    {
        // The codec every client is required to decode, and so the only one the
        // intersection may never drop. Mirrors FLOOR_CATEGORY in
        // codec-support.ts; why it is VP9 is in docs/plans/codec-negotiation.md.
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

        // Efficiency order, best-first; unknown codecs sort last.
        private static int CodecPriority(string codec)
            => codec switch {
                "av1" => 0,
                "hevc" => 1,
                "vp9" => 2,
                "h264" => 3,
                _ => 4,
            };

        private static ApiArray<string> ComputeSupportedDecoderCodecs(Dictionary<string, VideoStreamMemberInfo> members)
        {
            if (members.Count == 0)
                return new ApiArray<string>(["av1", "hevc", "vp9", "h264"]); // No viewers, all codecs available

            // An admin advertising the marker is overriding the negotiation, not
            // reporting what it can play: its codecs become the call's list as-is,
            // with no intersection and no floor. Several admins overriding at once
            // UNION their picks — each is asking for their own codec to be used,
            // and intersecting would leave nothing.
            var pinned = members.Values
                .Where(m => m.IsAdmin && m.SupportedDecoderCodecs.Contains(ForcedCodecMarker, StringComparer.Ordinal))
                .SelectMany(m => m.SupportedDecoderCodecs)
                .Where(codec => !string.Equals(codec, ForcedCodecMarker, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (pinned.Count > 0)
                return new ApiArray<string>(pinned.OrderBy(CodecPriority).ToArray());

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

            var result = new List<string>();
            if (allSupportAv1) result.Add("av1");
            if (allSupportHevc) result.Add("hevc");
            result.Add(FloorCodec); // see FloorCodec
            if (allSupportH264) result.Add("h264");

            // Members advertise best-first, so order the survivors by the
            // highest rank any member gave them. Senders take the first entry
            // they can encode, which is what lets one member pull the call onto
            // a codec that would otherwise never win: H.264 sits below the
            // floor on efficiency, so without this it was unreachable no matter
            // what anyone asked for.
            var bestRank = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var (_, info) in members) {
                var codecs = info.SupportedDecoderCodecs;
                for (var i = 0; i < codecs.Count; i++) {
                    var codec = codecs[i];
                    if (!bestRank.TryGetValue(codec, out var rank) || i < rank)
                        bestRank[codec] = i;
                }
            }
            return new ApiArray<string>(result
                .OrderBy(c => bestRank.GetValueOrDefault(c, int.MaxValue))
                .ToArray());
        }
    }
}
