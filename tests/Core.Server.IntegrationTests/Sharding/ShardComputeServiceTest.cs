using System.Diagnostics;
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
        var timeout = TimeSpan.FromSeconds(5);
        var shardScheme = ShardScheme.TestBackend;
        var key = "";
        var maxRecentDelta = TimeSpan.FromSeconds(TestRunnerInfo.IsBuildAgent() ? 0.25 : 0.1);

        await using var h1 = await NewAppHost();
        var o1 = h1.Services.ShardOwner(shardScheme);
        var s1 = h1.Services.GetRequiredService<TestShardComputeService>();
        (await s1.GetTime(key)).Elapsed.Should().BeLessThan(maxRecentDelta);
        var c1 = await Computed.Capture(() => s1.TryGetTime(key));
        c1.Value!.Value.Elapsed.Should().BeLessThan(maxRecentDelta);

        await using var h2 = await NewAppHost();
        var o2 = h2.Services.ShardOwner(shardScheme);
        var s2 = h2.Services.GetRequiredService<TestShardComputeService>();
        await ComputedTest.When(async ct => {
            var st1 = await o1.State.Use(ct).ConfigureAwait(false);
            st1.ShardStates.Count(x => x.OwnershipState.Value is not null).Should().Be(shardScheme.ShardCount / 2);
            var st2 = await o2.State.Use(ct).ConfigureAwait(false);
            st2.ShardStates.Count(x => x.OwnershipState.Value is not null).Should().Be(shardScheme.ShardCount / 2);
        }, TimeSpan.FromSeconds(20)); // May need more time on build agents

        var isOwner1 = o1.GetShardOwnershipStatus(key) is ShardOwnershipStatus.LockedByThisNode;
        var isOwner2 = o2.GetShardOwnershipStatus(key) is ShardOwnershipStatus.LockedByThisNode;
        isOwner2.Should().NotBe(isOwner1);
        var c2 = await Computed.Capture(() => s2.TryGetTime(key));
        var (cOwned, cNotOwned) = isOwner1 ? (c1, c2) : (c2, c1);

        await Check(false);

        // RpcRerouteException is OperationCanceledException,
        // so Computed.Capture shouldn't capture a computed that "stores" it
        await Assert.ThrowsAsync<RpcRerouteException>(
            async () => await Computed.Capture(() => {
                var sNotOwned = isOwner1 ? s2 : s1;
                return sNotOwned.GetTime(key); // GetTime throws, TryGetTime doesn't
            }));

        // Dispose the host that "owns" key's shard
        var hOwner = isOwner1 ? h1 : h2;
        await hOwner.DisposeAsync();

        await Check(true);
        return;

        async Task Check(bool mustInvert) {
            for (var i = 0; i < 3; i++) {
                cOwned = await cOwned.When(x => x.HasValue ^ mustInvert).WaitAsync(timeout);
                cNotOwned = await cNotOwned.When(x => !x.HasValue ^ mustInvert).WaitAsync(timeout);
                var maxWait = Task.Delay(500);
                var completedTask = await Task.WhenAny(maxWait, cOwned.WhenInvalidated(), cNotOwned.WhenInvalidated());
                if (completedTask == maxWait)
                    break;
            }
            WriteLine($"cOwned:    {cOwned.Value}");
            WriteLine($"cNotOwned: {cNotOwned.Value}");
            cOwned.IsConsistent().Should().BeTrue();
            if (!cNotOwned.IsConsistent())
                Debugger.Break();
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
            ShardOwner.GetShardOwnershipStatus(key) is ShardOwnershipStatus.LockedByThisNode
                ? CpuTimestamp.Now
                : null);

    [ComputeMethod]
    public virtual async Task<CpuTimestamp> GetTime(string key, CancellationToken cancellationToken = default)
    {
        await ShardOwner.RequireOwnership(key, cancellationToken).ConfigureAwait(false);
        return CpuTimestamp.Now;
    }
}
