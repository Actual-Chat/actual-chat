namespace ActualChat.Resilience;

/// <summary>
/// Allows <see cref="TokenLimit"/> permits per <see cref="ReplenishmentPeriod"/>, refilled
/// continuously rather than in steps.
/// </summary>
public sealed record TokenBucketBudget(int TokenLimit, TimeSpan ReplenishmentPeriod);
