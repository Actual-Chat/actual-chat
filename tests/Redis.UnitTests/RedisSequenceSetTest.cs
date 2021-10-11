namespace ActualChat.Redis.UnitTests;

public class RedisSequenceSetTest : RedisTestBase
{
    public RedisSequenceSetTest(ITestOutputHelper @out) : base(@out) { }

    [Fact]
    public async Task BasicTest()
    {
        var set = GetRedisDb().GetSequenceSet("seq");
        await set.Clear();

        (await set.Next("a")).Should().Be(1);
        (await set.Next("a")).Should().Be(2);
        (await set.Next("a", 5)).Should().Be(6);
        (await set.Next("a", 1000_000_000).WithTimeout(TimeSpan.FromMilliseconds(100)))
            .Should().Be(Option.Some(1000_000_001L)); // Auto-reset test
        await set.Reset("a", 10);
        (await set.Next("a", 5)).Should().Be(11);
    }
}
