namespace ActualChat;

public interface IHasShardKey;

public interface IHasShardKey<out T> : IHasShardKey
{
    T ShardKey { get; }
}
