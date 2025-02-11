namespace ActualChat.Redis.Module;

public sealed class RedisSettings
{
    public string DefaultRedis { get; set; } = "localhost|{instance.}{context}";
    public string OverrideRedis { get; set; } = "";
    public string MeshLockSubspace { get; set; } = ""; // "?" means "make it random"
    public string MeshLockOptionsPreset { get; set; } = "";
}
