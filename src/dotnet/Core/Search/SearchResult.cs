using MemoryPack;

namespace ActualChat.Search;

[DataContract]
public abstract class SearchResult(string id, SearchMatch searchMatch)
{
    [DataMember, MemoryPackOrder(0)] public string Id { get; } = id;
    [DataMember, MemoryPackOrder(1)] public SearchMatch SearchMatch { get; } = searchMatch;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string Text => SearchMatch.Text;
}
