using System;
using System.Linq;
using FluentAssertions;
using Stl.Testing;
using Xunit;
using Xunit.Abstractions;

namespace ActualChat.Tests
{
    public class TimeLogCoverTest : TestBase
    {
        public TimeLogCoverTest(ITestOutputHelper @out) : base(@out) { }

        [Fact]
        public void DefaultTest()
        {
            var tlc = TimeLogCover.Default;
            tlc.MinSpanSize.Should().Be(TimeSpan.FromMinutes(3));
            tlc.SpanSizes.First().Should().Be(tlc.MinSpanSize);
            tlc.SpanSizes.Last().Should().Be(tlc.MaxSpanSize);
            (tlc.SpanSizes[1] / tlc.MinSpanSize).Should().Be(4);
            tlc.SpanSizes.Length.Should().Be(11);

            tlc.GetSpanStart(tlc.ZeroPoint + TimeSpan.FromMinutes(1), 0)
                .Should().Be(tlc.ZeroPoint);
            tlc.GetSpanStart(tlc.ZeroPoint + TimeSpan.FromMinutes(4), 0)
                .Should().Be(tlc.ZeroPoint + TimeSpan.FromMinutes(3));
            tlc.GetSpanStart(tlc.ZeroPoint + TimeSpan.FromMinutes(4), 1)
                .Should().Be(tlc.ZeroPoint + TimeSpan.FromMinutes(0));
            tlc.GetSpanStart(tlc.ZeroPoint + TimeSpan.FromMinutes(25), 1)
                .Should().Be(tlc.ZeroPoint + TimeSpan.FromMinutes(24));
        }
    }
}
