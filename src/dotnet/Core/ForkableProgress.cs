namespace ActualChat;

/// <summary>
/// A progress reporter that can be forked into multiple child progress reporters.
/// Each child reports progress in its own 0-100 range, which is automatically
/// scaled and offset to contribute to the parent's progress range.
/// </summary>
/// <example>
/// var progress = new ForkableProgress(pct => _uploadPct.Value = pct);
///
/// // Fork into equal parts for each chat
/// var chatForks = progress.Fork(chatIds.Count);
/// for (var i = 0; i &lt; chatIds.Count; i++)
///     await ProcessChat(chatIds[i], chatForks[i], ct);
///
/// // Fork with custom weights (20% transcoding, 80% upload)
/// var (transcodingProgress, uploadProgress) = progress.Fork(0.2, 0.8);
/// </example>
public sealed class ForkableProgress : IProgress<double>
{
    private readonly Action<double> _report;
    private readonly double _baseProgress;
    private readonly double _weight;

    /// <summary>
    /// Creates a root ForkableProgress that reports progress from 0 to 100.
    /// </summary>
    /// <param name="report">Action to receive progress updates (0-100).</param>
    public ForkableProgress(Action<double> report)
        : this(report, 0, 100)
    { }

    private ForkableProgress(Action<double> report, double baseProgress, double weight)
    {
        _report = report;
        _baseProgress = baseProgress;
        _weight = weight;
    }

    /// <summary>
    /// Reports progress within this fork's range.
    /// The value should be 0-100 and will be scaled to the fork's portion of the parent's range.
    /// </summary>
    /// <param name="value">Progress value from 0 to 100.</param>
    public void Report(double value)
        => _report(_baseProgress + (value * _weight / 100));

    /// <summary>
    /// Forks this progress into N equal parts.
    /// Each fork will contribute equally to the parent's progress.
    /// </summary>
    /// <param name="count">Number of forks to create.</param>
    /// <returns>Array of child progress reporters.</returns>
    public ForkableProgress[] Fork(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(count, 0);

        var weightPerFork = _weight / count;
        var forks = new ForkableProgress[count];
        for (var i = 0; i < count; i++)
            forks[i] = new ForkableProgress(_report, _baseProgress + (i * weightPerFork), weightPerFork);
        return forks;
    }

    /// <summary>
    /// Forks this progress into parts with custom weights.
    /// Weights are relative and will be normalized to sum to this fork's weight.
    /// </summary>
    /// <param name="weights">Relative weights for each fork.</param>
    /// <returns>Array of child progress reporters.</returns>
    /// <example>
    /// // 20% for transcoding, 80% for upload
    /// var forks = progress.Fork(0.2, 0.8);
    /// var transcodingProgress = forks[0];
    /// var uploadProgress = forks[1];
    /// </example>
    public ForkableProgress[] Fork(params double[] weights)
    {
        if (weights.Length == 0)
            throw new ArgumentException("At least one weight is required.", nameof(weights));

        var totalWeight = weights.Sum();
        if (totalWeight <= 0)
            throw new ArgumentException("Total weight must be positive.", nameof(weights));

        var forks = new ForkableProgress[weights.Length];
        var currentBase = _baseProgress;
        for (var i = 0; i < weights.Length; i++) {
            var normalizedWeight = _weight * weights[i] / totalWeight;
            forks[i] = new ForkableProgress(_report, currentBase, normalizedWeight);
            currentBase += normalizedWeight;
        }
        return forks;
    }

    /// <summary>
    /// Forks this progress into two parts with custom weights.
    /// </summary>
    /// <returns>Tuple of two child progress reporters.</returns>
    public (ForkableProgress First, ForkableProgress Second) Fork(double weight1, double weight2)
    {
        var forks = Fork([weight1, weight2]);
        return (forks[0], forks[1]);
    }

    /// <summary>
    /// Forks this progress into three parts with custom weights.
    /// </summary>
    /// <returns>Tuple of three child progress reporters.</returns>
    public (ForkableProgress First, ForkableProgress Second, ForkableProgress Third) Fork(
        double weight1,
        double weight2,
        double weight3)
    {
        var forks = Fork([weight1, weight2, weight3]);
        return (forks[0], forks[1], forks[2]);
    }
}
