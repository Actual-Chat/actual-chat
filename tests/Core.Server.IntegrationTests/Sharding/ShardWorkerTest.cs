using ActualChat.Testing.Host;

namespace ActualChat.Core.Server.IntegrationTests;

public class ShardWorkerTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(ShardWorkerTest)}", TestAppHostOptions.None, @out)
{
    [Fact(Timeout = 30_000)]
    public async Task BasicTest()
    {
        var shardScheme = ShardScheme.TestBackend;
        using var h1 = await NewAppHost();
        await using var w1a = new TestChannelShardWorker(h1.Services, Out, "w1a");
        w1a.Start();

        // w1a should lock all shards
        await ToShardIndexSets(w1a.UsedShardIndexes).AnyAsync(x => x.Count == shardScheme.ShardCount);
        await w1a.DisposeSilentlyAsync();

        await using var w1b = new TestChannelShardWorker(h1.Services, Out, "w1b");
        w1b.Start();

        // w1b should lock all shards, coz w1a was disposed
        await ToShardIndexSets(w1b.UsedShardIndexes).AnyAsync(x => x.Count == shardScheme.ShardCount);

        using var h2 = await NewAppHost();
        await using var w2a = new TestChannelShardWorker(h2.Services, Out, "w2a");
        w2a.Start();
        await using var w2b = new TestChannelShardWorker(h2.Services, Out, "w2b");
        w2b.Start();

        // h2 workers should lock half of the shards, splitting them between them
        var h2UsedShardIndexes = ActualLab.Channels.ChannelExt.Create<int>(new UnboundedChannelOptions());
        _ = w2a.UsedShardIndexes.Reader.Copy(h2UsedShardIndexes.Writer, ChannelCopyMode.CopyAll);
        _ = w2b.UsedShardIndexes.Reader.Copy(h2UsedShardIndexes.Writer, ChannelCopyMode.CopyAll);
        await ToShardIndexSets(h2UsedShardIndexes)
            .FirstAsync(x => x.Count == shardScheme.ShardCount / 2);
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
        : ShardWorker(services, ShardScheme.TestBackend)
    {
        private ITestOutputHelper Out { get; } = @out;

        protected override async Task OnRun(int shardIndex, CancellationToken cancellationToken)
        {
            Out.WriteLine($"-> OnRun({shardIndex} @ {ThisNode.Ref}-{name})");
            await ActualLab.Async.TaskExt.NewNeverEndingUnreferenced()
                .WaitAsync(cancellationToken)
                .SilentAwait();
            Out.WriteLine($"<- OnRun({shardIndex} @ {ThisNode.Ref}-{name})");
        }
    }

    public class TestChannelShardWorker(IServiceProvider services, ITestOutputHelper @out, string name)
        : ShardWorker(services, ShardScheme.TestBackend)
    {
        private static readonly object?[] ShardOwners = new object?[ShardScheme.TestBackend.ShardCount];
        private static readonly RandomTimeSpan WaitDelay = TimeSpan.FromSeconds(0.1).ToRandom(0.5);
        private ITestOutputHelper Out { get; } = @out;

        public Channel<int> UsedShardIndexes { get; } = ActualLab.Channels.ChannelExt.Create<int>(new UnboundedChannelOptions() {
            SingleReader = false,
            SingleWriter = false,
        });

        protected override async Task OnRun(int shardIndex, CancellationToken cancellationToken)
        {
            Out.WriteLine($"-> OnRun({shardIndex} @ {ThisNode.Ref}-{name})");
            lock (ShardOwners) {
                if (ShardOwners[shardIndex] != null)
                    UsedShardIndexes.Writer.TryComplete(StandardError.Constraint("Shard is used by another worker!"));
                ShardOwners[shardIndex] = this;
            }
            try {
                await UsedShardIndexes.Writer.WriteAsync(shardIndex, cancellationToken);
                await Clock.Delay(WaitDelay.Next(), cancellationToken);
            }
            finally {
                lock (ShardOwners) {
                    if (ShardOwners[shardIndex] != this)
                        UsedShardIndexes.Writer.TryComplete(StandardError.Constraint("Shard must be used by this worker!"));
                    ShardOwners[shardIndex] = null;
                }
                Out.WriteLine($"<- OnRun({shardIndex} @ {ThisNode.Ref}-{name})");
            }
        }

        protected override Task OnStop()
        {
            UsedShardIndexes.Writer.TryComplete();
            return Task.CompletedTask;
        }
    }
}
