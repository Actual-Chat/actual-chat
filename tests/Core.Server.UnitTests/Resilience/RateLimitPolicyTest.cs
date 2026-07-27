using ActualChat.Resilience;

namespace ActualChat.Core.Server.UnitTests.Resilience;

public class RateLimitPolicyTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly SlidingWindowBudget OnePerMinute = new(1, TimeSpan.FromMinutes(1));

    [Fact]
    public async Task EveryDimensionIsChargedWhenOneOfThemIsExhausted()
    {
        // arrange
        var sessionLimiter = new TestRateLimiter { ExhaustedAfter = 1 };
        var ipLimiter = new TestRateLimiter();
        var policy = NewPolicy(RateLimitClass.Command, sessionLimiter, ipLimiter);
        var identities = NewIdentities();

        // act
        await policy.Check("Test", RateLimitClass.Command, identities, default);
        var act = () => policy.Check("Test", RateLimitClass.Command, identities, default).AsTask();

        // assert
        var error = await act.Should().ThrowAsync<RateLimitExceededException>();
        error.Which.RetryDelay.Should().Be(TimeSpan.FromSeconds(30));
        sessionLimiter.CheckCount.Should().Be(2);
        ipLimiter.CheckCount.Should().Be(2);
    }

    [Fact]
    public async Task AuthDimensionsAreCheckedConcurrentlyAndCommandsSequentially()
    {
        // arrange
        var authLimiters = NewBlockedLimiters();
        var commandLimiters = NewBlockedLimiters();
        var authPolicy = NewPolicy(RateLimitClass.Auth, authLimiters[0], authLimiters[1]);
        var commandPolicy = NewPolicy(RateLimitClass.Command, commandLimiters[0], commandLimiters[1]);
        var identities = NewIdentities();

        // act
        var authTask = authPolicy.Check("Test", RateLimitClass.Auth, identities, default).AsTask();
        var commandTask = commandPolicy.Check("Test", RateLimitClass.Command, identities, default).AsTask();
        await authLimiters[1].WhenStarted.WaitAsync(TimeSpan.FromSeconds(5));

        // assert
        commandLimiters[1].WhenStarted.IsCompleted.Should().BeFalse();
        foreach (var limiter in authLimiters.Concat(commandLimiters))
            limiter.Unblock();
        await Task.WhenAll(authTask, commandTask).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task LocalRateLimiterAllowsUpToItsBudget()
    {
        // arrange
        var limiter = new LocalRateLimiter<string>(new SlidingWindowBudget(2, TimeSpan.FromMinutes(1)));

        // act
        var first = await limiter.Check("a");
        var second = await limiter.Check("a");
        var third = await limiter.Check("a");
        var otherKey = await limiter.Check("b");

        // assert
        first.Should().BeNull();
        second.Should().BeNull();
        third.Should().NotBeNull();
        third!.Value.Should().BeGreaterThan(TimeSpan.Zero).And.BeLessThanOrEqualTo(TimeSpan.FromMinutes(1));
        otherKey.Should().BeNull();
    }

    // Private methods

    private static TestRateLimiter[] NewBlockedLimiters()
        => [new TestRateLimiter { IsBlocked = true }, new TestRateLimiter { IsBlocked = true }];

    private static RateLimitIdentity[] NewIdentities()
        => [
            new RateLimitIdentity(RateLimitIdentityKind.Session, "session-1"),
            new RateLimitIdentity(RateLimitIdentityKind.IP, "203.0.113.9"),
        ];

    private static RateLimitPolicy NewPolicy(
        RateLimitClass rateLimitClass,
        TestRateLimiter sessionLimiter,
        TestRateLimiter ipLimiter)
        => new(
            new Dictionary<(RateLimitClass, RateLimitIdentityKind), RateLimitRule> {
                [(rateLimitClass, RateLimitIdentityKind.Session)]
                    = RateLimitRule.New(sessionLimiter, OnePerMinute, x => x.Value),
                [(rateLimitClass, RateLimitIdentityKind.IP)]
                    = RateLimitRule.New(ipLimiter, OnePerMinute, x => x.Value),
            },
            NullLogger.Instance);

    // Nested types

    private sealed class TestRateLimiter : IRateLimiter<string, SlidingWindowBudget>
    {
        private readonly TaskCompletionSource _whenStarted = TaskCompletionSourceExt.New();
        private readonly TaskCompletionSource _whenUnblocked = TaskCompletionSourceExt.New();
        private int _checkCount;

        public int CheckCount => _checkCount;
        public int ExhaustedAfter { get; init; } = int.MaxValue;
        public bool IsBlocked { get; init; }
        public Task WhenStarted => _whenStarted.Task;

        public void Unblock()
            => _whenUnblocked.TrySetResult();

        ValueTask<TimeSpan?> IRateLimiter<string>.Check(
            string key,
            object? budget,
            CancellationToken cancellationToken)
            => Check(key, (SlidingWindowBudget?)budget, cancellationToken);

        public ValueTask<TimeSpan?> Check(
            string key,
            SlidingWindowBudget? budget,
            CancellationToken cancellationToken = default)
        {
            _whenStarted.TrySetResult();
            var count = Interlocked.Increment(ref _checkCount);
            var retryDelay = count > ExhaustedAfter ? TimeSpan.FromSeconds(30) : (TimeSpan?)null;
            return IsBlocked
                ? new ValueTask<TimeSpan?>(CheckAsync(retryDelay))
                : new ValueTask<TimeSpan?>(retryDelay);
        }

        // Private methods

        private async Task<TimeSpan?> CheckAsync(TimeSpan? retryDelay)
        {
            await _whenUnblocked.Task.ConfigureAwait(false);
            return retryDelay;
        }
    }
}
