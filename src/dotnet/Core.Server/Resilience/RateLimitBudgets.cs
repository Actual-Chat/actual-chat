namespace ActualChat.Resilience;

/// <summary>
/// Per-<see cref="RateLimitClass"/>, per-<see cref="RateLimitIdentityKind"/> call budgets.
/// A missing entry means the dimension isn't charged for that class.
/// </summary>
public sealed record RateLimitBudgets
{
    public const long UploadByteUnit = 100L * 1024 * 1024;
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

            [(RateLimitClass.UploadCreation, RateLimitIdentityKind.UserId)] = new(10_000, TimeSpan.FromDays(1)),
            [(RateLimitClass.UploadCreation, RateLimitIdentityKind.IP)] = new(100_000, TimeSpan.FromDays(1)),
            [(RateLimitClass.UploadBytes, RateLimitIdentityKind.UserId)] = new(10_486, TimeSpan.FromDays(1)),
            [(RateLimitClass.UploadBytes, RateLimitIdentityKind.IP)] = new(104_858, TimeSpan.FromDays(1)),

            // Every generation is money at an external provider, so this is charged far harder than
            // anything else here: ~10 pictures a day is a few tenths of a cent per user.
            [(RateLimitClass.ImageGeneration, RateLimitIdentityKind.UserId)] = new(10, TimeSpan.FromDays(1)),
            [(RateLimitClass.ImageGeneration, RateLimitIdentityKind.IP)] = new(100, TimeSpan.FromDays(1)),

            // Charged by the incoming web hook endpoint itself: it's a minimal-API route, so the
            // controller-only HTTP rate limit middleware never sees it.
            [(RateLimitClass.WebHookInbound, RateLimitIdentityKind.Target)] = new(60, TimeSpan.FromMinutes(1)),
            [(RateLimitClass.WebHookInbound, RateLimitIdentityKind.UserId)] = new(600, TimeSpan.FromHours(1)),
        };

    public SlidingWindowBudget? Get(RateLimitClass rateLimitClass, RateLimitIdentityKind kind)
        => Items.GetValueOrDefault((rateLimitClass, kind));
}
