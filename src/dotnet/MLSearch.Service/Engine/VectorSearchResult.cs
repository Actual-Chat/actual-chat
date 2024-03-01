
namespace ActualChat.MLSearch.Engine;

internal record VectorSearchResult<TDocument>(IReadOnlyList<RankedDocument<TDocument>> Documents)
    where TDocument : class;
