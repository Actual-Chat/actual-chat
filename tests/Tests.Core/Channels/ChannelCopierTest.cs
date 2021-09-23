using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using ActualChat.Channels;
using FluentAssertions;
using Stl.Channels;
using Xunit;

namespace ActualChat.Tests.Channels
{
    public class ChannelCopierTest
    {
        [Fact]
        public async Task BasicTest()
        {
            await RunTest1(Enumerable.Range(0, 0));
            // await RunTest1(Enumerable.Range(0, 3));
        }

        private async Task RunTest1<T>(IEnumerable<T> source)
        {
            var cSource = Channel.CreateUnbounded<T>();
            var copier = cSource.CreateCopier();
            var copyTask = source.CopyTo(cSource, ChannelCompletionMode.CompleteAndPropagateError);

            var channels = Enumerable.Range(0, 2)
                .Select(_ => Channel.CreateUnbounded<T>())
                .ToArray();
            foreach (var channel in channels)
                await copier.AddTarget(channel);

            await copyTask;
            foreach (var channel in channels) {
                var items = channel.ToAsyncEnumerable().ToEnumerable();
                items.Should().BeEquivalentTo(source);
            }
        }
    }
}
