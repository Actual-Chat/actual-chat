using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;
using MemoryPack;

namespace ActualChat.MLSearch;

[ParameterComparer(typeof(ByIdAndVersionParameterComparer<MLSearchChatId, long>))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record MLSearchChat(
    [property: DataMember, MemoryPackOrder(0)] MLSearchChatId Id,
    [property: DataMember, MemoryPackOrder(1)] long Version = 0
    ) : IHasId<MLSearchChatId>, IHasVersion<long>, IRequirementTarget
{
}
