using ActualChat.Testing.Host;

namespace ActualChat.Core.Server.IntegrationTests;

public class LegacyShardWorkerTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(LegacyShardWorkerTest)}", TestAppHostOptions.None, @out)
{
    [Fact(Timeout = 30_000)]
    public async Task BasicTest()
    {
        var shardScheme = ShardScheme.TestBackend;
        using var h1 = await NewAppHost();
        await using var w1a = new TestChannelShardWorker(h1.Services, Out, "w1a");
        w1a.Start();
        // w1a should use all shards
        await ToShardIndexSets(w1a.UsedShardIndexes).AnyAsync(x => x.Count == shardScheme.ShardCount);

        await using var w1b = new TestChannelShardWorker(h1.Services, Out, "w1b");
        w1b.Start();
        // w1b should use all shards as well
        await ToShardIndexSets(w1b.UsedShardIndexes).AnyAsync(x => x.Count == shardScheme.ShardCount);

        using var h2 = await NewAppHost();
        await using var w2 = new TestChannelShardWorker(h2.Services, Out, "w2");
        w2.Start();
        // w2 workers should use half of the shards
        var w2Shards = await ToShardIndexSets(w2.UsedShardIndexes).FirstAsync(x => x.Count == shardScheme.ShardCount / 2);

        // w1c workers should use half of the shards
        await using var w1 = new TestChannelShardWorker(h1.Services, Out, "w1");
        w1.Start();
        var w1Shards = await ToShardIndexSets(w1.UsedShardIndexes).FirstAsync(x => x.Count == shardScheme.ShardCount / 2);
        w1Shards.Intersect(w2Shards).Should().BeEmpty();
    }

    [Fact(Skip = "For manual runs only. Start/stop Redis and watch the output.")]
    public async Task RedisReconnectTest()
    {
        using var h = await NewAppHost();
        await using var w = new TestShardWorker(h.Services, Out, "w");
        w.Start();
        await Task.Delay(TimeSpan.FromMinutes(5));
    }

    private IAsyncEnumerable<ImmutableHashSet<int>> ToShardIndexSets(
        ChannelReader<int> usedShardIndexes,
        CancellationToken cancellationToken = default)
    {
        var shardIndexes = ImmutableHashSet<int>.Empty;
        return usedShardIndexes
            .ReadAllAsync(cancellationToken)
            .Where(x => !shardIndexes.Contains(x))
            .Select(x => shardIndexes = shardIndexes.Add(x));
    }

    // Nested types

    public class TestShardWorker(IServiceProvider services, ITestOutputHelper @out, string name)
        : LegacyShardWorker(services, ShardScheme.TestBackend)
    {
        private ITestOutputHelper Out { get; } = @out;

        protected override async Task OnRun(int shardIndex, CancellationToken cancellationToken)
        {
            var thisNode = ShardScheduler.Owner.ThisNode;
            Out.WriteLine($"-> OnRun({shardIndex} @ {thisNode.Ref}-{name})");
            await ActualLab.Async.TaskExt.NewNeverEndingUnreferenced()
                .WaitAsync(cancellationToken)
                .SilentAwait();
            Out.WriteLine($"<- OnRun({shardIndex} @ {thisNode.Ref}-{name})");
        }
    }

    public class TestChannelShardWorker(IServiceProvider services, ITestOutputHelper @out, string name)
        : LegacyShardWorker(services, ShardScheme.TestBackend)
    {
        private static readonly HashSet<TestChannelShardWorker>[] ShardOwners
            = Enumerable
                .Range(0, ShardScheme.TestBackend.ShardCount)
                .Select(_ => new HashSet<TestChannelShardWorker>())
                .ToArray();
        private static readonly RandomTimeSpan WaitDelay = TimeSpan.FromSeconds(0.1).ToRandom(0.5);
        private ITestOutputHelper Out { get; } = @out;

        public Channel<int> UsedShardIndexes { get; } = ActualLab.Channels.ChannelExt.Create<int>(new UnboundedChannelOptions() {
            SingleReader = false,
            SingleWriter = false,
        });

        public override string ToString()
        {
            var thisNode = ShardScheduler.Owner.ThisNode;
            return $"{thisNode.Ref}-{name}";
        }

        protected override async Task OnRun(int shardIndex, CancellationToken cancellationToken)
        {
            Out.WriteLine($"-> OnRun({shardIndex} @ {this})");
            lock (ShardOwners) {
                var shardOwners = ShardOwners[shardIndex];
                if (shardOwners.Any(x => x.ShardScheduler != ShardScheduler))
                    UsedShardIndexes.Writer.TryComplete(StandardError.Constraint(
                        $"Shard {shardIndex} @ {this} is used by a worker from another host!"));
                shardOwners.Add(this);
            }
            try {
                await UsedShardIndexes.Writer.WriteAsync(shardIndex, cancellationToken);
                await Task.Delay(WaitDelay.Next(), cancellationToken);
            }
            finally {
                lock (ShardOwners) {
                    var shardOwners = ShardOwners[shardIndex];
                    if (!shardOwners.Remove(this))
                        UsedShardIndexes.Writer.TryComplete(StandardError.Constraint(
                            $"Shard {shardIndex} must be used {this}!"));
                }
                Out.WriteLine($"<- OnRun({shardIndex} @ {this})");
            }
        }

        protected override Task OnStop()
        {
            UsedShardIndexes.Writer.TryComplete();
            return Task.CompletedTask;
        }
    }
}
