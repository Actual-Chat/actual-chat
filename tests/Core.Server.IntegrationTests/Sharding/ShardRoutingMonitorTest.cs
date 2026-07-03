using ActualChat.Chat;
using ActualChat.Testing.Host;

namespace ActualChat.Core.Server.IntegrationTests.Sharding;

[Trait("Category", "Slow")]
public class ShardRoutingMonitorTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(ShardRoutingMonitorTest)}",
        TestAppHostOptions.None with { MustStart = true }, @out)
{
    [Fact]
    public async Task ProbesMustPassOnEveryHostAndSurviveMigration()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var cancellationToken = cts.Token;
        var shardScheme = ShardScheme.DiagnosticsBackend;
        var shardCount = shardScheme.ShardCount;

        await using var h1 = await NewAppHost();
        var h2 = await NewAppHost(o => o with { MustInitializeDb = false });
        var o1 = h1.Services.ShardOwner(shardScheme);
        var o2 = h2.Services.ShardOwner(shardScheme);
        await ComputedTest.When(async ct => {
            var bits1 = await o1.BitmapState.Use(ct).ConfigureAwait(false);
            var bits2 = await o2.BitmapState.Use(ct).ConfigureAwait(false);
            bits1.SetBitCount().Should().Be(shardCount / 2);
            bits2.SetBitCount().Should().Be(shardCount / 2);
        }, TimeSpan.FromSeconds(15));

        var monitor1 = new ShardRoutingMonitor(h1.Services);
        var monitor2 = new ShardRoutingMonitor(h2.Services);
        for (var shard = 0; shard < shardCount; shard++) {
            await WhenChecked(monitor1, shard, cancellationToken);
            await WhenChecked(monitor2, shard, cancellationToken);
        }

        // The dead host's shards migrate to h1; monitor1's probe computeds must follow
        await h2.DisposeAsync();
        await ComputedTest.When(async ct => {
            var bits1 = await o1.BitmapState.Use(ct).ConfigureAwait(false);
            bits1.SetBitCount().Should().Be(shardCount);
        }, TimeSpan.FromSeconds(30));

        for (var shard = 0; shard < shardCount; shard++)
            await WhenChecked(monitor1, shard, cancellationToken);
    }

    private async Task WhenChecked(ShardRoutingMonitor monitor, int shardIndex, CancellationToken cancellationToken)
    {
        var lastError = "";
        for (var i = 0; i < 20; i++) {
            try {
                lastError = await monitor.CheckShard(shardIndex, cancellationToken);
                if (lastError == null)
                    return;
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                lastError = $"{e.GetType().GetName()}({e.Message})";
            }
            WriteLine($"Shard {shardIndex.Format()}, attempt {i.Format()}: {lastError}");
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
        Assert.Fail($"Shard {shardIndex.Format()}: the probe check keeps failing: {lastError}");
    }
}
