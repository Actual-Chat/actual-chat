using MemoryPack;
using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial record ChatNews(
    [property: DataMember, MemoryPackOrder(0)] Range<long> TextEntryIdRange,
    [property: DataMember, MemoryPackOrder(1)] ChatEntry? LastTextEntry = null);
