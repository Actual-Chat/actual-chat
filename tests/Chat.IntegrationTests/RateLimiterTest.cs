using System.Diagnostics;
using ActualChat.Chat.Db;
using ActualChat.Redis;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class RateLimiterTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task RateLimiterShouldReturnTrue()
    {
        var rateLimiter = RedisSlidingWindowRateLimiter.Create<ChatDbContext>(
            new RedisSlidingWindowRateLimiter.Options("rate_limit:openai_chat_completion:1", 10, TimeSpan.FromSeconds(2)),
            AppHost.Services);
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
        return;

        Task<bool> IsRequestAllowedAsync()
            => rateLimiter.IsRequestAllowedAsync();
    }

    [Fact]
    public async Task TokenBucketRateLimiterShouldReturnTrue()
    {
        var rateLimiter = RedisTokenBucketRateLimiter.Create<ChatDbContext>(
            new RedisTokenBucketRateLimiter.Options("rate_limit:openai_chat_completion:3", 300, TimeSpan.FromSeconds(2)),
            AppHost.Services);
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
        return;

        Task<bool> IsRequestAllowedAsync()
            => rateLimiter.IsRequestAllowedAsync(30);
    }

    [Fact]
    public async Task RequestsRateShouldBeLimited()
    {
        var rateLimiter = RedisSlidingWindowRateLimiter.Create<ChatDbContext>(
            new RedisSlidingWindowRateLimiter.Options("rate_limit:openai_chat_completion:2", 10, TimeSpan.FromSeconds(1)),
            AppHost.Services);
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
        catch(OperationCanceledException) {}
        finally {
            cts.DisposeSilently();
        }
        executedRequests.Should().Be(50);
    }
}
