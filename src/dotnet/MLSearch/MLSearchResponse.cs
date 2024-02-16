using MemoryPack;
using ActualChat.Search;

namespace ActualChat.MLSearch;

// Represents an individual result of a search execution
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class MLSearchResponse : SearchResult
{
    public MLSearchResponse(string id, SearchMatch searchMatch) : base(id, searchMatch)
    {
    }
}
