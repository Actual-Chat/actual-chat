using ActualChat.Kvas;
using MemoryPack;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record UserNavbarSettings : IHasOrigin
{
    public const string KvasKey = nameof(UserNavbarSettings);

    [DataMember, MemoryPackOrder(1), MemoryPackInclude]
    private ApiArray<LegacyId> LegacyPinnedChats {
        get => PinnedChats.Select(x => LegacyId.Parse(x.Value)).ToApiArray();
        init => PinnedChats = value.Select(x => ChatId.Parse(x.Value)).ToArray()!;
    }

    [DataMember, MemoryPackOrder(2), MemoryPackInclude]
    private ApiArray<LegacyId> LegacyPlacesOrder {
        get => PlacesOrder.Select(x => LegacyId.Parse(x.Value)).ToApiArray();
        init => PlacesOrder = value.Select(x => PlaceId.Parse(x.Value)).ToArray()!;
    }

    [DataMember, MemoryPackOrder(0)] public string Origin { get; init; } = "";

    // Actually used properties
    [IgnoreDataMember, MemoryPackIgnore]
    public ChatId[] PinnedChats { get; init; } = [];
    [IgnoreDataMember, MemoryPackIgnore]
    public PlaceId[] PlacesOrder { get; init; } = [];
}
