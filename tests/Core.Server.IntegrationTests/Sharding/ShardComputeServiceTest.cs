using ActualChat.Testing.Host;

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
    [Fact(Timeout = 30_000)]
    public async Task BasicTest()
    {
        using var h1 = await NewAppHost();
        var s1 = h1.Services.GetRequiredService<TestShardComputeService>();
        var t1 = await s1.GetTime("", CancellationToken.None);
        t1.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(0.1));
    }
}

// Can't be nested, coz proxies aren't generated for nested types
public class TestShardComputeService(IServiceProvider services, ITestOutputHelper output) : IComputeService
{
    private ITestOutputHelper Out { get; } = output;
    private ShardBroker ShardBroker { get; } = services.ShardBroker(ShardScheme.TestBackend);

    [ComputeMethod]
    public virtual async Task<CpuTimestamp> GetTime(string key, CancellationToken cancellationToken)
    {
        Out.WriteLine($"-> GetTime({key})");
        await ShardBroker.ShardLeaseTracker.WhileLeased(key, cancellationToken);
        Out.WriteLine($"<- GetTime({key})");
        return CpuTimestamp.Now;
    }
}
