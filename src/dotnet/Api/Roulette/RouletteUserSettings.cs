using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;
using MemoryPack;

namespace ActualChat.Roulette;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[ParameterComparer(typeof(ByRefParameterComparer))]
public partial record RouletteUserSettings(
    [property: DataMember, MemoryPackOrder(0)] UserId Id,
    [property: DataMember, MemoryPackOrder(1)] long Version = 0
) : IHasId<UserId>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, MemoryPackOrder(2)] public bool IsEnabled { get; set; } = true;
}
