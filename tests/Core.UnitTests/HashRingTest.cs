using Microsoft.Toolkit.HighPerformance;

namespace ActualChat.Core.UnitTests;

public class HashRingTest : TestBase
{
    public HashRingTest(ITestOutputHelper @out) : base(@out) { }

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

            hr.Get(hash, 3).Should().Be(x);
            hr.Get(hash, 6).Should().Be(x);

            hr.Get(hash - 1).Should().Be(x);
            hr.Get(hash + 1).Should().Be(y);

            hr.GetMany(hash, 3).Should().Equal(new[] {x, y, z});
        }

        for (var i = 0; i < 10; i++) {
            foreach (var (_, hash) in hr.Items) {
                var x = hr.Get(hash);
                var y = hr.Get(hash, 1);
                var z = hr.Get(hash, 2);

                hr.GetManyRandom(hash, 0, 0).Should().BeEmpty();
                hr.GetManyRandom(hash, 1, 1).Should().Equal(new[] {x});
                hr.GetManyRandom(hash, 2, 2).Should().BeEquivalentTo(new[] {x, y});
                hr.GetManyRandom(hash, 3, 3).Should().BeEquivalentTo(new[] {x, y, z});
                hr.GetManyRandom(hash, 4, 4).Should().BeEquivalentTo(new[] {x, y, z, x});
            }
        }
    }
}
