namespace ActualChat.MLSearch.Engine;

internal record struct RankedDocument<TDocument>(double? Rank, TDocument Document)
    : ICanBeNone<RankedDocument<TDocument>>
    where TDocument : class
{
    public static RankedDocument<TDocument> None { get; } = default;

    public bool IsNone => ReferenceEquals(Document, null) && !Rank.HasValue;
}
