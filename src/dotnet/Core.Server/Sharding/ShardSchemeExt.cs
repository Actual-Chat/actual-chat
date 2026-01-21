namespace ActualChat.Sharding;

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

    public static bool HasFlags(this ShardScheme? shardScheme, ShardSchemeFlags flags)
        => shardScheme != null && (shardScheme.Flags & flags) == flags;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int? TryGetShardIndex(this ShardScheme? shardScheme, int shardKey)
        => shardScheme is { IsValid: true }
            ? shardKey.PositiveModulo(shardScheme.ShardCount)
            : null;

    public static int? TryGetShardIndex<T>(this ShardScheme? shardScheme, T shardKey)
    {
        var shardKeyResolver = ShardKeyResolvers.Get<T>();
        var intShardKey = shardKeyResolver.Invoke(shardKey);
        return shardScheme.TryGetShardIndex(intShardKey);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetShardIndex(this ShardScheme? shardScheme, int shardKey)
        => shardKey.PositiveModulo(shardScheme.RequireValid().ShardCount);

    public static int GetShardIndex<T>(this ShardScheme? shardScheme, T shardKey)
    {
        var shardKeyResolver = ShardKeyResolvers.Get<T>();
        var intShardKey = shardKeyResolver.Invoke(shardKey);
        return shardScheme.GetShardIndex(intShardKey);
    }
}
