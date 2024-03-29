using System.Text;
using ActualChat.Hashing;
using MemoryPack;
using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

[Obsolete("2023.10: Use ActualChat.Media.ILinkPreviews instead.")]
public interface ILinkPreviews : IComputeService
{
    [ComputeMethod]
    Task<bool> IsEnabled();
    [ComputeMethod]
    Task<LinkPreview?> Get(Symbol id, CancellationToken cancellationToken);
}

[Obsolete("2023.10: Use ActualChat.Media.LinkPreview instead.")]
[ParameterComparer(typeof(ByValueParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record LinkPreview : IHasId<Symbol>, IRequirementTarget
{
    public static readonly LinkPreview None = new ();
    public static readonly LinkPreview Loading = new () { Id = "__LOADING__" };
    [DataMember, MemoryPackOrder(0)] public Symbol Id { get; init; }
    [DataMember, MemoryPackOrder(1)] public string Url { get; init; } = "";
    [DataMember, MemoryPackOrder(2)] public MediaId PreviewMediaId { get; init; }
    [DataMember, MemoryPackOrder(3)] public string Title { get; init; } = "";
    [DataMember, MemoryPackOrder(4)] public string Description { get; init; } = "";
    [DataMember, MemoryPackOrder(5)] public Moment CreatedAt { get; init; }
    [DataMember, MemoryPackOrder(6)] public Moment ModifiedAt { get; init; }
    [DataMember, MemoryPackOrder(7)] public Media.Media? PreviewMedia { get; init; } // populated only on reads
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsEmpty => PreviewMediaId.IsNone && Title.IsNullOrEmpty() && Description.IsNullOrEmpty();

    public static Symbol ComposeId(string url)
        => url.Hash(Encoding.UTF8).SHA256().AlphaNumeric();
}
