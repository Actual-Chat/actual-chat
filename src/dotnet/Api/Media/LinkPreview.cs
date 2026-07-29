using System.Text;
using ActualChat.Hashing;
using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

namespace ActualChat.Media;

/// <summary>
/// Represents a preview of a linked URL with title, description, and thumbnail.
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record LinkPreview : IHasId<Symbol>, IHasVersion<long>, IHasMetadata, IRequirementTarget
{
    [DataMember, MemoryPackOrder(0), Key(0)] public required Symbol Id { get; init; }
    [DataMember, MemoryPackOrder(8), Key(8)] public long Version { get; init; }
    [DataMember, MemoryPackOrder(1), Key(1)] public string Url { get; init; } = "";
    [DataMember, MemoryPackOrder(2), Key(2)] public MediaId? PreviewMediaId { get; init; }
    [DataMember, MemoryPackOrder(3), Key(3)] public string Title { get; init; } = "";
    [DataMember, MemoryPackOrder(4), Key(4)] public string Description { get; init; } = "";
    [DataMember, MemoryPackOrder(5), Key(5)] public Moment CreatedAt { get; init; }
    [DataMember, MemoryPackOrder(6), Key(6)] public Moment ModifiedAt { get; init; }
    [DataMember, MemoryPackOrder(7), Key(7)] public Media? PreviewMedia { get; init; } // Populated only on reads
    [DataMember, MemoryPackOrder(10), Key(9)] public MetadataBag Metadata { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public int VideoWidth {
        get => this.GetMetadataValue<int>();
        init => this.SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public int VideoHeight {
        get => this.GetMetadataValue<int>();
        init => this.SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public string VideoUrl {
        get => this.GetMetadataValue<string>();
        init => this.SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public string VideoSite {
        get => this.GetMetadataValue<string>();
        init => this.SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public bool IsEmpty => Title.IsNullOrEmpty() && Description.IsNullOrEmpty() && PreviewMediaId == null;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public bool IsYouTubeVideo
        => VideoSite == "YouTube" && !VideoUrl.IsNullOrEmpty();

    public static Symbol ComposeId(string url)
        => url.IsNullOrEmpty()
            ? Symbol.Empty
            : url.Hash(Encoding.UTF8).SHA256().AlphaNumeric();

    // This record relies on referential equality
    public bool Equals(LinkPreview? other)
        => ReferenceEquals(this, other);
    public override int GetHashCode()
        => RuntimeHelpers.GetHashCode(this);
}
