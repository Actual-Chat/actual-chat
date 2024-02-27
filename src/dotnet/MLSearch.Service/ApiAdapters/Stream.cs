using System.Diagnostics.Contracts;
using ActualChat.MLSearch.SearchEngine.OpenSearch.Stream;

namespace ActualChat.MLSearch.ApiAdapters;

// TODO: Ask/resolve the following:
// Note: It seems that ShardScheme MUST be defined
// in the src/dotnet/Core.Server/Sharding/ShardScheme.cs
// and added into ById dictionary there.
internal sealed class StreamShardScheme<T>() :
        ShardScheme($"{nameof(StreamShardScheme<T>)}:{nameof(T)}", T.ShardCount),
        IShardScheme<StreamShardScheme<T>>
    where T : ISharded
{
    public static StreamShardScheme<T> Instance { get; } = new ();
}


// TODO: remove ChatEntriesStream. It is added as a prototype implementation.
internal class Stream<T>(IServiceProvider services, T streamTopology): ShardWorker<StreamShardScheme<T>>(services)
where
    T: IStreamTopology
{
    protected override async Task OnRun(int shardIndex, CancellationToken cancellationToken)
    {
        await streamTopology.Execute(shardIndex, ShardScheme.ShardCount, cancellationToken)
            .ConfigureAwait(false);
    }
}
