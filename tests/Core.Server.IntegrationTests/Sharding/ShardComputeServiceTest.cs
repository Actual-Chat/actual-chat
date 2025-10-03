using ActualChat.Testing.Host;
using ActualLab.Rpc;

namespace ActualChat.Core.Server.IntegrationTests.Sharding;

public class ShardComputeServiceTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(ShardComputeServiceTest)}",
        TestAppHostOptions.Default with {
            ConfigureServices = (ctx, services) => {
                var fusion = services.AddFusion();
                fusion.AddComputeService<TestShardComputeService>();
            },
        }, @out)
{
    [Fact(Skip = "AY Should resolve timeout")]
    public async Task BasicTest()
    {
        var shardScheme = ShardScheme.TestBackend;
        var key = "";
        var maxRecentDelta = TimeSpan.FromSeconds(0.1);

        using var h1 = await NewAppHost();
        var sb1 = h1.Services.ShardOwner(shardScheme);
        var s1 = h1.Services.GetRequiredService<TestShardComputeService>();
        (await s1.GetTime(key)).Elapsed.Should().BeLessThan(maxRecentDelta);
        var c1 = await Computed.Capture(() => s1.TryGetTime(key));
        c1.Value!.Value.Elapsed.Should().BeLessThan(maxRecentDelta);

        using var h2 = await NewAppHost();
        var sb2 = h2.Services.ShardOwner(shardScheme);
        var s2 = h2.Services.GetRequiredService<TestShardComputeService>();
        await ComputedTest.When(async ct => {
            var st1 = await sb1.State.Use(ct).ConfigureAwait(false);
            st1.ShardStates.Count(x => x.LeaseState.Value is not null).Should().Be(shardScheme.ShardCount / 2);
            var st2 = await sb2.State.Use(ct).ConfigureAwait(false);
            st2.ShardStates.Count(x => x.LeaseState.Value is not null).Should().Be(shardScheme.ShardCount / 2);
        });

        var isLeased1 = await sb1.IsLeased(key, CancellationToken.None);
        var isLeased2 = await sb2.IsLeased(key, CancellationToken.None);
        isLeased2.Should().NotBe(isLeased1);
        var c2 = await Computed.Capture(() => s2.TryGetTime(key));
        if (isLeased2)
            (c1, c2) = (c2, c1); // Swap c1 and c2 to make c1 the one that is leased

        // c1 should (still) be consistent
        c1.IsConsistent().Should().BeTrue();

        // c2 should be null
        c2.Value.HasValue.Should().BeFalse();

        // RpcRerouteException is OperationCanceledException,
        // so Computed.Capture shouldn't capture a computed that "stores" it
        await Assert.ThrowsAsync<RpcRerouteException>(
            async () => await Computed.Capture(() => {
                var sNotLeased = isLeased1 ? s2 : s1;
                return sNotLeased.GetTime(key); // GetTime throws, TryGetTime doesn't
            }));

        // Dispose the host leasing key's shard
        var hLeased = isLeased1 ? h1 : h2;
        hLeased.Dispose();

        // c2 should auto-update
        await c2.WhenInvalidated();
        c2 = await c2.Update();
        c2.Value.HasValue.Should().BeTrue();
    }
}

// Can't be nested, coz proxies aren't generated for nested types
public class TestShardComputeService(IServiceProvider services, ITestOutputHelper output) : IComputeService
{
    private ITestOutputHelper Out { get; } = output;
    private ShardOwner ShardOwner { get; } = services.ShardOwner(ShardScheme.TestBackend);

    [ComputeMethod]
    public virtual async Task<CpuTimestamp?> TryGetTime(string key, CancellationToken cancellationToken = default)
    {
        var isLeased = await ShardOwner.IsLeased(key, cancellationToken).ConfigureAwait(false);
        return isLeased ? CpuTimestamp.Now : null;
    }

    [ComputeMethod]
    public virtual async Task<CpuTimestamp> GetTime(string key, CancellationToken cancellationToken = default)
    {
        await ShardOwner.RequireLeaseOrReroute(key, cancellationToken).ConfigureAwait(false);
        return CpuTimestamp.Now;
    }
}
