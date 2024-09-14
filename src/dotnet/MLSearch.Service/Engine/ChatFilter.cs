namespace ActualChat.MLSearch.Engine;

public sealed class ChatFilter: IQueryFilter
{
    public bool IncludePublic { get; set; }
    public ISet<PlaceId> PlaceIds { get;} = new HashSet<PlaceId>();
    public ISet<ChatId> ChatIds { get;} = new HashSet<ChatId>();

    public void Apply(IQueryBuilder queryBuilder) => queryBuilder.ApplyChatFilter(this);
}
