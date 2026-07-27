namespace ActualChat.Resilience;

/// <summary>
/// Per-<see cref="RateLimitClass"/>, per-<see cref="RateLimitIdentityKind"/> call budgets.
/// A missing entry means the dimension isn't charged for that class.
/// </summary>
public sealed record RateLimitBudgets
{
    public static readonly RateLimitBudgets Default = new();

    public IReadOnlyDictionary<(RateLimitClass, RateLimitIdentityKind), SlidingWindowBudget> Items { get; init; }
        = new Dictionary<(RateLimitClass, RateLimitIdentityKind), SlidingWindowBudget> {
            // ~100 calls/s per session, and controller requests only: RPC reads aren't charged
            [(RateLimitClass.HttpRead, RateLimitIdentityKind.Session)] = new(6_000, TimeSpan.FromMinutes(1)),
            [(RateLimitClass.Command, RateLimitIdentityKind.Session)] = new(1_200, TimeSpan.FromMinutes(1)),
            [(RateLimitClass.Command, RateLimitIdentityKind.UserId)] = new(2_400, TimeSpan.FromMinutes(1)),
            // Sign-in & invite acceptance: a few calls per attempt, a handful of attempts per user
            [(RateLimitClass.Auth, RateLimitIdentityKind.Session)] = new(30, TimeSpan.FromMinutes(5)),
            [(RateLimitClass.Auth, RateLimitIdentityKind.UserId)] = new(30, TimeSpan.FromMinutes(5)),
            [(RateLimitClass.Auth, RateLimitIdentityKind.IP)] = new(600, TimeSpan.FromMinutes(5)),
            [(RateLimitClass.Auth, RateLimitIdentityKind.Target)] = new(30, TimeSpan.FromMinutes(5)),

            [(RateLimitClass.SessionCreation, RateLimitIdentityKind.UserId)] = new(100, TimeSpan.FromDays(1)),
            [(RateLimitClass.SessionCreation, RateLimitIdentityKind.IP)] = new(100_000, TimeSpan.FromDays(1)),

            [(RateLimitClass.GifProvider, RateLimitIdentityKind.UserId)] = new(1_000, TimeSpan.FromHours(1)),
            [(RateLimitClass.GifProvider, RateLimitIdentityKind.IP)] = new(100_000, TimeSpan.FromHours(1)),
        };

    public SlidingWindowBudget? Get(RateLimitClass rateLimitClass, RateLimitIdentityKind kind)
        => Items.GetValueOrDefault((rateLimitClass, kind));
}
