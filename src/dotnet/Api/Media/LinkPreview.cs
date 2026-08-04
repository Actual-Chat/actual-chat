using System.Text;
using ActualChat.Hashing;
using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

namespace ActualChat.Media;

/// <summary>
/// Represents a preview of a linked URL with title, description, and thumbnail.
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MessagePackObject]
public sealed partial record LinkPreview : IHasId<Symbol>, IHasVersion<long>, IHasMetadata, IRequirementTarget
{
    [DataMember, Key(0)] public required Symbol Id { get; init; }
    [DataMember, Key(8)] public long Version { get; init; }
    [DataMember, Key(1)] public string Url { get; init; } = "";
    [DataMember, Key(2)] public MediaId? PreviewMediaId { get; init; }
    [DataMember, Key(3)] public string Title { get; init; } = "";
    [DataMember, Key(4)] public string Description { get; init; } = "";
    [DataMember, Key(5)] public Moment CreatedAt { get; init; }
    [DataMember, Key(6)] public Moment ModifiedAt { get; init; }
    [DataMember, Key(7)] public Media? PreviewMedia { get; init; } // Populated only on reads
    [DataMember, Key(9)] public MetadataBag Metadata { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public int VideoWidth {
        get => this.GetMetadataValue<int>();
        init => this.SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public int VideoHeight {
        get => this.GetMetadataValue<int>();
        init => this.SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public string VideoUrl {
        get => this.GetMetadataValue<string>();
        init => this.SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public string VideoSite {
        get => this.GetMetadataValue<string>();
        init => this.SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsEmpty => Title.IsNullOrEmpty() && Description.IsNullOrEmpty() && PreviewMediaId == null;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
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
