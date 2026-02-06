namespace ActualChat;

/// <summary>
/// Marker interface for types that have a shard key.
/// </summary>
public interface IHasShardKey;

/// <summary>
/// Indicates an object has a shard key of type <typeparamref name="T"/>.
/// </summary>
public interface IHasShardKey<out T> : IHasShardKey
{
    T ShardKey { get; }
}
