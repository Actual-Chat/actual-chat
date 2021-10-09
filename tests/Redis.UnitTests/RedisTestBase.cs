using StackExchange.Redis;

namespace ActualChat.Redis.UnitTests;

public class RedisTestBase : TestBase
{
    public RedisTestBase(ITestOutputHelper @out) : base(@out) { }

    public virtual RedisDb GetRedisDb()
    {
        var redis = ConnectionMultiplexer.Connect("localhost");
        return new RedisDb(redis).WithKeyPrefix("tests").WithKeyPrefix(GetType().Name);
    }
}
