namespace ActualChat.Users;

[DataContract]
public sealed record GuestIdOption(
    [property: DataMember(Order = 0)] UserId GuestId
    );
