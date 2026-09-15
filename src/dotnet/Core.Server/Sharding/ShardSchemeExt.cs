namespace ActualChat.Sharding;

/// <summary>
/// Extension methods for <see cref="ShardScheme"/>.
/// </summary>
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
    public static int? TryGetShardIndex(this ShardScheme? shardScheme, ShardKey shardKey)
        => shardScheme is { IsValid: true }
            ? unchecked((int)shardKey.Value).PositiveModulo(shardScheme.ShardCount)
            : null;

    public static int? TryGetShardIndex<T>(this ShardScheme? shardScheme, T shardKey)
    {
        var shardKeyResolver = ShardKeyResolvers.Get<T>();
        var resolvedKey = shardKeyResolver.Invoke(shardKey);
        return shardScheme.TryGetShardIndex(resolvedKey);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetShardIndex(this ShardScheme? shardScheme, ShardKey shardKey)
        // Keep existing assignments for the current 12-shard schemes when the hash's high bit is set.
        => unchecked((int)shardKey.Value).PositiveModulo(shardScheme.RequireValid().ShardCount);

    public static int GetShardIndex<T>(this ShardScheme? shardScheme, T shardKey)
    {
        var shardKeyResolver = ShardKeyResolvers.Get<T>();
        var resolvedKey = shardKeyResolver.Invoke(shardKey);
        return shardScheme.GetShardIndex(resolvedKey);
    }
}
