using ActualChat.Kvas;
using MemoryPack;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record UserNavbarSettings : IHasOrigin
{
    public const string KvasKey = nameof(UserNavbarSettings);

    [DataMember, MemoryPackOrder(0)] public string Origin { get; init; } = "";
    [DataMember, MemoryPackOrder(1)] public ApiArray<ChatId> PinnedChats { get; init; }
    [DataMember, MemoryPackOrder(2)] public ApiArray<PlaceId> PlacesOrder { get; init; }
}
