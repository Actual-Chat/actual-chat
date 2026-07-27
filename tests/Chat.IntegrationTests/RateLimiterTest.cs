using ActualChat.Chat.Db;
using ActualChat.Redis;
using ActualChat.Resilience;
using ActualChat.Testing.Host;
using ActualLab.Redis;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class RateLimiterTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [FlakyFact("AY: Time dependent", 3)]
    public async Task SlidingWindowShouldAllowUpToItsLimit()
    {
        // arrange
        var key = $"rate_limit:test:{Ulid.NewUlid()}";
        var rateLimiter = NewSlidingWindowRateLimiter(new SlidingWindowBudget(10, TimeSpan.FromSeconds(2)));
        try {
            // act & assert
            for (var i = 0; i < 10; i++) {
                var retryDelay = await rateLimiter.Check(key);
                retryDelay.Should().BeNull();
            }
            (await rateLimiter.Check(key)).Should().NotBeNull();
            // TODO(DF): Check rate limiter, it takes longer to permit next call than expected.
            await Task.Delay(2100);
            (await rateLimiter.Check(key)).Should().BeNull();
        }
        finally {
            await rateLimiter.DeleteKey(key);
        }
    }

    [FlakyFact("AY: Time dependent", 3)]
    public async Task TokenBucketShouldAllowUpToItsLimit()
    {
        // arrange
        var key = $"rate_limit:test:{Ulid.NewUlid()}";
        var rateLimiter = NewTokenBucketRateLimiter(new TokenBucketBudget(300, TimeSpan.FromSeconds(10)));
        try {
            // act & assert
            for (var i = 0; i < 10; i++) {
                var retryDelay = await rateLimiter.Check(key, 30);
                retryDelay.Should().BeNull();
            }
            (await rateLimiter.Check(key, 30)).Should().NotBeNull();
            await Task.Delay(2000);
            (await rateLimiter.Check(key, 30)).Should().BeNull();
        }
        finally {
            await rateLimiter.DeleteKey(key);
        }
    }

    [FlakyFact("AY: Time dependent", 3)]
    public async Task RequestsRateShouldBeLimited()
    {
        // arrange
        var key = $"rate_limit:test:{Ulid.NewUlid()}";
        var rateLimiter = NewSlidingWindowRateLimiter(new SlidingWindowBudget(10, TimeSpan.FromSeconds(1)));
        try {
            var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            long executedRequests = 0;

            // act
            try {
                await Parallel.ForAsync(0, 100, cts.Token,
                    async (_, ct2) => {
                        while (await rateLimiter.Check(key, ct2).ConfigureAwait(false) is { } retryDelay)
                            await Task.Delay(retryDelay, ct2).ConfigureAwait(false);
                        Interlocked.Increment(ref executedRequests);
                    });
            }
            catch (OperationCanceledException) { }
            finally {
                cts.DisposeSilently();
            }

            // assert
            // Expect ~50 requests (10 per second × 5 seconds), but allow margin for:
            // - Race at cancellation boundary (tasks completing delay just as cancellation fires)
            // - Timing variations in sliding window boundaries
            executedRequests.Should().BeInRange(50, 60);
        }
        finally {
            await rateLimiter.DeleteKey(key);
        }
    }

    // Private methods

    private RedisSlidingWindowRateLimiter NewSlidingWindowRateLimiter(SlidingWindowBudget budget)
        => new(AppHost.Services.GetRequiredService<RedisDb<ChatDbContext>>(), "", budget);

    private RedisTokenBucketRateLimiter NewTokenBucketRateLimiter(TokenBucketBudget budget)
        => new(AppHost.Services.GetRequiredService<RedisDb<ChatDbContext>>(), "", budget);
}
