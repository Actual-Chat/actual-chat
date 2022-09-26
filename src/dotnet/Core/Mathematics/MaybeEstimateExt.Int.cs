namespace ActualChat.Mathematics;

public static partial class MaybeEstimateExt
{
    public static MaybeEstimate<int> Sum(this IEnumerable<MaybeEstimate<int>> estimates)
    {
        var estimateList = estimates.ToList();
        if (estimateList.Count == 0)
            return (0, false);

        var sum = estimateList.Sum(x => x.Value);
        var isEstimate = estimateList.Aggregate(false, (x, estimate) => x || estimate.IsEstimate);
        return new MaybeEstimate<int>(sum, isEstimate);
    }
}
