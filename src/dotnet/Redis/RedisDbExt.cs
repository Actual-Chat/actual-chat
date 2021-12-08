using Stl.Redis;

namespace ActualChat.Redis;

public static class RedisDbExt
{
    public static RedisStreamer<T> GetStreamer<T>(this RedisDb redisDb, string key, RedisStreamer<T>.Options? settings = null)
        => new(redisDb, key, settings);

    public static RedisQueue<T> GetQueue<T>(this RedisDb redisDb, string key, RedisQueue<T>.Options? settings = null)
        => new (redisDb, key, settings);
}
