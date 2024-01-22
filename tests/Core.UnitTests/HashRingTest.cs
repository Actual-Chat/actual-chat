using Microsoft.Toolkit.HighPerformance;

namespace ActualChat.Core.UnitTests;

public class HashRingTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void BasicTest()
    {
        var hr = new HashRing<string>(new [] { "a", "b", "c"}, v => v.GetDjb2HashCode());
        hr.Count.Should().Be(3);

        foreach (var (item, hash) in hr.Items) {
            var x = hr.Get(hash);
            var y = hr.Get(hash, 1);
            var z = hr.Get(hash, 2);
            x.Should().Be(item);
            x.Should().NotBe(y);
            x.Should().NotBe(z);
            y.Should().NotBe(z);

            hr.Get(hash, -6).Should().Be(x);
            hr.Get(hash, -3).Should().Be(x);
            hr.Get(hash, 3).Should().Be(x);
            hr.Get(hash, 6).Should().Be(x);

            hr.Get(hash - 1).Should().Be(x);
            hr.Get(hash + 1).Should().Be(y);

            hr.Span(hash, 0).ToArray().Should().BeEmpty();
            hr.Span(hash, 1).ToArray().Should().Equal(new[] {x});
            hr.Span(hash, 2).ToArray().Should().Equal(new[] {x, y});
            hr.Span(hash, 3).ToArray().Should().Equal(new[] {x, y, z});
            hr.Span(hash, 4).ToArray().Should().Equal(new[] {x, y, z});

            hr.Segment(hash, 0).ToArray().Should().BeEmpty();
            hr.Segment(hash, 1).Should().Equal(new[] {x});
            hr.Segment(hash, 2).Should().Equal(new[] {x, y});
            hr.Segment(hash, 3).Should().Equal(new[] {x, y, z});
            hr.Segment(hash, 4).Should().Equal(new[] {x, y, z});
        }

        hr = HashRing<string>.Empty;
        hr.Span(1, 0).ToArray().Should().BeEmpty();
        hr.Span(1, 1).ToArray().Should().BeEmpty();
        hr.Span(1, 2).ToArray().Should().BeEmpty();

        hr.Segment(1, 0).ToArray().Should().BeEmpty();
        hr.Segment(1, 1).ToArray().Should().BeEmpty();
        hr.Segment(1, 2).ToArray().Should().BeEmpty();
    }
}
