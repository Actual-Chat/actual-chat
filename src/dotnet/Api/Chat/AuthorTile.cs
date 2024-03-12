using MemoryPack;

namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class AuthorTile
{
    public static AuthorTile Empty { get; } = new ();

    [DataMember, MemoryPackOrder(0)] public Range<int> PositionTileRange { get; init; }
    [DataMember, MemoryPackOrder(1)] public ApiArray<Author> Authors { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsEmpty => Authors.Count == 0;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public AuthorTile() { }
}
