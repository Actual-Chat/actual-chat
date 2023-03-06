using Stl.Fusion.Blazor;
using Stl.Versioning;

namespace ActualChat.Medias;

[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract]
public sealed record Media(
    [property: DataMember] MediaId Id,
    [property: DataMember] long Version = 0
    ) : IHasId<MediaId>, IHasVersion<long>, IRequirementTarget
{
    [DataMember] public string ContentId { get; init; } = "";
    [DataMember] public string FileName { get; init; } = "";
    [DataMember] public string ContentType { get; set; } = "";
    [DataMember] public long Length { get; set; }
    [DataMember] public bool IsRemoved { get; set; }
}
