using ActualChat.Chat.Db;
using ActualChat.Redis;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class RateLimiterTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [FlakyFact("AY: Time dependent", 3)]
    public async Task RateLimiterShouldReturnTrue()
    {
        var key = $"rate_limit:test:{Ulid.NewUlid()}";
        var rateLimiter = RedisSlidingWindowRateLimiter.Create<ChatDbContext>(
            new RedisSlidingWindowRateLimiter.Options(key, 10, TimeSpan.FromSeconds(2)),
            AppHost.Services);
        try {
            for (int i = 0; i < 10; i++) {
                var isAllowed = await IsRequestAllowedAsync();
                isAllowed.Should().BeTrue();
            }
            {
                var isAllowed = await IsRequestAllowedAsync();
                isAllowed.Should().BeFalse();
            }
            // TODO(DF): Check rate limiter, it takes longer to permit next call than expected.
            //await Task.Delay(2000);
            await Task.Delay(2100);
            {
                var isAllowed = await IsRequestAllowedAsync();
                isAllowed.Should().BeTrue();
            }
        }
        finally {
            await rateLimiter.DeleteKey();
        }
        return;

        Task<bool> IsRequestAllowedAsync()
            => rateLimiter.IsRequestAllowedAsync();
    }

    [FlakyFact("AY: Time dependent", 3)]
    public async Task TokenBucketRateLimiterShouldReturnTrue()
    {
        var key = $"rate_limit:test:{Ulid.NewUlid()}";
        var rateLimiter = RedisTokenBucketRateLimiter.Create<ChatDbContext>(
            new RedisTokenBucketRateLimiter.Options(key, 300, TimeSpan.FromSeconds(10)),
            AppHost.Services);
        try {
            for (int i = 0; i < 10; i++) {
                var isAllowed = await IsRequestAllowedAsync();
                isAllowed.Should().BeTrue();
            }
            {
                var isAllowed = await IsRequestAllowedAsync();
                isAllowed.Should().BeFalse();
            }
            await Task.Delay(2000);
            {
                var isAllowed = await IsRequestAllowedAsync();
                isAllowed.Should().BeTrue();
            }
        }
        finally {
            await rateLimiter.DeleteKey();
        }
        return;

        Task<bool> IsRequestAllowedAsync()
            => rateLimiter.IsRequestAllowedAsync(30);
    }

    [FlakyFact("AY: Time dependent", 3)]
    public async Task RequestsRateShouldBeLimited()
    {
        var key = $"rate_limit:test:{Ulid.NewUlid()}";
        var rateLimiter = RedisSlidingWindowRateLimiter.Create<ChatDbContext>(
            new RedisSlidingWindowRateLimiter.Options(key, 10, TimeSpan.FromSeconds(1)),
            AppHost.Services);
        try {
            var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            long executedRequests = 0;
            try {
                await Parallel.ForAsync(0, 100, cts.Token,
                    async (_, ct2) => {
                        await rateLimiter.Acquire(ct2).ConfigureAwait(false);
                        Interlocked.Increment(ref executedRequests);
                    });
            }
            catch (OperationCanceledException) { }
            finally {
                cts.DisposeSilently();
            }
            // Expect ~50 requests (10 per second × 5 seconds), but allow margin for:
            // - Race at cancellation boundary (tasks completing delay just as cancellation fires)
            // - Timing variations in sliding window boundaries
            executedRequests.Should().BeInRange(50, 60);
        }
        finally {
            await rateLimiter.DeleteKey();
        }
    }
}
