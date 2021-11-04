using StackExchange.Redis;
using Stl.Redis;

namespace ActualChat.Redis.IntegrationTests;

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
