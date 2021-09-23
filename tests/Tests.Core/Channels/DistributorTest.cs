using System;
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
            var cSource = Channel.CreateUnbounded<int>();
            cSource.Writer.Complete();
            var distributor = cSource.Distribute();
            var cTarget = Channel.CreateUnbounded<int>();
            await distributor.AddTarget(cTarget);
            await cTarget.Reader.Completion;
        }

        [Fact]
        public async Task DistributeEmptyChannelTest()
        {
            var cSource = Channel.CreateUnbounded<int>();
            var distributor = cSource.Distribute();
            var cTarget = Channel.CreateUnbounded<int>();
            await distributor.AddTarget(cTarget);
            cSource.Writer.Complete();
            await cTarget.Reader.Completion;
        }


        [Fact]
        public async Task BasicTest()
        {
            await RunTest1(Enumerable.Range(0, 0));
            await RunTest1(Enumerable.Range(0, 3));
        }

        private async Task RunTest1<T>(IEnumerable<T> source)
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
