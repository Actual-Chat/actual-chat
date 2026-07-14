using ActualChat.Streaming;

namespace ActualChat.UI.Blazor.App.Services;

internal sealed class DecoderCapState
{
    // Step-wise release: every tier raise re-arms the decoder-reconfigure +
    // keyframe cost, so climb one tier per GoodTicksPerRaise consecutive Good
    // verdicts instead of uncapping in one jump — the one-shot release
    // re-triggered the stall and oscillated cap↔uncap on a ~1min cadence.
    private const int GoodTicksPerRaise = 3;

    private readonly Dictionary<string, HealthVerdict> _lastVerdict = new();
    private readonly Dictionary<string, int> _caps = new();
    private readonly Dictionary<string, int> _goodStreaks = new();

    public IReadOnlyDictionary<string, int> Caps => _caps;

    public int? OnVerdict(string streamId, HealthVerdict verdict, int requestedLayerCount)
    {
        var prev = _lastVerdict.GetValueOrDefault(streamId, HealthVerdict.Unknown);
        if (verdict == HealthVerdict.Bad) {
            _goodStreaks.Remove(streamId);
            if (prev != HealthVerdict.Bad) {
                var currentLayer = Math.Max(0, requestedLayerCount - 1);
                _caps[streamId] = Math.Max(0, currentLayer - 1);
            }
        }
        else if (verdict == HealthVerdict.Good && _caps.TryGetValue(streamId, out var cap)) {
            var streak = _goodStreaks.GetValueOrDefault(streamId) + 1;
            if (streak < GoodTicksPerRaise) {
                _goodStreaks[streamId] = streak;
            }
            else {
                _goodStreaks.Remove(streamId);
                cap++;
                if (cap >= requestedLayerCount - 1)
                    _caps.Remove(streamId);
                else
                    _caps[streamId] = cap;
            }
        }
        else if (verdict != HealthVerdict.Good) {
            // Marginal/Unknown hold the cap and reset the raise streak.
            _goodStreaks.Remove(streamId);
        }
        _lastVerdict[streamId] = verdict;
        return _caps.TryGetValue(streamId, out var c) ? c : null;
    }

    public bool HasState(string streamId)
        => _lastVerdict.ContainsKey(streamId) || _caps.ContainsKey(streamId);

    public void PruneStaleStreams(IReadOnlyCollection<string> liveStreamIds)
    {
        var deadVerdictKeys = _lastVerdict.Keys.Where(k => !liveStreamIds.Contains(k)).ToArray();
        foreach (var k in deadVerdictKeys)
            _lastVerdict.Remove(k);
        var deadCapKeys = _caps.Keys.Where(k => !liveStreamIds.Contains(k)).ToArray();
        foreach (var k in deadCapKeys)
            _caps.Remove(k);
    }
}
