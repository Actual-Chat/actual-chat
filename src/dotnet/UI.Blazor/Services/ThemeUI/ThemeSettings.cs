using ActualChat.Kvas;
using MemoryPack;

namespace ActualChat.UI.Blazor.Services;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record ThemeSettings(
    [property: DataMember, MemoryPackOrder(0)] Theme? Theme,
    [property: DataMember, MemoryPackOrder(1)] string Origin = ""
    ) : IHasOrigin;
