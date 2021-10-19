using ActualChat.Testing.Collections;
using StackExchange.Redis;

namespace ActualChat.Redis.IntergationTests;

public class RedisTestBase
{
    protected readonly TestSettings _testSettings;
    public RedisTestBase(TestSettings testSettings)
        => _testSettings = testSettings;

    public virtual RedisDb GetRedisDb()
    {
        var redis = ConnectionMultiplexer.Connect(_testSettings.RedisConnectionString);
        return new RedisDb(redis).WithKeyPrefix("tests").WithKeyPrefix(GetType().Name);
    }
}
