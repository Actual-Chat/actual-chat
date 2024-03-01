namespace ActualChat.MLSearch.Engine;

internal record struct RankedDocument<TDocument>(double? Rank, TDocument Document)
    where TDocument : class;
