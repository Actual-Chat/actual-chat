namespace ActualChat.Resilience;

/// <summary>
/// Allows <see cref="Limit"/> calls within any <see cref="Window"/>-long interval.
/// </summary>
public sealed record SlidingWindowBudget(int Limit, TimeSpan Window);
