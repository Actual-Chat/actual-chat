namespace ActualChat.MLSearch;

internal record VectorSearchResult(IReadOnlyList<VectorSearchRankedDocument> Documents);
