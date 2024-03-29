namespace ActualChat.MLSearch.ApiAdapters.ShardWorker;

internal interface IShardIndexResolver<in TShardKey>
{
    int Resolve<TItem>(TItem item, ShardScheme shardScheme) where TItem: IHasShardKey<TShardKey>;
}

internal sealed class ShardIndexResolver<TShardKey> : IShardIndexResolver<TShardKey>
{
    private readonly ShardKeyResolver<TShardKey> _resolver = ShardKeyResolvers.Get<TShardKey>(
            new Requester(null, static _ => $"MLSearch.{nameof(ShardIndexResolver<TShardKey>)}<{typeof(TShardKey).Name}>"))
        ?? throw StandardError.NotFound<ShardKeyResolver<TShardKey>>();

    public int Resolve<TItem>(TItem item, ShardScheme shardScheme) where TItem : IHasShardKey<TShardKey>
        => shardScheme.GetShardIndex(_resolver.Invoke(item.ShardKey));
}
