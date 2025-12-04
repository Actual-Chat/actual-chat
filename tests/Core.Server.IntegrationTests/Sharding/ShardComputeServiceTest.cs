using ActualChat.Testing.Host;
using ActualLab.Rpc;

namespace ActualChat.Core.Server.IntegrationTests.Sharding;

public class ShardComputeServiceTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(ShardComputeServiceTest)}",
        TestAppHostOptions.None with {
            MustStart = true,
            ConfigureServices = (ctx, services) => {
                var fusion = services.AddFusion();
                fusion.AddComputeService<TestShardComputeService>();
            },
        }, @out)
{
    [Fact]
    public async Task BasicTest()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cancellationToken = cts.Token;

        var timeout = TimeSpan.FromSeconds(5);
        var shardScheme = ShardScheme.TestBackend;
        var key = "";
        var maxRecentDelta = TimeSpan.FromSeconds(TestRunnerInfo.IsBuildAgent() ? 0.25 : 0.1);

        await using var h1 = await NewAppHost();
        var o1 = h1.Services.ShardOwner(shardScheme);
        var s1 = h1.Services.GetRequiredService<TestShardComputeService>();
        (await s1.GetTime(key, cancellationToken)).Elapsed.Should().BeLessThan(maxRecentDelta);
        var c1 = await Computed.Capture(() => s1.TryGetTime(key, cancellationToken), cancellationToken);
        c1.Value!.Value.Elapsed.Should().BeLessThan(maxRecentDelta);

        await using var h2 = await NewAppHost();
        var o2 = h2.Services.ShardOwner(shardScheme);
        var s2 = h2.Services.GetRequiredService<TestShardComputeService>();
        await ComputedTest.When(async ct => {
            var bits1 = await o1.BitmapState.Use(ct).ConfigureAwait(false);
            bits1.SetBitCount().Should().Be(shardScheme.ShardCount / 2);
            var bits2 = await o2.BitmapState.Use(ct).ConfigureAwait(false);
            bits2.SetBitCount().Should().Be(shardScheme.ShardCount / 2);
        }, TimeSpan.FromSeconds(10)); // May need more time on build agents

        bool isOwner1 = false;
        bool isOwner2 = false;
        WriteLine($"!!!! Shard: {o1.ShardScheme.GetShardIndex(key)}");
        WriteLine($"- o1.NodeId: {o1.Host.ThisNode.Ref.Value}");
        WriteLine($"- o2.NodeId: {o2.Host.ThisNode.Ref.Value}");
        await ComputedTest.When(_ => {
            var c1 = o1.GetShardStateComputed(key, true);
            var c2 = o2.GetShardStateComputed(key, true);
            c1.Value.Version.Should().NotBe(0); // Waiting for the first update
            c2.Value.Version.Should().NotBe(0); // Waiting for the first update
            isOwner1 = c1.Value.OwnershipStatus is ShardOwnershipStatus.OwnedByThisNode;
            isOwner2 = c2.Value.OwnershipStatus is ShardOwnershipStatus.OwnedByThisNode;
            isOwner2.Should().NotBe(isOwner1);
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(10));
        var c2 = await Computed.Capture(() => s2.TryGetTime(key, cancellationToken), cancellationToken);
        var (cOwned, cNotOwned) = isOwner1 ? (c1, c2) : (c2, c1);

        await Check(false);

        // RpcRerouteException is OperationCanceledException,
        // so Computed.Capture shouldn't capture a computed that "stores" it
        await Assert.ThrowsAsync<RpcRerouteException>(
            async () => await Computed.Capture(() => {
                WriteLine($"!!!! isOwner1: {isOwner1}");
                var sNotOwned = isOwner1 ? s2 : s1;
                return sNotOwned.GetTime(key, cancellationToken); // GetTime throws, TryGetTime doesn't
            }, cancellationToken));

        // Dispose the host that "owns" key's shard
        var hOwner = isOwner1 ? h1 : h2;
        await hOwner.DisposeAsync();

        await Check(true);
        return;

        async Task Check(bool mustInvert) {
            for (var i = 0; i < 10; i++) {
                WriteLine($"Check, iteration {i}");
                WriteLine($"- cOwned:    {cOwned.ToString(InvalidationSourceFormat.Origin)}");
                WriteLine($"- cNotOwned: {cNotOwned.ToString(InvalidationSourceFormat.Origin)}");
                cOwned = await cOwned
                    .When(x => x.HasValue ^ mustInvert, cancellationToken)
                    .WaitAsync(timeout, cancellationToken);
                cNotOwned = await cNotOwned
                    .When(x => !x.HasValue ^ mustInvert, cancellationToken)
                    .WaitAsync(timeout, cancellationToken);
                var waitTask = Task.Delay(250, cancellationToken);
                var completedTask = await Task.WhenAny(
                    waitTask,
                    cOwned.WhenInvalidated(cancellationToken),
                    cNotOwned.WhenInvalidated(cancellationToken));
                if (completedTask == waitTask && completedTask.IsCompletedSuccessfully)
                    break;

                await waitTask;
            }
            cOwned.IsConsistent().Should().BeTrue();
            cNotOwned.IsConsistent().Should().BeTrue();
        }
    }
}

// Can't be nested, coz proxies aren't generated for nested types
public class TestShardComputeService(IServiceProvider services, ITestOutputHelper output) : IComputeService
{
    private ITestOutputHelper Out { get; } = output;
    private ShardOwner ShardOwner { get; } = services.ShardOwner(ShardScheme.TestBackend);

    [ComputeMethod]
    public virtual Task<CpuTimestamp?> TryGetTime(string key, CancellationToken cancellationToken = default)
        => Task.FromResult<CpuTimestamp?>(
            ShardOwner.GetShardOwnershipStatus(key, true) is ShardOwnershipStatus.OwnedByThisNode
                ? CpuTimestamp.Now
                : null);

    [ComputeMethod]
    public virtual async Task<CpuTimestamp> GetTime(string key, CancellationToken cancellationToken = default)
    {
        await ShardOwner.RequireShardOwnership(key, true, cancellationToken).ConfigureAwait(false);
        return CpuTimestamp.Now;
    }
}
