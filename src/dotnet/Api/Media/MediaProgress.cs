using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

namespace ActualChat.Media;

[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MessagePackObject]
public sealed partial record MediaProgress(
    [property: DataMember, Key(0)] MediaId Id,
    [property: DataMember, Key(1)] long Version,
    [property: DataMember, Key(2)] MediaProcessingStage Stage,
    [property: DataMember, Key(3)] double StageProgress,
    [property: DataMember, Key(4)] string? Error = null
) : IHasId<MediaId>, IHasVersion<long>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember]
    public bool HasFailed => !Error.IsNullOrEmpty();

    // This record relies on referential equality
    public bool Equals(MediaProgress? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
