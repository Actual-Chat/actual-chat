
namespace ActualChat.MLSearch.Engine;

public sealed record SearchResult<TDocument>(IReadOnlyList<RankedDocument<TDocument>> Documents)
    where TDocument : class;
