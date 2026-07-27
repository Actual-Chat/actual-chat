namespace ActualChat.Resilience;

/// <summary>
/// In-process fixed-window limiter: no network, no allocation on the allowed path. It's a node-local
/// approximation - N nodes allow up to N budgets, and a burst spanning a window boundary up to 2x.
/// </summary>
public sealed class LocalRateLimiter<TKey>(SlidingWindowBudget defaultBudget, int maxWindowCount = 100_000)
    : IRateLimiter<TKey, SlidingWindowBudget>
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, Window> _windows = new();

    ValueTask<TimeSpan?> IRateLimiter<TKey>.Check(
        TKey key,
        object? budget,
        CancellationToken cancellationToken)
        => Check(key, (SlidingWindowBudget?)budget, cancellationToken);

    public ValueTask<TimeSpan?> Check(
        TKey key,
        SlidingWindowBudget? budget,
        CancellationToken cancellationToken = default)
        => new(CheckImpl(key, budget ?? defaultBudget));

    // Private methods

    private TimeSpan? CheckImpl(TKey key, SlidingWindowBudget budget)
    {
        var windowMs = (long)budget.Window.TotalMilliseconds;
        var now = Environment.TickCount64;
        if (_windows.Count > maxWindowCount)
            Prune(now, windowMs);

        var window = _windows.GetOrAdd(key, static (_, startedAt) => new Window(startedAt), now);
        lock (window) {
            if (now - window.StartedAt >= windowMs) {
                window.StartedAt = now;
                window.Count = 1;
                return null;
            }
            if (window.Count < budget.Limit) {
                window.Count++;
                return null;
            }
            return TimeSpan.FromMilliseconds(window.StartedAt + windowMs - now);
        }
    }

    private void Prune(long now, long windowMs)
    {
        foreach (var (key, window) in _windows)
            if (now - window.StartedAt >= windowMs)
                _windows.TryRemove(key, out _);
    }

    // Nested types

    private sealed class Window(long startedAt)
    {
        public long StartedAt { get; set; } = startedAt;
        public int Count { get; set; }
    }
}
