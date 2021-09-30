namespace ActualChat.Redis;

public static class RedisDbExt
{
    public static RedisPubSub GetPubSub(this RedisDb redisDb, string key)
        => new(redisDb, key);

    public static RedisQueue<T> GetQueue<T>(this RedisDb redisDb, string key, RedisQueue<T>.Options? settings = null)
        => new(redisDb, key, settings);

    public static RedisStreamer<T> GetStreamer<T>(this RedisDb redisDb, string key, RedisStreamer<T>.Options? settings = null)
        => new(redisDb, key, settings);
}
