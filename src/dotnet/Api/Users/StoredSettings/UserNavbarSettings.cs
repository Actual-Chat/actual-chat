using ActualChat.Kvas;

namespace ActualChat.Users;

/// <summary>
/// User preferences for navigation bar including pinned chats and place ordering.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record UserNavbarSettings : StoredSettings, IHasOrigin, IHasKvasKey<UserNavbarSettings>
{
    public static string KvasKey => nameof(UserNavbarSettings);
    [DataMember, MemoryPackOrder(0), Key(0)] public string Origin { get; init; } = "";
    [DataMember, MemoryPackOrder(1), Key(1)] public ChatId[] PinnedChats { get; init; } = [];
    [DataMember, MemoryPackOrder(2), Key(2)] public PlaceId[] PlacesOrder { get; init; } = [];
    [DataMember, MemoryPackOrder(3), Key(3)] public NavbarPlacement NarrowPlacement { get; init; }
    [DataMember, MemoryPackOrder(4), Key(4)] public NavbarPlacement WidePlacement { get; init; }
    [DataMember, MemoryPackOrder(5), Key(5)] public bool IsAccountButtonFirst { get; init; }
    [DataMember, MemoryPackOrder(6), Key(6)] public bool IsMultiRow { get; init; }

    public NavbarPlacement GetPlacement(bool isNarrow)
        => isNarrow ? NarrowPlacement : WidePlacement;
}
