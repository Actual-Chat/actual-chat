namespace ActualChat;

/// <summary>
/// Extension methods for <see cref="IProgress{T}"/> that enable forking.
/// </summary>
public static class ProgressExt
{
    /// <summary>
    /// Forks this progress into N equal parts.
    /// Each fork will contribute equally to the parent's progress.
    /// </summary>
    /// <param name="progress">The progress to fork.</param>
    /// <param name="count">Number of forks to create.</param>
    /// <returns>Array of child progress reporters.</returns>
    public static ForkableProgress[] Fork(this IProgress<double> progress, int count)
        => progress.AsForkable().Fork(count);

    /// <summary>
    /// Forks this progress into parts with custom weights.
    /// Weights are relative and will be normalized.
    /// </summary>
    /// <param name="progress">The progress to fork.</param>
    /// <param name="weights">Relative weights for each fork.</param>
    /// <returns>Array of child progress reporters.</returns>
    public static ForkableProgress[] Fork(this IProgress<double> progress, params double[] weights)
        => progress.AsForkable().Fork(weights);

    /// <summary>
    /// Forks this progress into two parts with custom weights.
    /// </summary>
    /// <param name="progress">The progress to fork.</param>
    /// <param name="weight1">Weight for first fork.</param>
    /// <param name="weight2">Weight for second fork.</param>
    /// <returns>Tuple of two child progress reporters.</returns>
    public static (ForkableProgress First, ForkableProgress Second) Fork(
        this IProgress<double> progress,
        double weight1,
        double weight2)
        => progress.AsForkable().Fork(weight1, weight2);

    /// <summary>
    /// Forks this progress into three parts with custom weights.
    /// </summary>
    /// <param name="progress">The progress to fork.</param>
    /// <param name="weight1">Weight for first fork.</param>
    /// <param name="weight2">Weight for second fork.</param>
    /// <param name="weight3">Weight for third fork.</param>
    /// <returns>Tuple of three child progress reporters.</returns>
    public static (ForkableProgress First, ForkableProgress Second, ForkableProgress Third) Fork(
        this IProgress<double> progress,
        double weight1,
        double weight2,
        double weight3)
        => progress.AsForkable().Fork(weight1, weight2, weight3);

    /// <summary>
    /// Converts an IProgress to ForkableProgress, reusing it if already a ForkableProgress.
    /// </summary>
    private static ForkableProgress AsForkable(this IProgress<double> progress)
        => progress as ForkableProgress ?? new ForkableProgress(progress.Report);
}
