
namespace ActualChat.MLSearch.Engine;

internal sealed record SearchResult<TDocument>(IReadOnlyList<RankedDocument<TDocument>> Documents)
    where TDocument : class;
