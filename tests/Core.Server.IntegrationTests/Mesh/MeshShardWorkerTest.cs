using ActualChat.Mesh;
using ActualChat.Testing.Host;

namespace ActualChat.Core.Server.IntegrationTests.Mesh;

public class MeshShardWorkerTest(ITestOutputHelper @out) : AppHostTestBase(@out)
{
    [Fact(Timeout = 30_000)]
    public async Task BasicTest()
    {
        var shardingDef = Sharding.Backend.Instance;
        using var h1 = await NewAppHost(TestAppHostOptions.None);
        await using var w1 = new TestShardWorker(h1.Services);
        w1.Start();
        await Task.Delay(1000);
        await w1.DisposeSilentlyAsync();
        var shardIndexes = await w1.ShardIndexes.Reader.ReadAllAsync().Distinct().ToListAsync();
        shardIndexes.Count.Should().Be(shardingDef.Size);

        await using var w1a = new TestShardWorker(h1.Services);
        w1a.Start();

        using var h2 = await NewAppHost(TestAppHostOptions.None);
        await using var w2 = new TestShardWorker(h2.Services);
        w2.Start();
        await Task.Delay(3000);
        await w2.DisposeSilentlyAsync();
        shardIndexes = await w2.ShardIndexes.Reader.ReadAllAsync().Distinct().ToListAsync();
        shardIndexes.Count.Should().Be(shardingDef.Size / 2);
    }

    public class TestShardWorker(IServiceProvider services) : MeshShardWorker<Sharding.Backend>(services)
    {
        public ITestOutputHelper Out { get; } = services.GetRequiredService<ITestOutputHelper>();
        public Channel<int> ShardIndexes { get; } = Channel.CreateUnbounded<int>(new UnboundedChannelOptions() {
            SingleReader = true,
            SingleWriter = false,
        });

        protected override async Task OnRun(int shardIndex, CancellationToken cancellationToken)
        {
            Out.WriteLine($"-> OnRun({shardIndex} @ {ThisNode.Id})");
            await ShardIndexes.Writer.WriteAsync(shardIndex, cancellationToken);
            await Clock.Delay(RepeatDelay.Next(), cancellationToken);
            Out.WriteLine($"<- OnRun({shardIndex} @ {ThisNode.Id})");
        }

        protected override Task OnStop()
        {
            ShardIndexes.Writer.TryComplete();
            return Task.CompletedTask;
        }
    }
}
