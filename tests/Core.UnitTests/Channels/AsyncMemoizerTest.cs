using System.Threading.Channels;
using ActualChat.Channels;
using Stl.Async;
using Stl.Channels;
using Stl.Testing;
using Xunit.Abstractions;

namespace ActualChat.Core.UnitTests.Channels
{
    public class AsyncMemoizerTest : TestBase
    {
        public AsyncMemoizerTest(ITestOutputHelper @out) : base(@out) { }

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
            var cSource = Channel.CreateUnbounded<T>();
            var memoizer = cSource.Memoize();
            var copyTask = source.CopyTo(cSource, ChannelCompletionMode.CompleteAndPropagateError);

            var channels = Enumerable.Range(0, 2)
                .Select(_ => Channel.CreateUnbounded<T>())
                .ToArray();
            foreach (var channel in channels)
                await memoizer.AddReplayTarget(channel);
            await copyTask;
            foreach (var channel in channels) {
                // await channel.Reader.Completion;
                var items = channel.ToAsyncEnumerable().ToEnumerable();
                // var items = channel.Reader.ReadAllAsync().ToEnumerable();
                items.Should().BeEquivalentTo(source);
            }
        }
    }
}
