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
        await using var w1a = new ShardWorker1(h1.Services, Out, "w1a");
        w1a.Start();
        await Task.Delay(1000);
        await w1a.DisposeSilentlyAsync();
        var shardIndexes = await w1a.Channel.Reader.ReadAllAsync().Distinct().ToListAsync();
        shardIndexes.Count.Should().Be(shardScheme.ShardCount);

        await using var w1b = new ShardWorker1(h1.Services, Out, "w1b");
        w1b.Start();

        using var h2 = await NewAppHost();
        await using var w2a = new ShardWorker1(h2.Services, Out, "w2a");
        w2a.Start();
        await using var w2b = new ShardWorker1(h2.Services, Out, "w2b");
        w2b.Start();
        await Task.Delay(3000);
        await w2a.DisposeSilentlyAsync();
        shardIndexes = await w2a.Channel.Reader.ReadAllAsync().Distinct().ToListAsync();
        shardIndexes.Count.Should().Be(shardScheme.ShardCount / 2);
        await w2b.DisposeSilentlyAsync();
        shardIndexes = await w2b.Channel.Reader.ReadAllAsync().Distinct().ToListAsync();
        shardIndexes.Count.Should().Be(shardScheme.ShardCount / 2);
    }

    [Fact(Skip = "For manual runs only. Start/stop Redis and watch the output.")]
    public async Task RedisReconnectTest()
    {
        using var h = await NewAppHost();
        await using var w = new ShardWorker2(h.Services, Out, "w");
        w.Start();
        await ActualLab.Async.TaskExt.NeverEndingTask;
    }

    // Nested types

    public class ShardWorker1(IServiceProvider services, ITestOutputHelper @out1, string name)
        : ShardWorker(services, ShardScheme.TestBackend)
    {
        private static readonly object?[] ShardOwners = new object?[ShardScheme.TestBackend.ShardCount];
        private static readonly RandomTimeSpan WaitDelay = TimeSpan.FromSeconds(0.1).ToRandom(0.5);
        private ITestOutputHelper Out { get; } = @out1;

        public Channel<int> Channel { get; } = System.Threading.Channels.Channel.CreateUnbounded<int>(new UnboundedChannelOptions() {
            SingleReader = true,
            SingleWriter = false,
        });

        protected override async Task OnRun(int shardIndex, CancellationToken cancellationToken)
        {
            Out.WriteLine($"-> OnRun({shardIndex} @ {ThisNode.Ref}-{name})");
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
                Out.WriteLine($"<- OnRun({shardIndex} @ {ThisNode.Ref}-{name})");
            }
        }

        protected override Task OnStop()
        {
            Channel.Writer.TryComplete();
            return Task.CompletedTask;
        }
    }

    public class ShardWorker2(IServiceProvider services, ITestOutputHelper @out1, string name)
        : ShardWorker(services, ShardScheme.TestBackend)
    {
        private ITestOutputHelper Out { get; } = @out1;

        protected override async Task OnRun(int shardIndex, CancellationToken cancellationToken)
        {
            Out.WriteLine($"-> OnRun({shardIndex} @ {ThisNode.Ref}-{name})");
            await ActualLab.Async.TaskExt.NeverEndingTask.WaitAsync(cancellationToken).SilentAwait();
            Out.WriteLine($"<- OnRun({shardIndex} @ {ThisNode.Ref}-{name})");
        }
    }
}
