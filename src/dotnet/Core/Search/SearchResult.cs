using MemoryPack;

namespace ActualChat.Search;

[DataContract]
public abstract class SearchResult
{
    [DataMember, MemoryPackOrder(0)] public string Id { get; }
    [DataMember, MemoryPackOrder(1)] public SearchMatch SearchMatch { get; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, MemoryPackIgnore]
    public string Text => SearchMatch.Text;

    protected SearchResult(string id, SearchMatch searchMatch)
    {
        Id = id;
        SearchMatch = searchMatch;
    }
}
