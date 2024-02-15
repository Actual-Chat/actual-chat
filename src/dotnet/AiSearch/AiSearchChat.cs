using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;
using MemoryPack;

namespace ActualChat.AiSearch;

[ParameterComparer(typeof(ByIdAndVersionParameterComparer<AiSearchChatId, long>))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record AiSearchChat(
    [property: DataMember, MemoryPackOrder(0)] AiSearchChatId Id,
    [property: DataMember, MemoryPackOrder(1)] long Version = 0
    ) : IHasId<AiSearchChatId>, IHasVersion<long>, IRequirementTarget
{
}
