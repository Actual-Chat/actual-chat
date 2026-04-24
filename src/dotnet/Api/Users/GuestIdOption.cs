namespace ActualChat.Users;

/// <summary>
/// Session option storing the guest user ID.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record GuestIdOption(
    [property: DataMember(Order = 0), MemoryPackOrder(0), Key(0)] UserId GuestId
    );
