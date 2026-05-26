using ActualChat.Streaming;

namespace ActualChat.UI.Blazor.App.Services;

internal sealed class DecoderCapState
{
    private readonly Dictionary<string, HealthVerdict> _lastVerdict = new();
    private readonly Dictionary<string, int> _caps = new();

    public IReadOnlyDictionary<string, int> Caps => _caps;

    public int? OnVerdict(string streamId, HealthVerdict verdict, int requestedLayerCount)
    {
        var prev = _lastVerdict.GetValueOrDefault(streamId, HealthVerdict.Unknown);
        if (verdict == HealthVerdict.Bad && prev != HealthVerdict.Bad) {
            var currentLayer = Math.Max(0, requestedLayerCount - 1);
            _caps[streamId] = Math.Max(0, currentLayer - 1);
        }
        else if (verdict == HealthVerdict.Good) {
            _caps.Remove(streamId);
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
