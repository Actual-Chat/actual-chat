namespace ActualChat.Redis.Module;

public sealed class RedisSettings
{
    public string DefaultRedis { get; set; } = "127.0.0.1|{instance.}{context}";
    public string OverrideRedis { get; set; } = "";
}
