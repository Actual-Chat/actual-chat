using ActualChat.Streaming;

namespace ActualChat.UI.Blazor.App.Services;

public sealed record StreamAllocationRequest(
    string StreamId,
    IReadOnlyList<long> PredictedRatesByLayer,
    int LayerCountCap,
    int ProducerTemporalLayerCount,
    double RenderArea)
{
    public int EffectiveLayerCountCap => Math.Clamp(LayerCountCap, 1, Math.Max(1, PredictedRatesByLayer.Count));
    public int EffectiveTemporalLayerCount => Math.Max(1, ProducerTemporalLayerCount);
}

/// <summary>
/// Priority-based downstream allocator. Reserves a floor for every secondary
/// stream first, then assigns the largest fitting (spatial layers, temporal
/// fraction) pair to each primary, then distributes the remainder across
/// secondaries proportional to their <c>RenderArea</c>.
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
        foreach (var p in primaries) {
            var (layers, frac, rate) = PickBestFit(p, primaryBudget - primariesUsed, minimumFit: false);
            if (rate < 0)
                continue;
            result[p.StreamId] = ToReceiveQuality(p, layers, frac);
            primariesUsed += rate;
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

            var (layers, frac, _) = PickBestFit(s, share, minimumFit: true);
            result[s.StreamId] = ToReceiveQuality(s, layers, frac);
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
        var fullRate = s.PredictedRatesByLayer[0];
        return (long)Math.Ceiling(fullRate * TemporalFloor(s.EffectiveTemporalLayerCount));
    }

    private static (int layers, double fraction, long rate) PickBestFit(
        StreamAllocationRequest s,
        long budget,
        bool minimumFit)
    {
        if (s.PredictedRatesByLayer.Count == 0)
            return (1, TemporalFloor(s.EffectiveTemporalLayerCount), minimumFit ? 0 : -1);

        var bestLayers = 1;
        var bestFrac = TemporalFloor(s.EffectiveTemporalLayerCount);
        long bestRate = -1;

        for (var layers = 1; layers <= s.EffectiveLayerCountCap; layers++) {
            var fullRate = s.PredictedRatesByLayer[layers - 1];
            foreach (var (frac, _) in EnumerateTemporalSteps(s.EffectiveTemporalLayerCount)) {
                var rate = (long)Math.Ceiling(fullRate * frac);
                if (rate > budget)
                    continue;
                if (rate > bestRate || (rate == bestRate && frac > bestFrac)) {
                    bestRate = rate;
                    bestLayers = layers;
                    bestFrac = frac;
                }
            }
        }

        if (bestRate < 0) {
            if (!minimumFit)
                return (0, 0, -1);
            var fullRate = s.PredictedRatesByLayer[0];
            return (1, TemporalFloor(s.EffectiveTemporalLayerCount),
                    (long)Math.Ceiling(fullRate * TemporalFloor(s.EffectiveTemporalLayerCount)));
        }
        return (bestLayers, bestFrac, bestRate);
    }

    private static IEnumerable<(double Fraction, int Count)> EnumerateTemporalSteps(int producerCount)
    {
        var k = Math.Max(1, producerCount);
        for (var c = 1; c <= k; c++) {
            var frac = 1.0 / Math.Pow(2, k - c);
            yield return (frac, c);
        }
    }

    private static double TemporalFloor(int producerCount)
        => 1.0 / Math.Pow(2, Math.Max(0, producerCount - 1));

    private static ReceiveQuality ToReceiveQuality(StreamAllocationRequest s, int layers, double fraction)
    {
        var k = s.EffectiveTemporalLayerCount;
        var keptTemporalLayerCount = Math.Clamp((int)Math.Ceiling(fraction * k), 1, k);
        return new ReceiveQuality(layers - 1, k - keptTemporalLayerCount);
    }
}
