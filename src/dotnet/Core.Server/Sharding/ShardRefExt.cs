namespace ActualChat;

public static class ShardRefExt
{
    public static ShardRef RequireValid(this ShardRef shardRef)
        => shardRef.IsValid ? shardRef
            : throw new ArgumentOutOfRangeException(nameof(shardRef), $"Invalid {nameof(ShardRef)}: {shardRef}.");
}
