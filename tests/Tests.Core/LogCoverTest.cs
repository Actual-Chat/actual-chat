using System;
using System.Linq;
using ActualChat.Mathematics;
using FluentAssertions;
using Stl.Testing;
using Xunit;
using Xunit.Abstractions;

namespace ActualChat.Tests
{
    public class LogCoverTest : TestBase
    {
        public LogCoverTest(ITestOutputHelper @out) : base(@out) { }

        [Fact]
        public void MomentLogCoverTest()
        {
            var c = MomentLogCover.Default;
            c.MinSpanSize.Should().Be(TimeSpan.FromMinutes(3));
            c.SpanSizeMultiplier.Should().Be(4);
            c.SpanSizes.First().Should().Be(c.MinSpanSize);
            c.SpanSizes.Last().Should().Be(c.MaxSpanSize);
            (c.SpanSizes[1] / c.MinSpanSize).Should().Be(c.SpanSizeMultiplier);
            c.SpanSizes.Length.Should().Be(11);

            c.GetSpanStart(c.Zero + TimeSpan.FromMinutes(1), 0)
                .Should().Be(c.Zero);
            c.GetSpanStart(c.Zero + TimeSpan.FromMinutes(4), 0)
                .Should().Be(c.Zero + TimeSpan.FromMinutes(3));
            c.GetSpanStart(c.Zero + TimeSpan.FromMinutes(4), 1)
                .Should().Be(c.Zero + TimeSpan.FromMinutes(0));
            c.GetSpanStart(c.Zero + TimeSpan.FromMinutes(25), 1)
                .Should().Be(c.Zero + TimeSpan.FromMinutes(24));
        }

        [Fact]
        public void LongLogCoverTest()
        {
            var c = LongLogCover.Default;
            c.MinSpanSize.Should().Be(16);
            c.SpanSizeMultiplier.Should().Be(4);
            c.SpanSizes.First().Should().Be(c.MinSpanSize);
            c.SpanSizes.Last().Should().Be(c.MaxSpanSize);
            (c.SpanSizes[1] / c.MinSpanSize).Should().Be(c.SpanSizeMultiplier);
            c.SpanSizes.Length.Should().Be(6);

            c.GetSpanStart(1, 0)
                .Should().Be(0);
            c.GetSpanStart(17, 0)
                .Should().Be(16);
            c.GetSpanStart(16, 1)
                .Should().Be(0);
            c.GetSpanStart(257, 1)
                .Should().Be(256);
        }
    }
}
