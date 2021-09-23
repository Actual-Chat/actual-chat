using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using ActualChat.Channels;
using FluentAssertions;
using Stl.Channels;
using Stl.Testing;
using Xunit;
using Xunit.Abstractions;

namespace ActualChat.Tests.Channels
{
    public class DistributorTest : TestBase
    {
        public DistributorTest(ITestOutputHelper @out) : base(@out)
        { }

        [Fact]
        public async Task DistributeCompletedEmptyChannelTest()
        {
            var tasks = Enumerable.Range(0, 10).Select(async _ => {
                var cSource = Channel.CreateUnbounded<int>();
                cSource.Writer.Complete();
                var distributor = cSource.Distribute();
                var cTarget = Channel.CreateUnbounded<int>();
                await distributor.AddTarget(cTarget);
                await cTarget.Reader.Completion;
            }).ToArray();
            foreach (var task in tasks)
                await task;
        }

        [Fact]
        public async Task DistributeEmptyChannelTest()
        {
            var tasks = Enumerable.Range(0, 10).Select(async _ => {
                var cSource = Channel.CreateUnbounded<int>();
                var distributor = cSource.Distribute();
                var cTarget = Channel.CreateUnbounded<int>();
                await distributor.AddTarget(cTarget);
                cSource.Writer.Complete();
                await cTarget.Reader.Completion;
            }).ToArray();
            foreach (var task in tasks)
                await task;
        }

        [Fact]
        public async Task BasicTest()
        {
            var tasks = Enumerable.Range(0, 50)
                .Select(i => RunRangeTest(Enumerable.Range(0, i)))
                .ToArray();
            foreach (var task in tasks)
                await task;
        }

        private async Task RunRangeTest<T>(IEnumerable<T> source)
        {
            var cSource = Channel.CreateUnbounded<T>();
            var distributor = cSource.Distribute();
            var copyTask = source.CopyTo(cSource, ChannelCompletionMode.CompleteAndPropagateError);

            var channels = Enumerable.Range(0, 2)
                .Select(_ => Channel.CreateUnbounded<T>())
                .ToArray();
            foreach (var channel in channels)
                await distributor.AddTarget(channel);
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
