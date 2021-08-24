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
            var c = LogCover.Default.Moment;
            c.MinRangeSize.Should().Be(TimeSpan.FromMinutes(3));
            c.RangeSizeFactor.Should().Be(4);
            c.RangeSizes.First().Should().Be(c.MinRangeSize);
            c.RangeSizes.Last().Should().Be(c.MaxRangeSize);
            (c.RangeSizes[1] / c.MinRangeSize).Should().Be(c.RangeSizeFactor);
            c.RangeSizes.Length.Should().Be(11);

            c.GetRangeStart(c.Zero + TimeSpan.FromMinutes(1), 0)
                .Should().Be(c.Zero);
            c.GetRangeStart(c.Zero + TimeSpan.FromMinutes(4), 0)
                .Should().Be(c.Zero + TimeSpan.FromMinutes(3));
            c.GetRangeStart(c.Zero + TimeSpan.FromMinutes(4), 1)
                .Should().Be(c.Zero + TimeSpan.FromMinutes(0));
            c.GetRangeStart(c.Zero + TimeSpan.FromMinutes(25), 1)
                .Should().Be(c.Zero + TimeSpan.FromMinutes(24));
        }

        [Fact]
        public void LongLogCoverTest()
        {
            var c = LogCover.Default.Long;
            c.MinRangeSize.Should().Be(16);
            c.RangeSizeFactor.Should().Be(4);
            c.RangeSizes.First().Should().Be(c.MinRangeSize);
            c.RangeSizes.Last().Should().Be(c.MaxRangeSize);
            (c.RangeSizes[1] / c.MinRangeSize).Should().Be(c.RangeSizeFactor);
            c.RangeSizes.Length.Should().Be(6);

            c.GetRangeStart(1, 0)
                .Should().Be(0);
            c.GetRangeStart(17, 0)
                .Should().Be(16);
            c.GetRangeStart(16, 1)
                .Should().Be(0);
            c.GetRangeStart(257, 1)
                .Should().Be(256);
        }
    }
}
