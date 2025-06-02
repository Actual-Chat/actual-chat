using ActualChat.Users;
using MemoryPack;

namespace ActualChat.UI.Blazor.Services;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record LocalClientCompatibility
{
    public const string KvasKey = nameof(LocalClientCompatibility);

    [DataMember, MemoryPackOrder(0)] public string ClientVersion { get; init; } = "";
    [DataMember, MemoryPackOrder(1)] public SystemProperties_ClientCompatibility ClientCompatibility { get; init; }
}
