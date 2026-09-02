using ActualChat.Streaming;

namespace ActualChat.UI.Blazor.App.Services;

public sealed record StreamAllocationRequest(
    string StreamId,
    IReadOnlyList<long> PredictedRatesByLayer,
    int LayerCountCap,
    double RenderArea)
{
    public int EffectiveLayerCountCap => Math.Clamp(LayerCountCap, 1, Math.Max(1, PredictedRatesByLayer.Count));
}

/// <summary>
/// Priority-based downstream allocator. Reserves a floor for every secondary
/// stream first, splits what's left equally across the primaries and gives each
/// the largest layer fitting its share, spends any leftover on whichever
/// primaries can still use it, then distributes the remainder across secondaries
/// proportional to their <c>RenderArea</c>.
/// Output: per-stream <see cref="ReceiveQuality"/>. Streams that don't fit
/// even at floor are omitted; the caller maps that to <see cref="ReceiveQuality.Lowest"/>.
/// </summary>
public static class VideoQualityAllocator
{
    /// <summary>
    /// Allocates per-stream <see cref="ReceiveQuality"/> envelopes within
    /// <paramref name="budgetBytesPerSec"/>. The optional
    /// <paramref name="decoderLayerCap"/> dictionary caps a per-stream
    /// allocation when the receiver's decoder can't keep up: the picked
    /// layer is clamped to <c>min(LayerCountCap, decoderLayerCap[streamId] + 1)</c>
    /// so a slow decoder demotes only its own viewer without dragging the
    /// shared downlink BWE down. Streams absent from the cap dict are
    /// unconstrained.
    /// </summary>
    public static IReadOnlyDictionary<string, ReceiveQuality> Allocate(
        long budgetBytesPerSec,
        IReadOnlyList<StreamAllocationRequest> primaries,
        IReadOnlyList<StreamAllocationRequest> secondaries,
        IReadOnlyDictionary<string, int>? decoderLayerCap = null)
    {
        var result = new Dictionary<string, ReceiveQuality>();

        primaries = ApplyDecoderCap(primaries, decoderLayerCap);
        secondaries = ApplyDecoderCap(secondaries, decoderLayerCap);

        long floorBudget = 0;
        foreach (var s in secondaries)
            floorBudget += FloorRateOf(s);

        var primaryBudget = Math.Max(0, budgetBytesPerSec - floorBudget);
        long primariesUsed = 0;
        // Equal shares first, so tile order doesn't decide who gets quality. The
        // equal-tile layout makes every visible stream primary; a purely greedy
        // pass there would hand the whole budget to whoever came first in the
        // dictionary and leave the last tiles on the floor. With a single primary
        // the share IS the whole budget, so the common layout is unaffected.
        var primaryShare = primaries.Count > 0 ? primaryBudget / primaries.Count : 0;
        var pickedRates = new Dictionary<string, long>(primaries.Count);
        foreach (var p in primaries) {
            var (layers, rate) = PickBestFit(p, primaryShare, minimumFit: false);
            if (rate < 0)
                continue;
            result[p.StreamId] = ToReceiveQuality(layers);
            pickedRates[p.StreamId] = rate;
            primariesUsed += rate;
        }
        // Then spend what the shares left over — a stream whose next layer is
        // cheap should not be held at its share while the budget sits unused.
        foreach (var p in primaries) {
            var current = pickedRates.GetValueOrDefault(p.StreamId, 0L);
            var (layers, rate) = PickBestFit(p, current + (primaryBudget - primariesUsed), minimumFit: false);
            if (rate <= current)
                continue;

            result[p.StreamId] = ToReceiveQuality(layers);
            pickedRates[p.StreamId] = rate;
            primariesUsed += rate - current;
        }

        var remaining = Math.Max(0, budgetBytesPerSec - primariesUsed);
        double totalArea = 0;
        foreach (var s in secondaries)
            totalArea += Math.Max(0, s.RenderArea);

        foreach (var s in secondaries) {
            long share;
            if (totalArea <= 0) {
                share = remaining / Math.Max(1, secondaries.Count);
            }
            else {
                var ratio = Math.Max(0, s.RenderArea) / totalArea;
                share = (long)(remaining * ratio);
            }
            share = Math.Max(share, FloorRateOf(s));

            var (layers, _) = PickBestFit(s, share, minimumFit: true);
            result[s.StreamId] = ToReceiveQuality(layers);
        }

        return result;
    }

    private static IReadOnlyList<StreamAllocationRequest> ApplyDecoderCap(
        IReadOnlyList<StreamAllocationRequest> requests,
        IReadOnlyDictionary<string, int>? decoderLayerCap)
    {
        if (decoderLayerCap is null || decoderLayerCap.Count == 0)
            return requests;
        var out_ = new List<StreamAllocationRequest>(requests.Count);
        foreach (var r in requests) {
            if (decoderLayerCap.TryGetValue(r.StreamId, out var capLayerId)) {
                var capLayerCount = Math.Max(1, capLayerId + 1);
                if (capLayerCount < r.LayerCountCap) {
                    out_.Add(r with { LayerCountCap = capLayerCount });
                    continue;
                }
            }
            out_.Add(r);
        }
        return out_;
    }

    private static long FloorRateOf(StreamAllocationRequest s)
    {
        if (s.PredictedRatesByLayer.Count == 0)
            return 0;
        return s.PredictedRatesByLayer[0];
    }

    private static (int layers, long rate) PickBestFit(
        StreamAllocationRequest s,
        long budget,
        bool minimumFit)
    {
        if (s.PredictedRatesByLayer.Count == 0)
            return (1, minimumFit ? 0 : -1);

        var bestLayers = 1;
        long bestRate = -1;

        for (var layers = 1; layers <= s.EffectiveLayerCountCap; layers++) {
            var rate = s.PredictedRatesByLayer[layers - 1];
            if (rate > budget)
                continue;
            if (rate > bestRate) {
                bestRate = rate;
                bestLayers = layers;
            }
        }

        if (bestRate < 0) {
            if (!minimumFit)
                return (0, -1);
            return (1, s.PredictedRatesByLayer[0]);
        }
        return (bestLayers, bestRate);
    }

    private static ReceiveQuality ToReceiveQuality(int layers)
        => new(layers - 1);
}
