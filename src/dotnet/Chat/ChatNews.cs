using MemoryPack;
using Stl.Fusion.Blazor;

namespace ActualChat.Chat;

[ParameterComparer(typeof(ByValueParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record struct ChatNews(
    [property: DataMember, MemoryPackOrder(0)] Range<long> TextEntryIdRange,
    [property: DataMember, MemoryPackOrder(1)] ChatEntry? LastTextEntry = null
    ) : IRequirementTarget, ICanBeNone<ChatNews>
{
    public static ChatNews None { get; } = default;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsNone => this == default;
}
