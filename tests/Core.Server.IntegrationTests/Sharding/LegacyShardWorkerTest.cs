using ActualChat.Testing.Host;
using ActualLab.Generators;

namespace ActualChat.Core.Server.IntegrationTests.Sharding;

public class LegacyShardWorkerTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(LegacyShardWorkerTest)}", TestAppHostOptions.None, @out)
{
    [Fact(Timeout = 30_000)]
    public async Task BasicTest()
    {
        var shardScheme = ShardScheme.TestBackend;
        await using var h1 = await NewAppHost();
        await using var w1a = new ChannelShardWorker(h1.Services, Out, "w1a");
        w1a.Start();
        // w1a should use all shards
        await ToShardIndexSets("w1a", w1a.UsedShardIndexes).AnyAsync(x => x.Count == shardScheme.ShardCount);

        await using var w1b = new ChannelShardWorker(h1.Services, Out, "w1b");
        w1b.Start();
        // w1b should use all shards as well
        await ToShardIndexSets("w1b", w1b.UsedShardIndexes).AnyAsync(x => x.Count == shardScheme.ShardCount);

        await using var h2 = await NewAppHost(o => o with { MustInitializeDb = false });
        await using var w2 = new ChannelShardWorker(h2.Services, Out, "w2");
        w2.Start();
        // w2 workers should use half of the shards
        var w2Shards = await ToShardIndexSets("w2", w2.UsedShardIndexes).FirstAsync(x => x.Count == shardScheme.ShardCount / 2);

        // w1c workers should use half of the shards
        await using var w1c = new ChannelShardWorker(h1.Services, Out, "w1c");
        w1c.Start();
        var w1Shards = await ToShardIndexSets("w1", w1c.UsedShardIndexes).FirstAsync(x => x.Count == shardScheme.ShardCount / 2);
        w1Shards.Intersect(w2Shards).Should().BeEmpty();
    }

    [Fact(Timeout = 30_000)]
    public async Task TortureTest()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cancellationToken = cts.Token;

        var startDelays = new RandomTimeSpan(5, 1);
        var runDelays = new RandomTimeSpan(2, 1);
        var workerCount = TestRunnerInfo.IsBuildAgent() ? 3 : 30;
        var tasks = Enumerable.Range(0, workerCount).Select(i => Worker(i, cancellationToken)).ToList();
        await Task.WhenAll(tasks);
        return;

        async Task Worker(int index, CancellationToken ct) {
            var workerName = $"w{index}";

            // ReSharper disable once LocalFunctionHidesMethod
            void WriteLine(string message)
                => Out.WriteLine($"{workerName}: {message}");

            await Task.Delay(startDelays.Next(), ct);
            WriteLine("starting host...");
            await using var host = await NewAppHost();
            await using var w = new ChannelShardWorker(host.Services, Out, workerName);
            WriteLine("starting...");
            w.Start();
            await Task.Delay(runDelays.Next(), ct);
            WriteLine("stopping...");
            await w.Stop();
            w.UsedShardIndexes.Writer.TryComplete();
            var shardIndexes = await ToShardIndexList(w.UsedShardIndexes, ct);
            WriteLine("used shards: " + shardIndexes.ToDelimitedString(","));
        }
    }

    [Fact(Skip = "For manual runs only. Start/stop Redis and watch the output.")]
    public async Task RedisReconnectTest()
    {
        await using var h = await NewAppHost();
        await using var w = new SimpleShardWorker(h.Services, Out, "w");
        w.Start();
        await Task.Delay(TimeSpan.FromMinutes(5));
    }

    private ValueTask<List<int>> ToShardIndexList(
        ChannelReader<int> usedShardIndexes,
        CancellationToken cancellationToken = default)
        => usedShardIndexes
            .ReadAllAsync(cancellationToken)
            .Distinct()
            .ToListAsync(cancellationToken);

    private IAsyncEnumerable<ImmutableHashSet<int>> ToShardIndexSets(
        string name,
        ChannelReader<int> usedShardIndexes,
        CancellationToken cancellationToken = default)
    {
        var shardIndexes = ImmutableHashSet<int>.Empty;
        return usedShardIndexes
            .ReadAllAsync(cancellationToken)
            .Where(x => !shardIndexes.Contains(x))
            .Select(x => {
                shardIndexes = shardIndexes.Add(x);
                WriteLine($"{name}: {shardIndexes.ToDelimitedString()}");
                return shardIndexes;
            });
    }

    // Nested types

    private class SimpleShardWorker(IServiceProvider services, ITestOutputHelper @out, string name)
        : LegacyShardWorker(services, ShardScheme.TestBackend)
    {
        private ITestOutputHelper Out { get; } = @out;

        protected override async Task OnRun(int shardIndex, CancellationToken cancellationToken)
        {
            var thisNode = ShardOwner.Host.ThisNode;
            Out.WriteLine($"-> OnRun({shardIndex} @ {thisNode.Ref}-{name})");
            await TaskExt.NeverEnding(cancellationToken).SilentAwait();
            Out.WriteLine($"<- OnRun({shardIndex} @ {thisNode.Ref}-{name})");
        }
    }

    private class ChannelShardWorker(IServiceProvider services, ITestOutputHelper @out, string name)
        : LegacyShardWorker(services, ShardScheme.TestBackend)
    {
        private static readonly HashSet<ChannelShardWorker>[] ShardOwners
            = Enumerable
                .Range(0, ShardScheme.TestBackend.ShardCount)
                .Select(_ => new HashSet<ChannelShardWorker>())
                .ToArray();
        private static readonly RandomTimeSpan WaitDelay = TimeSpan.FromSeconds(0.1).ToRandom(0.5);

        public Channel<int> UsedShardIndexes { get; } = ActualLab.Channels.ChannelExt.Create<int>(new UnboundedChannelOptions() {
            SingleReader = false,
            SingleWriter = false,
        });

        public override string ToString()
        {
            var thisNode = ShardOwner.Host.ThisNode;
            return $"{thisNode.Ref}-{name}";
        }

        protected override async Task OnRun(int shardIndex, CancellationToken cancellationToken)
        {
            @out.WriteLine($"-> OnRun({shardIndex} @ {this})");
            lock (ShardOwners) {
                var shardOwners = ShardOwners[shardIndex];
                if (shardOwners.Any(x => x.ShardOwner != ShardOwner))
                    UsedShardIndexes.Writer.TryComplete(StandardError.Constraint(
                        $"Shard {shardIndex} @ {this} is used by a worker from another host!"));
                shardOwners.Add(this);
            }
            try {
                var rnd = RandomShared.NextDouble();
                if (rnd < 0.15) {
                    @out.WriteLine($"<- OnRun({shardIndex} @ {this}) - fake failure");
                    throw new InvalidOperationException("Fake failure");
                }
                if (rnd < 0.3) {
                    @out.WriteLine($"<- OnRun({shardIndex} @ {this}) - fake early termination");
                    return;
                }

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
                @out.WriteLine($"<- OnRun({shardIndex} @ {this})");
            }
        }

        protected override Task OnStop()
        {
            UsedShardIndexes.Writer.TryComplete();
            return Task.CompletedTask;
        }
    }
}
