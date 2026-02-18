using ActualChat.Video;

namespace ActualChat.Streaming;

public partial class LiveVideoBackend
{
    public sealed class PeerLatencyState
    {
        private readonly Queue<float> _samples = new();
        private readonly Lock _lock = new();
        private int _gopCounter;

        public float MedianLatencyMs { get; private set; }
        public int GopSkipRatio { get; set; } // 0=none, 1=skip every other GOP, 2=skip 2 of 3

        public void RecordLatency(float latencyMs)
        {
            lock (_lock) {
                _samples.Enqueue(latencyMs);
                while (_samples.Count > Constants.Video.LatencyHistorySize)
                    _samples.Dequeue();

                // Compute median
                var sorted = _samples.OrderBy(x => x).ToList();
                var mid = sorted.Count / 2;
                MedianLatencyMs = sorted.Count % 2 == 0
                    ? (sorted[mid - 1] + sorted[mid]) / 2f
                    : sorted[mid];
            }
        }

        public bool ShouldSkipNextGop()
        {
            if (GopSkipRatio <= 0)
                return false;

            var counter = Interlocked.Increment(ref _gopCounter);
            // ratio=1 → skip every other (skip when counter%2==0)
            // ratio=2 → skip 2 of 3 (skip when counter%3!=0)
            return GopSkipRatio switch {
                1 => counter % 2 == 0,
                2 => counter % 3 != 0,
                _ => false,
            };
        }
    }

    public sealed class StreamLatencyState(ILogger log)
    {
        private readonly ConcurrentDictionary<string, PeerLatencyState> _peers = new(StringComparer.Ordinal);
        private readonly AsyncObservable<VideoQualityPreset> _qualityDirectives = new();
        private readonly Lock _evaluationLock = new();

        private VideoQualityLevel _currentQuality = VideoQualityLevel.High;
        private CpuTimestamp _lastQualityChangeAt = CpuTimestamp.Now;
        private CpuTimestamp _lastEvaluationAt;

        public VideoQualityLevel CurrentQuality => _currentQuality;

        public void RecordPeerLatency(string peerId, float latencyMs)
        {
            var peer = _peers.GetOrAdd(peerId, _ => new PeerLatencyState());
            peer.RecordLatency(latencyMs);
            log.LogDebug("RecordPeerLatency: PeerId={PeerId}, LatencyMs={LatencyMs:F0}, MedianMs={MedianMs:F0}",
                peerId, latencyMs, peer.MedianLatencyMs);

            // Throttle evaluation to QualityDecisionInterval
            if (_lastEvaluationAt.Elapsed >= Constants.Video.QualityDecisionInterval)
                EvaluateQuality();
        }

        public bool ShouldSkipGopsForPeer(string peerId)
        {
            if (!_peers.TryGetValue(peerId, out var peer))
                return false;
            return peer.ShouldSkipNextGop();
        }

        public async IAsyncEnumerable<VideoQualityPreset> ObserveQualityDirectives(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var subscription = _qualityDirectives.Subscribe();
            await using var _ = subscription.ConfigureAwait(false);

            // Emit current quality as the first directive
            yield return VideoQualityPreset.ForLevel(_currentQuality);

            await foreach (var preset in subscription.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return preset;
        }

        private void EvaluateQuality()
        {
            lock (_evaluationLock) {
                _lastEvaluationAt = CpuTimestamp.Now;

                var peers = _peers.ToList();
                if (peers.Count == 0)
                    return;

                var slowCount = peers.Count(p => p.Value.MedianLatencyMs > Constants.Video.HighLatencyThresholdMs);
                var slowRatio = (float)slowCount / peers.Count;

                // Step down sender quality if majority are slow
                if (slowRatio > Constants.Video.PeerOutlierRatio) {
                    var stepped = VideoQualityPreset.StepDown(_currentQuality);
                    if (stepped != null) {
                        var oldQuality = _currentQuality;
                        _currentQuality = stepped.Level;
                        _lastQualityChangeAt = CpuTimestamp.Now;
                        _qualityDirectives.Publish(stepped);
                        log.LogInformation("EvaluateQuality: STEP DOWN {OldLevel} -> {NewLevel}, slowRatio={SlowRatio:F2} ({SlowCount}/{TotalCount})",
                            oldQuality, stepped.Level, slowRatio, slowCount, peers.Count);
                    }
                }
                // Step up quality if all peers are fast and hysteresis window has elapsed
                else if (slowCount == 0
                    && _lastQualityChangeAt.Elapsed >= Constants.Video.QualityHysteresisWindow) {
                    var allFast = peers.All(p => p.Value.MedianLatencyMs < Constants.Video.LowLatencyThresholdMs);
                    if (allFast) {
                        var stepped = VideoQualityPreset.StepUp(_currentQuality);
                        if (stepped != null) {
                            var oldQuality = _currentQuality;
                            _currentQuality = stepped.Level;
                            _lastQualityChangeAt = CpuTimestamp.Now;
                            _qualityDirectives.Publish(stepped);
                            log.LogInformation("EvaluateQuality: STEP UP {OldLevel} -> {NewLevel}, all peers fast ({TotalCount} peers)",
                                oldQuality, stepped.Level, peers.Count);
                        }
                    }
                }
                else {
                    log.LogDebug("EvaluateQuality: HOLD at {Level}, slowRatio={SlowRatio:F2} ({SlowCount}/{TotalCount})",
                        _currentQuality, slowRatio, slowCount, peers.Count);
                }

                // Per-peer GOP skipping for individual outliers
                foreach (var (peerId, peer) in peers)
                    if (peer.MedianLatencyMs > Constants.Video.GopSkipThresholdMs) {
                        if (peer.GopSkipRatio == 0) {
                            peer.GopSkipRatio = 1;
                            log.LogInformation("EvaluateQuality: Enable GOP skipping for PeerId={PeerId}, MedianMs={MedianMs:F0}, ratio=1",
                                peerId, peer.MedianLatencyMs);
                        }
                    }
                    else if (peer.MedianLatencyMs < Constants.Video.GopSkipRecoveryMs)
                        if (peer.GopSkipRatio > 0) {
                            peer.GopSkipRatio = 0;
                            log.LogInformation("EvaluateQuality: Disable GOP skipping for PeerId={PeerId}, MedianMs={MedianMs:F0}",
                                peerId, peer.MedianLatencyMs);
                        }
            }
        }

        public void Complete(Exception? error = null)
            => _qualityDirectives.TryComplete(error);
    }

    public sealed class ChatState(LiveVideoBackend owner, ChatId chatId)
    {
        private readonly ConcurrentDictionary<StreamId, VideoStreamInfo> _streams = new();
        private readonly AsyncObservable<VideoStreamInfo> _newStreams = new();
        private readonly HashSet<string> _members = new(StringComparer.Ordinal);
        private readonly Lock _membersLock = new();
        private readonly ConcurrentDictionary<StreamId, StreamLatencyState> _latencyStates = new();

        public LiveVideoBackend Owner { get; } = owner;
        public ChatId ChatId { get; } = chatId;

        public ApiArray<VideoStreamInfo> ListActiveStreams()
            => new(_streams.Values);

        public bool HasStream(StreamId streamId)
            => _streams.ContainsKey(streamId);

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
        {
            if (!_streams.TryRemove(streamId, out _))
                return false;

            // Clean up latency state for the stream
            if (_latencyStates.TryRemove(streamId, out var latencyState))
                latencyState.Complete();

            return true;
        }

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

        public void RecordPeerLatency(StreamId streamId, string peerId, float latencyMs)
        {
            Owner.Log.LogDebug("ChatState.RecordPeerLatency: ChatId={ChatId}, StreamId={StreamId}, PeerId={PeerId}, LatencyMs={LatencyMs:F0}",
                ChatId, streamId, peerId, latencyMs);
            var latencyState = _latencyStates.GetOrAdd(streamId, _ => new StreamLatencyState(Owner.Log));
            latencyState.RecordPeerLatency(peerId, latencyMs);
        }

        public bool ShouldSkipGopsForPeer(StreamId streamId, string peerId)
        {
            if (!_latencyStates.TryGetValue(streamId, out var latencyState))
                return false;
            return latencyState.ShouldSkipGopsForPeer(peerId);
        }

        public async IAsyncEnumerable<VideoQualityPreset> ObserveQualityDirectives(
            StreamId streamId,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var latencyState = _latencyStates.GetOrAdd(streamId, _ => new StreamLatencyState(Owner.Log));

            await foreach (var preset in latencyState.ObserveQualityDirectives(cancellationToken))
                yield return preset;
        }

        public void Complete(Exception? error = null)
        {
            _newStreams.TryComplete(error);
            foreach (var (_, latencyState) in _latencyStates)
                latencyState.Complete(error);
        }
    }
}
