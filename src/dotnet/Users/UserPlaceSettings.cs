using MemoryPack;

namespace ActualChat.Users;


// TODO: refactor this into a component. Make it attachable to any entity like places/chats/contacts/etc.
/// <summary>
/// This setting is responsible to store per item settings for the Places service.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record UserPlaceSettings
{
    public static readonly UserPlaceSettings Default = new();

    public static string GetKvasKey(string placeId) => $"@UserPlaceSettings({placeId})";
    /// <summary>
    /// Adds an information to order places lists in UI.
    /// Exact meaning is UI implementation dependent.
    /// Suggested usage:
    /// - Order places by OrderingHint descending.
    /// - Set OrderingHint to the current timestamp
    ///   when place added into the user space.
    ///   Timestamp must be truncated into the int space.
    /// - Set places ordering hints to reorder per user
    ///   drag-n-drop actions: current timestamp + index.
    /// </summary>
    [DataMember, MemoryPackOrder(0)] public int OrderingHint { get; init; }
}
