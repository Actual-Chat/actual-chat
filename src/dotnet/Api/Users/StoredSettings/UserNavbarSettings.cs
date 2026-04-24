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
}
