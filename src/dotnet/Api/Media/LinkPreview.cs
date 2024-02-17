using System.Text;
using ActualChat.Hashing;
using MemoryPack;
using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

namespace ActualChat.Media;

[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record LinkPreview : IHasId<Symbol>, IHasVersion<long>, IHasMetadata, IRequirementTarget
{
    private readonly NewtonsoftJsonSerialized<ImmutableOptionSet> _metadata =
        NewtonsoftJsonSerialized.New(ImmutableOptionSet.Empty);

    public static readonly LinkPreview Updating = new();
    public static readonly LinkPreview UseExisting = new();

    [DataMember, MemoryPackOrder(0)] public Symbol Id { get; init; }
    [DataMember, MemoryPackOrder(8)] public long Version { get; init; }
    [DataMember, MemoryPackOrder(1)] public string Url { get; init; } = "";
    [DataMember, MemoryPackOrder(2)] public MediaId PreviewMediaId { get; init; }
    [DataMember, MemoryPackOrder(3)] public string Title { get; init; } = "";
    [DataMember, MemoryPackOrder(4)] public string Description { get; init; } = "";
    [DataMember, MemoryPackOrder(5)] public Moment CreatedAt { get; init; }
    [DataMember, MemoryPackOrder(6)] public Moment ModifiedAt { get; init; }
    [DataMember, MemoryPackOrder(7)] public Media? PreviewMedia { get; init; } // Populated only on reads
    [DataMember, MemoryPackOrder(9)] public string MetadataJson {
#pragma warning disable IL2026
        get => _metadata.Data;
#pragma warning restore IL2026
        init => _metadata.Data = value;
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public ImmutableOptionSet Metadata {
#pragma warning disable IL2026
        get => _metadata.Value;
#pragma warning restore IL2026
        set => _metadata.Value = value;
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public int VideoWidth {
        get => this.GetMetadataValue<int>();
        init => this.SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public int VideoHeight {
        get => this.GetMetadataValue<int>();
        init => this.SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string VideoUrl {
        get => this.GetMetadataValue<string>();
        init => this.SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string VideoSite {
        get => this.GetMetadataValue<string>();
        init => this.SetMetadataValue(value);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsEmpty => Title.IsNullOrEmpty() && Description.IsNullOrEmpty() && PreviewMediaId.IsNone;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsYouTubeVideo
        => string.Equals(VideoSite, "YouTube", StringComparison.Ordinal) && !VideoUrl.IsNullOrEmpty();

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
