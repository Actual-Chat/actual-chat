
namespace ActualChat.MLSearch.Engine;

internal record SearchResult<TDocument>(IReadOnlyList<RankedDocument<TDocument>> Documents)
    where TDocument : class;
