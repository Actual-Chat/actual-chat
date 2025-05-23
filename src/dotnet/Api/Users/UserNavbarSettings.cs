using ActualChat.Kvas;
using MemoryPack;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record UserNavbarSettings : IHasOrigin
{
    public const string KvasKey = nameof(UserNavbarSettings);

    [DataMember, MemoryPackOrder(1), MemoryPackInclude]
    private ApiArray<ChatId> LegacyPinnedChats { get; init; }
    [DataMember, MemoryPackOrder(2), MemoryPackInclude]
    private ApiArray<PlaceId> LegacyPlacesOrder { get; init; }

    [DataMember, MemoryPackOrder(0)] public string Origin { get; init; } = "";

    [IgnoreDataMember, MemoryPackIgnore]
    public ChatId[] PinnedChats {
        get => LegacyPinnedChats.Items;
        init => LegacyPinnedChats = ApiArray.New(value);
    }

    [IgnoreDataMember, MemoryPackIgnore]
    public PlaceId[] PlacesOrder {
        get => LegacyPlacesOrder.Items;
        init => LegacyPlacesOrder = ApiArray.New(value);
    }
}
