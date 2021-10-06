using System.Threading.Channels;
using ActualChat.Channels;
using Stl.Async;
using Stl.Testing;
using Xunit.Abstractions;

namespace ActualChat.Core.UnitTests.Channels
{
    public class AsyncMemoizerTest : TestBase
    {
        public AsyncMemoizerTest(ITestOutputHelper @out) : base(@out) { }

        [Fact]
        public async Task MemoizeSyncPointTest()
        {
            var cSource = Channel.CreateUnbounded<int>();
            var memoizer = cSource.Reader.Memoize();
            cSource.Writer.WriteAsync(1);
            (await memoizer.Replay().Take(1).CountAsync()).Should().Be(1);
            cSource.Writer.WriteAsync(2);
            (await memoizer.Replay().Take(2).CountAsync()).Should().Be(2);

            var take3Task = memoizer.Replay().Take(3).CountAsync();
            await Task.Delay(100);
            take3Task.IsCompleted.Should().BeFalse();
            cSource.Writer.WriteAsync(3);
            (await take3Task).Should().Be(3);
        }

        [Fact]
        public async Task MemoizeCompletedEmptyChannelTest()
        {
            var tasks = Enumerable.Range(0, 1000).Select(async _ => {
                var cSource = Channel.CreateUnbounded<int>();
                cSource.Writer.Complete();
                var memoizer = cSource.Reader.Memoize();
                var cTarget = Channel.CreateUnbounded<int>();
                var success = await memoizer.AddReplayTarget(cTarget)
                    .WithTimeout(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
                Assert.True(success);
                success = await cTarget.Reader.Completion
                    .WithTimeout(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
                Assert.True(success);
            }).ToArray();
            foreach (var task in tasks)
                await task.ConfigureAwait(false);
        }

        [Fact]
        public async Task MemoizeEmptyChannelTest()
        {
            var tasks = Enumerable.Range(0, 1000).Select(async _ => {
                var cSource = Channel.CreateUnbounded<int>();
                var memoizer = cSource.Memoize();
                var cTarget = Channel.CreateUnbounded<int>();
                await memoizer.AddReplayTarget(cTarget).ConfigureAwait(false);
                cSource.Writer.Complete();
                var success = await cTarget.Reader.Completion
                    .WithTimeout(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
                Assert.True(success);
            }).ToArray();
            foreach (var task in tasks)
                await task.ConfigureAwait(false);
        }

        [Fact]
        public async Task BasicTest()
        {
            var tasks = Enumerable.Range(0, 100)
                .Select(i => RunRangeTest(Enumerable.Range(0, i)))
                .ToArray();
            foreach (var task in tasks)
                await task.ConfigureAwait(false);
        }

        private async Task RunRangeTest<T>(IEnumerable<T> source)
        {
            var memo = source.ToAsyncEnumerable().Memoize();
            var replays = Enumerable.Range(0, 2)
                .Select(_ => memo.Replay());
            foreach (var replay in replays) {
                var items = replay.ToEnumerable();
                items.Should().BeEquivalentTo(source);
            }
            await memo.DistributeTask;
        }
    }
}
