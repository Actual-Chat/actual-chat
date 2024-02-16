namespace ActualChat.Core.UnitTests.Channels;

public class AsyncMemoizerTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact(Skip = "Flaky test, avoid Task.Delay()")]
    public async Task SyncPointTest()
    {
        var cSource = Channel.CreateUnbounded<int>();
        var memoizer = cSource.Reader.Memoize();
        _ = cSource.Writer.WriteAsync(1);
        (await memoizer.Replay().Take(1).CountAsync()).Should().Be(1);
        _ = cSource.Writer.WriteAsync(2);
        (await memoizer.Replay().Take(2).CountAsync()).Should().Be(2);

        var take3Task = memoizer.Replay().Take(3).CountAsync();
        await Task.Delay(100);
        take3Task.IsCompleted.Should().BeFalse();
        _ = cSource.Writer.WriteAsync(3);
        (await take3Task).Should().Be(3);

        // Check for targets removal
        _ = cSource.Writer.WriteAsync(4);
        // Let's wait when unused channels are definitely removed
        await memoizer.Replay().Take(4).CountAsync();

        var targets = (HashSet<ChannelWriter<int>>)
            memoizer.GetType()
                .GetField("_targets", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(memoizer)!;
        targets.Count.Should().Be(1);
    }

    [Fact]
    public async Task CompletedEmptyChannelTest()
    {
        var tasks = Enumerable.Range(0, 1000).Select(async _ => {
            var cSource = Channel.CreateUnbounded<int>();
            cSource.Writer.Complete();
            var memoizer = cSource.Reader.Memoize();
            var cTarget = Channel.CreateUnbounded<int>();
            await memoizer.AddReplayTarget(cTarget)
                    .WaitAsync(TimeSpan.FromSeconds(5));
            await cTarget.Reader.Completion
                .WaitAsync(TimeSpan.FromSeconds(5));
        }).ToArray();
        foreach (var task in tasks)
            await task;
    }

    [Fact]
    public async Task EmptyChannelTest()
    {
        var tasks = Enumerable.Range(0, 1000).Select(async _ => {
            var cSource = Channel.CreateUnbounded<int>();
            var memoizer = cSource.Memoize();
            var cTarget = Channel.CreateUnbounded<int>();
            await memoizer.AddReplayTarget(cTarget);
            cSource.Writer.Complete();
            await cTarget.Reader.Completion
                .WaitAsync(TimeSpan.FromSeconds(5));
        }).ToArray();
        foreach (var task in tasks)
            await task;
    }

    [Fact]
    public async Task BasicTest()
    {
        var tasks = Enumerable.Range(0, 100)
            .Select(i => RunRangeTest(Enumerable.Range(0, i)))
            .ToArray();
        foreach (var task in tasks)
            await task;
    }

    private async Task RunRangeTest<T>(IEnumerable<T> source)
    {
        // ReSharper disable once PossibleMultipleEnumeration
        var memo = source.ToAsyncEnumerable().Memoize();
        var replays = Enumerable.Range(0, 2)
            .Select(_ => memo.Replay());
        foreach (var replay in replays) {
            var items = replay.ToEnumerable();
            // ReSharper disable once PossibleMultipleEnumeration
            items.Should().BeEquivalentTo(source);
        }
        await memo.WriteTask;
    }
}
