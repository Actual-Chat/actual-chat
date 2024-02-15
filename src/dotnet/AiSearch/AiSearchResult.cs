using MemoryPack;
using ActualChat.Search;

namespace ActualChat.AiSearch;

// Represents an individual result of a search execution
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class AiSearchResult : SearchResult
{
    public AiSearchResult(string id, SearchMatch searchMatch) : base(id, searchMatch)
    {
    }
}
