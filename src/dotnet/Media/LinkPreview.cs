using MemoryPack;
using Stl.Fusion.Blazor;
using Stl.Versioning;

namespace ActualChat.Media;

[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record LinkPreview : IHasId<Symbol>, IHasVersion<long>, IRequirementTarget
{
    public static readonly LinkPreview Updating = new();

    [DataMember, MemoryPackOrder(0)] public Symbol Id { get; init; }
    [DataMember, MemoryPackOrder(8)] public long Version { get; init; }
    [DataMember, MemoryPackOrder(1)] public string Url { get; init; } = "";
    [DataMember, MemoryPackOrder(2)] public MediaId PreviewMediaId { get; init; }
    [DataMember, MemoryPackOrder(3)] public string Title { get; init; } = "";
    [DataMember, MemoryPackOrder(4)] public string Description { get; init; } = "";
    [DataMember, MemoryPackOrder(5)] public Moment CreatedAt { get; init; }
    [DataMember, MemoryPackOrder(6)] public Moment ModifiedAt { get; init; }
    [DataMember, MemoryPackOrder(7)] public Media? PreviewMedia { get; init; } // Populated only on reads

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsEmpty => Title.IsNullOrEmpty() && Description.IsNullOrEmpty() && PreviewMediaId.IsNone;

    public static Symbol ComposeId(string url)
        => url.IsNullOrEmpty()
            ? Symbol.Empty
            : url.GetSHA256HashCode(HashEncoding.AlphaNumeric);

    // This record relies on referential equality
    public bool Equals(LinkPreview? other)
        => ReferenceEquals(this, other);
    public override int GetHashCode()
        => RuntimeHelpers.GetHashCode(this);
}
