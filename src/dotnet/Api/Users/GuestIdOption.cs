namespace ActualChat.Users;

/// <summary>
/// Session option storing the guest user ID.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record GuestIdOption(
    [property: DataMember(Order = 0), Key(0)] UserId GuestId
    );
