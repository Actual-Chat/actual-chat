using ActualLab.Fusion.Blazor;
using MemoryPack;

namespace ActualChat.Search;

[ParameterComparer(typeof(ByValueParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record IndexedUserContact : IRequirementTarget
{
    [DataMember, MemoryPackOrder(0)] public UserId Id { get; init; }
    [DataMember, MemoryPackOrder(1)] public string FullName { get; init; } = "";
    [DataMember, MemoryPackOrder(2)] public string FirstName { get; init; } = "";
    [DataMember, MemoryPackOrder(3)] public string SecondName { get; init; } = "";
    // TODO: store Version
}
