namespace ActualChat.Redis;

public static class RedisDbExt
{
    public static RedisPubSub GetPubSub(this RedisDb redisDb, string key)
        => new(redisDb, key);
    public static RedisPubSub<T> GetPubSub<T>(this RedisDb redisDb, string key)
        => new(redisDb, key);

    public static RedisQueue<T> GetQueue<T>(this RedisDb redisDb, string key, RedisQueue<T>.Options? settings = null)
        => new(redisDb, key, settings);

    public static RedisStreamer<T> GetStreamer<T>(this RedisDb redisDb, string key, RedisStreamer<T>.Options? settings = null)
        => new(redisDb, key, settings);

    public static RedisHash GetHash(this RedisDb redisDb, string hashKey)
        => new(redisDb, hashKey);

    public static RedisSequenceSet GetSequenceSet(
        this RedisDb redisDb, string hashKey, long autoResetDistance = 10)
        => new(redisDb.GetHash(hashKey), autoResetDistance);
    public static RedisSequenceSet<TScope> GetSequenceSet<TScope>(
        this RedisDb redisDb, string hashKey, long autoResetDistance = 10)
        => new(redisDb.GetHash(hashKey), autoResetDistance);
}
