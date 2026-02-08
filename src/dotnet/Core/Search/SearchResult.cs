
namespace ActualChat.Search;

/// <summary>
/// Base class for search results with ID and match information.
/// </summary>
[DataContract]
public abstract class SearchResult(string id, SearchMatch searchMatch)
{
    [DataMember, MemoryPackOrder(0)] public string Id { get; } = id;
    [DataMember, MemoryPackOrder(1)] public SearchMatch SearchMatch { get; } = searchMatch;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public string Text => SearchMatch.Text;
}
