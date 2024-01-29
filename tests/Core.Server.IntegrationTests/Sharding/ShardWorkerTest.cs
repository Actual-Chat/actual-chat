using ActualChat.Testing.Host;

namespace ActualChat.Core.Server.IntegrationTests;

public class ShardWorkerTest(ITestOutputHelper @out) : AppHostTestBase(@out)
{
    [Fact(Timeout = 30_000)]
    public async Task BasicTest()
    {
        var shardingDef = Sharding.Backend.Instance;
        using var h1 = await NewAppHost(TestAppHostOptions.None);
        await using var w1a = new TestShardWorker(h1.Services, "w1a");
        w1a.Start();
        await Task.Delay(1000);
        await w1a.DisposeSilentlyAsync();
        var shardIndexes = await w1a.Channel.Reader.ReadAllAsync().Distinct().ToListAsync();
        shardIndexes.Count.Should().Be(shardingDef.ShardCount);

        await using var w1b = new TestShardWorker(h1.Services, "w1b");
        w1b.Start();

        using var h2 = await NewAppHost(TestAppHostOptions.None);
        await using var w2a = new TestShardWorker(h2.Services, "w2a");
        w2a.Start();
        await using var w2b = new TestShardWorker(h2.Services, "w2b");
        w2b.Start();
        await Task.Delay(3000);
        await w2a.DisposeSilentlyAsync();
        shardIndexes = await w2a.Channel.Reader.ReadAllAsync().Distinct().ToListAsync();
        shardIndexes.Count.Should().Be(shardingDef.ShardCount / 2);
        await w2b.DisposeSilentlyAsync();
        shardIndexes = await w2b.Channel.Reader.ReadAllAsync().Distinct().ToListAsync();
        shardIndexes.Count.Should().Be(shardingDef.ShardCount / 2);
    }

    public class TestShardWorker(IServiceProvider services, string name) : ShardWorker<Sharding.Backend>(services)
    {
        private static readonly object?[] ShardOwners = new object?[Sharding.Backend.Instance.ShardCount];
        private static readonly RandomTimeSpan WaitDelay = TimeSpan.FromSeconds(0.1).ToRandom(0.5);
        private ITestOutputHelper Out { get; } = services.GetRequiredService<ITestOutputHelper>();

        public Channel<int> Channel { get; } = System.Threading.Channels.Channel.CreateUnbounded<int>(new UnboundedChannelOptions() {
            SingleReader = true,
            SingleWriter = false,
        });

        protected override async Task OnRun(int shardIndex, CancellationToken cancellationToken)
        {
            Out.WriteLine($"-> OnRun({shardIndex} @ {ThisNode.Id}-{name})");
            lock (ShardOwners) {
                if (ShardOwners[shardIndex] != null)
                    Channel.Writer.TryComplete(StandardError.Constraint("Shard is used by another worker!"));
                ShardOwners[shardIndex] = this;
            }
            try {
                await Channel.Writer.WriteAsync(shardIndex, cancellationToken);
                await Clock.Delay(WaitDelay.Next(), cancellationToken);
            }
            finally {
                lock (ShardOwners) {
                    if (ShardOwners[shardIndex] != this)
                        Channel.Writer.TryComplete(StandardError.Constraint("Shard must be used by this worker!"));
                    ShardOwners[shardIndex] = null;
                }
                Out.WriteLine($"<- OnRun({shardIndex} @ {ThisNode.Id}-{name})");
            }
        }

        protected override Task OnStop()
        {
            Channel.Writer.TryComplete();
            return Task.CompletedTask;
        }
    }
}
