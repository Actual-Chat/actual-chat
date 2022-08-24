namespace ActualChat.Search;

[DataContract]
public abstract class SearchResult
{
    [DataMember] public string Id { get; }
    [DataMember] public SearchMatch SearchMatch { get; }

    protected SearchResult(string id, SearchMatch searchMatch)
    {
        Id = id;
        SearchMatch = searchMatch;
    }
}
