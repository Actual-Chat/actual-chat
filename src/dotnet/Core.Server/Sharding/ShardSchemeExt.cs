namespace ActualChat;

public static class ShardSchemeExt
{
    public static ShardScheme RequireValid(this ShardScheme? shardScheme)
    {
        if (shardScheme == null)
            throw new ArgumentOutOfRangeException(nameof(shardScheme), $"{nameof(ShardScheme)} is null.");
        if (!shardScheme.IsValid)
            throw new ArgumentOutOfRangeException(nameof(shardScheme), $"Invalid {nameof(ShardScheme)}: {shardScheme}.");

        return shardScheme;
    }

    public static int? TryGetShardIndex(this ShardScheme? shardScheme, int shardKey)
        => shardScheme is { IsValid: true }
            ? shardKey.Mod(shardScheme.ShardCount)
            : null;

    public static int GetShardIndex(this ShardScheme? shardScheme, int shardKey)
        => shardKey.Mod(shardScheme.RequireValid().ShardCount);

    public static bool HasFlags(this ShardScheme? shardScheme, ShardSchemeFlags flags)
        => shardScheme != null && (shardScheme.Flags & flags) == flags;
}
