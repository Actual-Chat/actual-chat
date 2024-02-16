namespace ActualChat.Core.UnitTests.Collections;

public class LruCacheTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void BasicTest()
    {
        var c = new LruCache<int, int>(2);
        c.Capacity.Should().Be(2);
        c.Count.Should().Be(0);

        c.TryGetValue(1, out _).Should().BeFalse();
        c.GetValueOrDefault(1).Should().Be(0);

        c[1] = 1;
        c.Count.Should().Be(1);
        c.GetValueOrDefault(1).Should().Be(1);
        c.TryAdd(1, 2).Should().BeFalse();
        c.GetValueOrDefault(1).Should().Be(1);

        c[2] = 2;
        c.Count.Should().Be(2);
        c.GetValueOrDefault(1).Should().Be(1);
        c.GetValueOrDefault(2).Should().Be(2);

        c[3] = 3;
        c.Count.Should().Be(2);
        c.GetValueOrDefault(1).Should().Be(0);
        c.GetValueOrDefault(2).Should().Be(2);

        c[4] = 4;
        c.Count.Should().Be(2);
        c.GetValueOrDefault(1).Should().Be(0);
        c.GetValueOrDefault(2).Should().Be(2);
        c.GetValueOrDefault(3).Should().Be(0);
        c.GetValueOrDefault(4).Should().Be(4);

        c.Remove(4);
        c.Count.Should().Be(1);
        c.GetValueOrDefault(2).Should().Be(2);
        c.GetValueOrDefault(4).Should().Be(0);

        c.Clear();
        c.Count.Should().Be(0);
        c.GetValueOrDefault(2).Should().Be(0);
    }
}
