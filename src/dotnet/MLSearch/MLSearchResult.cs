using MemoryPack;
using ActualChat.Search;

namespace ActualChat.MLSearch;

// Represents an individual result of a search execution
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class MLSearchResult : SearchResult
{
    public MLSearchResult(string id, SearchMatch searchMatch) : base(id, searchMatch)
    {
    }
}
