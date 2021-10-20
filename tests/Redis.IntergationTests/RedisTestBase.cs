using ActualChat.Testing.Collections;
using StackExchange.Redis;

namespace ActualChat.Redis.IntergationTests;

public class RedisTestBase
{
    protected TestSettings Settings { get; }

    public RedisTestBase(TestSettings settings)
        => Settings = settings;

    public virtual RedisDb GetRedisDb()
    {
        var redis = ConnectionMultiplexer.Connect(Settings.RedisConfiguration);
        return new RedisDb(redis).WithKeyPrefix("tests").WithKeyPrefix(GetType().Name);
    }
}
